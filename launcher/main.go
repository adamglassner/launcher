// Ironite Launcher.
// Serves the launcher UI locally and opens it in an app-mode browser window.
// Games are installed and updated from their own GitHub repositories' releases,
// and the launcher updates itself from github.com/adamglassner/launcher.
package main

import (
	"crypto/rand"
	"embed"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"log"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// Set at build time with -ldflags "-X main.version=... -X main.build=...".
var (
	version = "0.0.0-dev"
	build   = "dev"
)

const productName = "Ironite Launcher"

//go:embed web
var webFS embed.FS

//go:embed CHANGELOG.md
var changelog string

type catalogGame struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Subtitle    string   `json:"subtitle,omitempty"`
	Category    string   `json:"category,omitempty"`
	Status      string   `json:"status,omitempty"` // "coming_soon" for games with no repo yet
	Platforms   []string `json:"platforms,omitempty"`
	Players     string   `json:"players,omitempty"`
	Description string   `json:"description,omitempty"`
	Repo        string   `json:"repo,omitempty"`   // GitHub owner/name the game installs from
	Bundle      string   `json:"bundle,omitempty"` // folder under Games\
	Exe         string   `json:"exe,omitempty"`
	Demo        bool     `json:"demo,omitempty"`
	Type        string   `json:"type,omitempty"` // "web" = plays online
	// The player's own games ("local"):
	Group string `json:"group,omitempty"` // category name shown on Non-Ironite Games
	Art   bool   `json:"art,omitempty"`   // has custom cover art
	Store bool   `json:"store,omitempty"` // a Microsoft Store / Xbox app
	URL   string `json:"url,omitempty"`
}

func (g catalogGame) isWeb() bool { return g.Type == "web" }

type catalog struct {
	Schema     int               `json:"schema"`
	Categories []json.RawMessage `json:"categories"`
	Games      []catalogGame     `json:"games"`
}

type gameStats struct {
	PlayCount   int   `json:"playCount"`   // times opened
	PlaySeconds int64 `json:"playSeconds"` // total time played
	LastPlayed  int64 `json:"lastPlayed"`
	InstalledAt int64 `json:"installedAt,omitempty"`
}

type settings struct {
	Stats      map[string]*gameStats     `json:"stats"`
	Installs   map[string]*installRecord `json:"installs,omitempty"`
	AutoUpdate *bool                     `json:"autoUpdate,omitempty"`
	Custom     []*customGame             `json:"custom,omitempty"` // the player's own games
}

type gameState struct {
	Available        bool   `json:"available"` // has a release (or is installed / web)
	Installed        bool   `json:"installed"`
	InstalledVersion string `json:"installedVersion,omitempty"`
	LatestVersion    string `json:"latestVersion,omitempty"`
	Update           bool   `json:"update"`
	Installing       bool   `json:"installing"`
	Updating         bool   `json:"updating"`
	Phase            string `json:"phase,omitempty"`
	Progress         int    `json:"progress"` // 0-100 while installing
	Size             int64  `json:"size,omitempty"`
	Notes            string `json:"notes,omitempty"`
	ReleaseName      string `json:"releaseName,omitempty"`
	ReleaseURL       string `json:"releaseUrl,omitempty"`
	PublishedAt      int64  `json:"publishedAt,omitempty"`
	InstalledAt      int64  `json:"installedAt"`
	Running          bool   `json:"running"`
	SessionStart     int64  `json:"sessionStart,omitempty"`
	PlayCount        int    `json:"playCount"`
	PlaySeconds      int64  `json:"playSeconds"`
	LastPlayed       int64  `json:"lastPlayed"`
	Error            string `json:"error,omitempty"`
	LaunchError      string `json:"launchError,omitempty"`
}

type app struct {
	mu        sync.Mutex
	cat       catalog // the Ironite catalog plus the player's own games
	catJSON   []byte
	catVer    int     // bumps whenever the catalog changes
	baseCat   catalog // the Ironite catalog alone
	baseJSON  []byte
	set       settings
	rel       *releaseCache
	running   map[string]*exec.Cmd
	started   map[string]time.Time
	credited  map[string]time.Time // last time play time was saved, per running game
	jobs      map[string]*installJob
	launchErr map[string]string // why a game didn't start
	token     string
	lastSeen  atomic.Int64
	seen      atomic.Bool

	checking  bool
	lastCheck time.Time
	checkErr  string
	checkBusy bool         // GitHub asked us to slow down (not offline)
	byeAt     atomic.Int64 // when the window said it was closing
	checkNow  chan struct{}
	selfJob   *installJob
}

func dataDir() string { // %APPDATA%\Ironite\Launcher
	d, err := os.UserConfigDir()
	if err != nil {
		d = os.TempDir()
	}
	return filepath.Join(d, "Ironite", "Launcher")
}
func cacheDir() string { // %LOCALAPPDATA%\Ironite\Launcher
	if v := os.Getenv("IRONITE_CACHE_DIR"); v != "" {
		return v
	}
	d, err := os.UserCacheDir()
	if err != nil {
		d = os.TempDir()
	}
	return filepath.Join(d, "Ironite", "Launcher")
}

// migrateOldFolders moves settings, play stats and installed games from the
// folders used before the launcher was named Ironite Launcher.
func migrateOldFolders() {
	for _, dir := range []string{dataDir(), cacheDir()} {
		old := filepath.Join(filepath.Dir(dir), "Casual Game Compilation")
		if _, err := os.Stat(old); err != nil {
			continue
		}
		if _, err := os.Stat(dir); err == nil {
			continue // already have the new folder
		}
		if err := os.Rename(old, dir); err != nil {
			log.Printf("migrate %s: %v", old, err)
		} else {
			log.Printf("migrated %s -> %s", old, dir)
		}
	}
}
func installDir() string {
	exe, err := os.Executable()
	if err != nil {
		return "."
	}
	return filepath.Dir(exe)
}

func (a *app) loadSettings() {
	a.set.Stats = map[string]*gameStats{}
	b, err := os.ReadFile(filepath.Join(dataDir(), "settings.json"))
	if err == nil {
		_ = json.Unmarshal(b, &a.set)
	}
	if a.set.Stats == nil {
		a.set.Stats = map[string]*gameStats{}
	}
	if a.set.Installs == nil {
		a.set.Installs = map[string]*installRecord{}
	}
}
func (a *app) saveSettings() { // caller holds a.mu
	_ = os.MkdirAll(dataDir(), 0o755)
	b, _ := json.MarshalIndent(a.set, "", "  ")
	tmp := filepath.Join(dataDir(), "settings.json.tmp")
	if os.WriteFile(tmp, b, 0o644) == nil {
		_ = os.Rename(tmp, filepath.Join(dataDir(), "settings.json"))
	}
}
func (a *app) autoUpdate() bool { return a.set.AutoUpdate == nil || *a.set.AutoUpdate }

// setCatalog switches to a catalog (built in, or the newer published one).
func (a *app) setCatalog(b []byte) error {
	var c catalog
	if err := json.Unmarshal(b, &c); err != nil {
		return err
	}
	a.baseCat, a.baseJSON = c, b
	defer a.mergeCustom()
	// Games installed by launcher 1.x (unpacked from inside the launcher) keep
	// working and are updated from GitHub like everything else.
	for _, g := range c.Games {
		if g.isWeb() || g.Bundle == "" {
			continue
		}
		rec := a.set.Installs[g.ID]
		if rec != nil {
			if _, err := os.Stat(filepath.Join(rec.Dir, rec.Exe)); err == nil {
				continue
			}
			delete(a.set.Installs, g.ID) // files were removed by hand
		}
		if l := findLegacy(g); l != nil {
			if s := a.set.Stats[g.ID]; s != nil && s.InstalledAt != 0 {
				l.InstalledAt = s.InstalledAt
			}
			a.set.Installs[g.ID] = l
			log.Printf("found %s %s from an earlier launcher", g.ID, l.Version)
		}
	}
	return nil
}

func (a *app) states() map[string]gameState { // caller holds a.mu
	out := map[string]gameState{}
	for _, g := range a.cat.Games {
		st := gameState{Running: a.running[g.ID] != nil}
		if g.isLocal() {
			st.Available, st.Installed = true, true
			if cg := a.customByID(g.ID); cg != nil {
				st.InstalledAt = cg.AddedAt
				if cg.isStore() {
					if cg.Location != "" {
						if _, err := os.Stat(cg.Location); err != nil {
							st.LaunchError = "This Xbox / Microsoft Store game doesn't seem to be installed any more. Reinstall it from the Xbox app or Microsoft Store, or remove it from the launcher."
						}
					}
				} else if _, err := os.Stat(cg.Path); err != nil {
					st.LaunchError = "The game's program wasn't found at " + cg.Path + ". If it moved, remove it from the launcher and add it again."
				}
			}
		} else if g.isWeb() {
			st.Available = g.URL != ""
		} else if g.Repo != "" {
			if rec := a.set.Installs[g.ID]; rec != nil {
				st.Installed, st.InstalledVersion, st.InstalledAt = true, rec.Version, rec.InstalledAt
			}
			if r := a.rel.get(g.Repo); r != nil && r.gameAsset() != nil {
				st.Available = true
				st.LatestVersion = r.Version()
				st.Notes, st.ReleaseName, st.ReleaseURL = r.Body, r.Name, r.HTMLURL
				st.PublishedAt = r.PublishedAt.Unix()
				st.Size = r.gameAsset().Size
				st.Update = st.Installed && newer(st.LatestVersion, st.InstalledVersion)
			}
			if st.Installed {
				st.Available = true
			}
		}
		if !st.Available && !st.Installed && a.set.Stats[g.ID] == nil {
			continue // coming soon
		}
		if st.Running {
			st.SessionStart = a.started[g.ID].Unix()
		}
		var live int64
		if c, ok := a.credited[g.ID]; ok && st.Running {
			live = int64(time.Since(c) / time.Second)
		}
		if j := a.jobs[g.ID]; j != nil {
			if j.err != "" {
				st.Error = j.err
			} else {
				st.Installing, st.Updating, st.Phase = true, j.update, j.getPhase()
				if t := j.total.Load(); t > 0 {
					st.Progress = int(j.done.Load() * 100 / t)
				}
			}
		}
		if e := a.launchErr[g.ID]; e != "" {
			st.LaunchError = e
		}
		if s := a.set.Stats[g.ID]; s != nil {
			st.PlayCount, st.LastPlayed, st.PlaySeconds = s.PlayCount, s.LastPlayed, s.PlaySeconds+live
		}
		out[g.ID] = st
	}
	return out
}

func (a *app) find(id string) (catalogGame, bool) {
	for _, g := range a.cat.Games {
		if g.ID == id {
			return g, true
		}
	}
	return catalogGame{}, false
}

// install downloads the game's latest release and installs it (or updates it).
func (a *app) install(id string) error {
	a.mu.Lock()
	defer a.mu.Unlock()
	g, ok := a.find(id)
	if !ok {
		return errors.New("unknown game")
	}
	if g.isWeb() {
		return errors.New("web games play online and don't need installing")
	}
	if g.isLocal() {
		return errors.New("that game is already on this PC")
	}
	r := a.rel.get(g.Repo)
	if g.Repo == "" || r == nil || r.gameAsset() == nil {
		if a.set.Installs[id] != nil {
			return errors.New("there's no newer version to download right now")
		}
		return errors.New("that game is coming soon")
	}
	if a.running[id] != nil {
		return errors.New("close the game before updating it")
	}
	if j := a.jobs[id]; j != nil && j.err == "" {
		return nil // already installing
	}
	if rec := a.set.Installs[id]; rec != nil && !newer(r.Version(), rec.Version) {
		return nil // up to date
	}
	job := &installJob{update: a.set.Installs[id] != nil}
	job.setPhase("Downloading")
	a.jobs[id] = job
	asset, ver := *r.gameAsset(), r.Version()
	go func() {
		rec, err := func() (*installRecord, error) {
			file, err := download(&asset, job)
			if err != nil {
				return nil, err
			}
			defer os.Remove(file)
			return installFile(g, ver, file, job)
		}()
		a.mu.Lock()
		defer a.mu.Unlock()
		if err != nil {
			log.Printf("install %s %s: %v", id, ver, err)
			job.err = "Install failed: " + err.Error()
			return
		}
		delete(a.jobs, id)
		if old := a.set.Installs[id]; old != nil && old.InstalledAt != 0 {
			rec.InstalledAt = old.InstalledAt // "installed" date is the first install
		}
		a.set.Installs[id] = rec
		s := a.set.Stats[id]
		if s == nil {
			s = &gameStats{}
			a.set.Stats[id] = s
		}
		s.InstalledAt = rec.InstalledAt
		a.saveSettings()
		log.Printf("installed %s %s (%s) in %s", id, ver, rec.Kind, rec.Dir)
	}()
	return nil
}

// uninstall removes a game's files. Saves and settings, which the games keep
// in the user's profile, are not touched.
func (a *app) uninstall(id string) error {
	a.mu.Lock()
	g, ok := a.find(id)
	if !ok {
		a.mu.Unlock()
		return errors.New("unknown game")
	}
	if g.isWeb() {
		a.mu.Unlock()
		return errors.New("web games play online and have nothing to uninstall")
	}
	if g.isLocal() {
		a.mu.Unlock()
		return a.removeCustom(id) // takes it out of the launcher; its files stay
	}
	if a.running[id] != nil {
		a.mu.Unlock()
		return errors.New("close the game before uninstalling it")
	}
	if j := a.jobs[id]; j != nil && j.err == "" {
		a.mu.Unlock()
		return errors.New("the game is still installing")
	}
	delete(a.jobs, id)
	rec := a.set.Installs[id]
	a.mu.Unlock()

	err := removeInstall(g, rec)

	a.mu.Lock()
	defer a.mu.Unlock()
	delete(a.set.Installs, id)
	if s := a.set.Stats[id]; s != nil {
		s.InstalledAt = 0
	}
	a.saveSettings()
	return err
}

func (a *app) launch(id string) error {
	a.mu.Lock()
	g, ok := a.find(id)
	if !ok {
		a.mu.Unlock()
		return errors.New("unknown game")
	}
	if a.running[id] != nil {
		a.mu.Unlock()
		return errors.New("already running")
	}
	if j := a.jobs[id]; j != nil && j.err == "" {
		a.mu.Unlock()
		return errors.New("still installing")
	}
	rec := a.set.Installs[id]
	a.mu.Unlock()

	var cmd *exec.Cmd
	storeDir, storeLaunch := "", false // for Store / Xbox games
	if g.isWeb() {
		// Its own app window and browser profile, kept in %APPDATA% so the
		// game's saved progress survives updates and uninstalls.
		profile := filepath.Join(dataDir(), "WebGames", g.ID)
		_ = os.MkdirAll(profile, 0o755)
		c, err := webGameCommand(g.URL, profile)
		if err != nil {
			return err
		}
		cmd = c
	} else if g.isLocal() {
		a.mu.Lock()
		cg := a.customByID(id)
		var c customGame
		if cg != nil {
			c = *cg
		}
		a.mu.Unlock()
		if cg == nil {
			return errors.New("unknown game")
		}
		if c.isStore() {
			// Windows starts Store / Xbox apps; we then watch for the game's
			// processes in its install folder to count play time.
			cmd = storeAppCommand(c.AppID)
			storeDir, storeLaunch = c.Location, true
		} else {
			if _, err := os.Stat(c.Path); err != nil {
				return errors.New("the game's program wasn't found at " + c.Path)
			}
			cmd = gameCommand(c.Path, filepath.Dir(c.Path))
		}
	} else {
		if rec == nil {
			return errors.New("install the game first")
		}
		exe := filepath.Join(rec.Dir, rec.Exe)
		if _, err := os.Stat(exe); err != nil {
			// Usually antivirus software removed the program. Forget the
			// install so the Install button can download it again.
			log.Printf("launch %s: %s is missing", id, exe)
			a.mu.Lock()
			delete(a.set.Installs, id)
			a.saveSettings()
			a.mu.Unlock()
			return errors.New("the game's program is missing (Windows Security may have removed it); click Install to download it again")
		}
		cmd = gameCommand(exe, filepath.Dir(exe))
	}
	log.Printf("starting %s: %s", id, cmd.Path)
	if err := cmd.Start(); err != nil {
		log.Printf("launch %s: %v", id, err)
		msg := err.Error()
		if strings.Contains(strings.ToLower(msg), "virus") {
			return errors.New("Windows Security blocked the game from starting")
		}
		return fmt.Errorf("the game couldn't start (%s)", msg)
	}
	a.mu.Lock()
	delete(a.launchErr, id)
	a.mu.Unlock()

	start := time.Now()
	a.mu.Lock()
	a.running[id] = cmd
	a.started[id] = start
	a.credited[id] = start
	s := a.set.Stats[id]
	if s == nil {
		s = &gameStats{}
		a.set.Stats[id] = s
	}
	s.PlayCount++
	s.LastPlayed = start.Unix()
	a.saveSettings()
	a.mu.Unlock()

	// Track time played. Saved every minute so a crash loses at most a minute.
	exited := make(chan struct{})
	go func() {
		err := cmd.Wait() // for Store games this is just Explorer handing over
		if storeLaunch {
			waitForStoreGame(storeDir, start)
			close(exited)
			return
		}
		if took := time.Since(start); took < 8*time.Second && !g.isWeb() {
			code := -1
			if cmd.ProcessState != nil {
				code = cmd.ProcessState.ExitCode()
			}
			if code != 0 {
				log.Printf("%s exited after %s with code %d (%v)", id, took.Round(time.Millisecond), code, err)
				a.mu.Lock()
				a.launchErr[id] = fmt.Sprintf("%s closed right after it started (exit code %d). If Windows Security showed a warning, allow the game there, then try again.", g.Title, code)
				a.mu.Unlock()
			}
		}
		close(exited)
	}()
	go func() {
		last := start
		tick := time.NewTicker(time.Minute)
		defer tick.Stop()
		credit := func(now time.Time) {
			a.mu.Lock()
			s.PlaySeconds += int64(now.Sub(last).Round(time.Second) / time.Second)
			s.LastPlayed = now.Unix()
			last = now
			a.credited[id] = now
			a.saveSettings()
			a.mu.Unlock()
		}
		for {
			select {
			case now := <-tick.C:
				credit(now)
			case <-exited:
				credit(time.Now())
				a.mu.Lock()
				delete(a.running, id)
				delete(a.started, id)
				delete(a.credited, id)
				a.mu.Unlock()
				log.Printf("%s closed after %s", id, time.Since(start).Round(time.Second))
				a.requestCheck() // a postponed update can install now
				return
			}
		}
	}()
	return nil
}

// gamesRunning reports whether any game the launcher started is still open.
func (a *app) gamesRunning() bool {
	a.mu.Lock()
	defer a.mu.Unlock()
	return len(a.running) > 0
}

/* ---------- checking GitHub for releases ---------- */

func (a *app) requestCheck() {
	select {
	case a.checkNow <- struct{}{}:
	default:
	}
}

// updateLoop checks every game's repository (and the launcher's own) when the
// launcher starts and every 30 minutes, and installs updates automatically.
func (a *app) updateLoop() {
	for {
		a.checkAll()
		a.autoSelfUpdate()
		a.autoInstallUpdates()
		select {
		case <-a.checkNow:
		case <-time.After(30 * time.Minute):
		}
	}
}

func (a *app) checkAll() {
	a.mu.Lock()
	if a.checking {
		a.mu.Unlock()
		return
	}
	a.checking = true
	a.mu.Unlock()

	a.rel.fetchCatalog()
	a.rel.mu.Lock()
	remote := a.rel.Catalog
	a.rel.mu.Unlock()
	if len(remote) > 0 {
		a.mu.Lock()
		if string(remote) != string(a.baseJSON) {
			if err := a.setCatalog(remote); err != nil {
				log.Printf("published catalog: %v", err)
			} else {
				log.Printf("using the published game catalog")
			}
		}
		a.mu.Unlock()
	}

	a.mu.Lock()
	repos := []string{launcherRepo}
	for _, g := range a.cat.Games {
		if g.Repo != "" && !g.isWeb() {
			repos = append(repos, g.Repo)
		}
	}
	a.mu.Unlock()

	var firstErr error
	sem := make(chan struct{}, 4)
	var wg sync.WaitGroup
	var emu sync.Mutex
	for _, r := range repos {
		wg.Add(1)
		go func(r string) {
			defer wg.Done()
			sem <- struct{}{}
			defer func() { <-sem }()
			if err := a.rel.fetchLatest(r); err != nil {
				log.Printf("check %s: %v", r, err)
				emu.Lock()
				if firstErr == nil {
					firstErr = err
				}
				emu.Unlock()
			}
		}(r)
	}
	wg.Wait()

	a.mu.Lock()
	a.checking = false
	a.lastCheck = time.Now()
	a.checkErr, a.checkBusy = "", false
	if firstErr != nil {
		if errors.Is(firstErr, errRateLimited) {
			a.checkErr, a.checkBusy = errRateLimited.Error(), true
		} else {
			a.checkErr = "Couldn't reach GitHub to check for updates. Check your internet connection."
		}
	}
	a.mu.Unlock()
}

// autoSelfUpdate updates the launcher as soon as a new version is out, like
// the games, unless automatic updates are off or something is in progress.
func (a *app) autoSelfUpdate() {
	a.mu.Lock()
	ok := a.autoUpdate() && a.selfJob == nil && len(a.running) == 0 && a.launcherUpdate() != nil
	for _, j := range a.jobs {
		if j.err == "" {
			ok = false
		}
	}
	a.mu.Unlock()
	if ok {
		log.Printf("updating the launcher automatically")
		logErr("self-update", a.selfUpdate(""))
	}
}

func (a *app) autoInstallUpdates() {
	a.mu.Lock()
	if !a.autoUpdate() {
		a.mu.Unlock()
		return
	}
	var ids []string
	for id, st := range a.states() {
		if st.Update && !st.Running && !st.Installing && st.Error == "" {
			ids = append(ids, id)
		}
	}
	a.mu.Unlock()
	for _, id := range ids {
		log.Printf("updating %s automatically", id)
		logErr("auto-update "+id, a.install(id))
	}
}

/* ---------- the launcher updating itself ---------- */

type launcherUpdate struct {
	Version     string `json:"version"`
	Notes       string `json:"notes"`
	URL         string `json:"url"`
	Downloading bool   `json:"downloading"`
	Progress    int    `json:"progress"`
	Error       string `json:"error,omitempty"`
}

func (a *app) launcherUpdate() *launcherUpdate { // caller holds a.mu
	r := a.rel.get(launcherRepo)
	if r == nil || r.launcherAsset() == nil || !newer(r.Version(), version) {
		return nil
	}
	u := &launcherUpdate{Version: r.Version(), Notes: r.Body, URL: r.HTMLURL}
	if j := a.selfJob; j != nil {
		u.Error = j.err
		u.Downloading = j.err == ""
		if t := j.total.Load(); t > 0 {
			u.Progress = int(j.done.Load() * 100 / t)
		}
	}
	return u
}

// selfUpdate downloads the new launcher setup, starts it silently (it closes
// this launcher, updates it and opens it again) and exits.
func (a *app) selfUpdate(string) error {
	a.mu.Lock()
	defer a.mu.Unlock()
	r := a.rel.get(launcherRepo)
	if r == nil || r.launcherAsset() == nil || !newer(r.Version(), version) {
		return errors.New("the launcher is up to date")
	}
	if len(a.running) > 0 {
		return errors.New("close your games before updating the launcher")
	}
	for _, j := range a.jobs {
		if j.err == "" {
			return errors.New("wait for downloads to finish before updating the launcher")
		}
	}
	if a.selfJob != nil && a.selfJob.err == "" {
		return nil
	}
	job := &installJob{}
	a.selfJob = job
	asset := *r.launcherAsset()
	go func() {
		file, err := download(&asset, job)
		if err == nil {
			err = startSelfUpdate(file)
		}
		if err != nil {
			a.mu.Lock()
			job.err = "Update failed: " + err.Error()
			a.mu.Unlock()
			log.Printf("self-update: %v", err)
			return
		}
		log.Printf("launcher update %s started; exiting", asset.Name)
		time.Sleep(1500 * time.Millisecond) // let the page show "Restarting"
		os.Exit(0)
	}()
	return nil
}

/* ---------- HTTP ---------- */

func (a *app) routes() http.Handler {
	mux := http.NewServeMux()
	sub, _ := fs.Sub(webFS, "web")
	files := http.FileServer(http.FS(sub))
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Cache-Control", "no-store")
		files.ServeHTTP(w, r)
	})
	writeJSON := func(w http.ResponseWriter, v any) {
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(v)
	}
	guard := func(h http.HandlerFunc) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			if r.Header.Get("X-Launcher-Token") != a.token {
				http.Error(w, "forbidden", http.StatusForbidden)
				return
			}
			a.seen.Store(true)
			a.lastSeen.Store(time.Now().UnixMilli())
			h(w, r)
		}
	}
	mux.HandleFunc("/api/state", guard(func(w http.ResponseWriter, r *http.Request) {
		a.mu.Lock()
		defer a.mu.Unlock()
		var last int64
		if !a.lastCheck.IsZero() {
			last = a.lastCheck.Unix()
		}
		writeJSON(w, map[string]any{
			"version": version, "build": build, "installDir": installDir(), "gamesDir": gamesRoot(),
			"games": a.states(), "autoUpdate": a.autoUpdate(),
			"checking": a.checking, "lastCheck": last, "checkError": a.checkErr, "checkBusy": a.checkBusy,
			"launcherUpdate": a.launcherUpdate(), "catalogSize": a.catVer,
		})
	}))
	mux.HandleFunc("/api/ping", guard(func(w http.ResponseWriter, r *http.Request) { _, _ = io.WriteString(w, "ok") }))
	// sendBeacon from the page as its window closes (it can't send headers).
	mux.HandleFunc("/api/bye", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Query().Get("t") == a.token {
			a.byeAt.Store(time.Now().UnixMilli())
		}
		w.WriteHeader(http.StatusNoContent)
	})
	mux.HandleFunc("/api/catalog", guard(func(w http.ResponseWriter, r *http.Request) {
		a.mu.Lock()
		b := a.catJSON
		a.mu.Unlock()
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write(b)
	}))
	mux.HandleFunc("/api/changelog", guard(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/markdown; charset=utf-8")
		_, _ = io.WriteString(w, changelog)
	}))
	action := func(fn func(string) error) http.HandlerFunc {
		return guard(func(w http.ResponseWriter, r *http.Request) {
			if r.Method != http.MethodPost {
				http.Error(w, "POST only", http.StatusMethodNotAllowed)
				return
			}
			var req struct {
				ID  string
				On  *bool
				URL string
			}
			_ = json.NewDecoder(r.Body).Decode(&req)
			arg := req.ID
			if req.URL != "" {
				arg = req.URL
			}
			if req.On != nil {
				arg = fmt.Sprint(*req.On)
			}
			if err := fn(arg); err != nil {
				http.Error(w, err.Error(), http.StatusConflict)
				return
			}
			a.mu.Lock()
			defer a.mu.Unlock()
			writeJSON(w, map[string]any{"games": a.states(), "launcherUpdate": a.launcherUpdate(), "autoUpdate": a.autoUpdate()})
		})
	}
	a.customRoutes(mux, guard, writeJSON)
	mux.HandleFunc("/api/launch", action(a.launch))
	mux.HandleFunc("/api/install", action(a.install))
	mux.HandleFunc("/api/uninstall", action(a.uninstall))
	mux.HandleFunc("/api/selfupdate", action(a.selfUpdate))
	mux.HandleFunc("/api/check", action(func(string) error { a.requestCheck(); return nil }))
	mux.HandleFunc("/api/dismiss", action(func(id string) error { // clear a failed install's message
		a.mu.Lock()
		defer a.mu.Unlock()
		if j := a.jobs[id]; j != nil && j.err != "" {
			delete(a.jobs, id)
		}
		delete(a.launchErr, id)
		if a.selfJob != nil && a.selfJob.err != "" && id == "launcher" {
			a.selfJob = nil
		}
		return nil
	}))
	mux.HandleFunc("/api/autoupdate", action(func(on string) error {
		a.mu.Lock()
		v := on == "true"
		a.set.AutoUpdate = &v
		a.saveSettings()
		a.mu.Unlock()
		if v {
			go a.autoInstallUpdates()
		}
		return nil
	}))
	mux.HandleFunc("/api/open", action(func(u string) error { // GitHub pages open in the normal browser
		if !strings.HasPrefix(u, "https://github.com/") {
			return errors.New("only GitHub links can be opened")
		}
		openDefaultBrowser(u)
		return nil
	}))
	return mux
}

type instanceInfo struct {
	PID     int    `json:"pid"`
	URL     string `json:"url"`
	Version string `json:"version"`
}

func instanceFile() string { return filepath.Join(cacheDir(), "instance.json") }

// handOff deals with a launcher that is already running: it brings its window
// to the front (or opens a new window for it) and returns true. If that
// launcher has stopped answering, it is closed and handOff returns false so
// this one takes over.
func handOff() bool {
	var in instanceInfo
	b, err := os.ReadFile(instanceFile())
	if err == nil {
		err = json.Unmarshal(b, &in)
	}
	if err != nil || in.URL == "" {
		log.Printf("another launcher holds the lock but left no details")
		if !waitForLock() {
			msgBox("Ironite Launcher is still closing. Please try again in a few seconds.", 0x40)
			return true
		}
		return false
	}
	base, token, _ := strings.Cut(in.URL, "/?t=")
	req, _ := http.NewRequest("GET", base+"/api/ping", nil)
	req.Header.Set("X-Launcher-Token", token)
	resp, err := (&http.Client{Timeout: 3 * time.Second}).Do(req)
	if err == nil {
		resp.Body.Close()
	}
	if err == nil && resp.StatusCode == http.StatusOK {
		if focusWindow(productName + " " + in.Version) {
			log.Printf("focused the open launcher window")
			return true
		}
		// It's running in the background (a game is open or a download is
		// running): give it a window again.
		log.Printf("reopening the window of the running launcher (pid %d)", in.PID)
		if _, ok := openWindow(in.URL); !ok {
			openDefaultBrowser(in.URL)
		}
		return true
	}
	log.Printf("the running launcher (pid %d) isn't answering (%v); closing it", in.PID, err)
	killLauncher(in.PID)
	if !waitForLock() {
		msgBox("Ironite Launcher is already running but not responding. Restart your PC if it doesn't open.", 0x30)
		return true
	}
	return false
}

func main() {
	if len(os.Args) > 1 && (os.Args[1] == "--version" || os.Args[1] == "/version") {
		fmt.Println(version)
		return
	}
	logPath := filepath.Join(os.TempDir(), "IroniteLauncher.log")
	if len(os.Args) > 3 && os.Args[1] == "--after-update" {
		// The restart helper (see startSelfUpdate).
		if f, _ := os.OpenFile(logPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o644); f != nil {
			log.SetOutput(f)
		}
		afterUpdate(os.Args[2], os.Args[3])
		return
	}
	if !singleInstance() {
		// Don't wipe the running launcher's log.
		if f, _ := os.OpenFile(logPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o644); f != nil {
			log.SetOutput(f)
		}
		log.Printf("%s %s: second start", productName, version)
		if handOff() {
			return
		}
	}
	if f, _ := os.OpenFile(logPath, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o644); f != nil {
		log.SetOutput(f)
	}
	log.Printf("%s %s (%s) starting", productName, version, build)
	migrateOldFolders()

	a := &app{running: map[string]*exec.Cmd{}, started: map[string]time.Time{}, credited: map[string]time.Time{},
		jobs: map[string]*installJob{}, launchErr: map[string]string{}, checkNow: make(chan struct{}, 1)}
	a.loadSettings()
	a.rel = loadReleaseCache()
	cb, _ := webFS.ReadFile("web/games.json")
	if len(a.rel.Catalog) > 0 && a.setCatalog(a.rel.Catalog) == nil {
		log.Printf("using the cached published catalog")
	} else if err := a.setCatalog(cb); err != nil {
		fatal("The game catalog is damaged: " + err.Error())
	}
	a.mu.Lock()
	a.saveSettings()
	a.mu.Unlock()
	_ = os.RemoveAll(filepath.Join(cacheDir(), "Downloads")) // leftovers from an interrupted download
	go a.updateLoop()
	go func() { time.Sleep(15 * time.Second); _ = os.Remove(helperPath()) }() // left by an update

	tb := make([]byte, 16)
	_, _ = rand.Read(tb)
	a.token = hex.EncodeToString(tb)

	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		fatal("Could not start the launcher: " + err.Error())
	}
	go func() { _ = http.Serve(ln, a.routes()) }()
	url := fmt.Sprintf("http://%s/?t=%s", ln.Addr().String(), a.token)
	log.Printf("serving at %s", ln.Addr())
	if b, err := json.Marshal(instanceInfo{PID: os.Getpid(), URL: url, Version: version}); err == nil {
		_ = os.MkdirAll(cacheDir(), 0o755)
		_ = os.WriteFile(instanceFile(), b, 0o600)
	}
	exit := func(why string) {
		log.Printf("%s, exiting", why)
		_ = os.Remove(instanceFile())
		os.Exit(0)
	}

	if dev := os.Getenv("IRONITE_DEV"); dev != "" {
		fmt.Println(url)
		select {}
	}

	// Open the window, then stay running until it's closed. Games that are
	// still open and downloads in progress keep the launcher running in the
	// background (so play time and installs are recorded); it quits after.
	started := time.Now()
	windowGone := make(chan struct{})
	if wait, ok := openWindow(url); ok {
		go func() {
			wait()
			// The window closed (and not just a hand-off to a browser that was
			// already running), after the page had loaded.
			if time.Since(started) > 5*time.Second && a.seen.Load() {
				close(windowGone)
			}
		}()
	} else {
		openDefaultBrowser(url)
	}
	// Right after an update the old window's browser may still be closing and
	// swallow the new window. If the page hasn't loaded, open the window again.
	go func() {
		for i := 0; i < 3; i++ {
			time.Sleep(10 * time.Second)
			if a.seen.Load() {
				return
			}
			log.Printf("window hasn't opened yet; opening it again")
			if _, ok := openWindow(url); !ok {
				openDefaultBrowser(url)
			}
		}
	}()
	for {
		time.Sleep(time.Second)
		if a.gamesRunning() || a.busy() {
			continue
		}
		select {
		case <-windowGone:
			exit("window closed")
		default:
		}
		// The page says goodbye when its window closes; a reload says hello
		// again straight away, so wait a moment before trusting it.
		if bye := a.byeAt.Load(); bye != 0 && a.lastSeen.Load() <= bye && time.Since(time.UnixMilli(bye)) > 3*time.Second {
			exit("window closed (page)")
		}
		idle := time.Since(time.UnixMilli(a.lastSeen.Load()))
		if a.seen.Load() && idle > 3*time.Minute {
			exit("no activity")
		}
		if !a.seen.Load() && time.Since(started) > 90*time.Second {
			exit("window never opened")
		}
	}
}

// busy reports whether a game is downloading or installing.
func (a *app) busy() bool {
	a.mu.Lock()
	defer a.mu.Unlock()
	for _, j := range a.jobs {
		if j.err == "" {
			return true
		}
	}
	return false
}
