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
	URL         string   `json:"url,omitempty"`
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
}

type app struct {
	mu       sync.Mutex
	cat      catalog
	catJSON  []byte
	set      settings
	rel      *releaseCache
	running  map[string]*exec.Cmd
	started  map[string]time.Time
	credited map[string]time.Time // last time play time was saved, per running game
	jobs     map[string]*installJob
	token    string
	lastSeen atomic.Int64
	seen     atomic.Bool

	checking  bool
	lastCheck time.Time
	checkErr  string
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
	a.cat, a.catJSON = c, b
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
		if g.isWeb() {
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
	} else {
		if rec == nil {
			return errors.New("install the game first")
		}
		exe := filepath.Join(rec.Dir, rec.Exe)
		if _, err := os.Stat(exe); err != nil {
			return errors.New("the game's files are missing; uninstall it and install it again")
		}
		cmd = gameCommand(exe, filepath.Dir(exe))
	}
	if err := cmd.Start(); err != nil {
		return err
	}

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
	go func() { _ = cmd.Wait(); close(exited) }()
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
		if string(remote) != string(a.catJSON) {
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
	a.checkErr = ""
	if firstErr != nil {
		if errors.Is(firstErr, errRateLimited) {
			a.checkErr = errRateLimited.Error()
		} else {
			a.checkErr = "Couldn't reach GitHub to check for updates. Check your internet connection."
		}
	}
	a.mu.Unlock()
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
			a.lastSeen.Store(time.Now().Unix())
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
			"checking": a.checking, "lastCheck": last, "checkError": a.checkErr,
			"launcherUpdate": a.launcherUpdate(), "catalogSize": len(a.catJSON),
		})
	}))
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

func main() {
	logf, _ := os.OpenFile(filepath.Join(os.TempDir(), "IroniteLauncher.log"), os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o644)
	if logf != nil {
		log.SetOutput(logf)
	}
	log.Printf("%s %s (%s) starting", productName, version, build)

	if len(os.Args) > 1 && (os.Args[1] == "--version" || os.Args[1] == "/version") {
		fmt.Println(version)
		return
	}
	if !singleInstance() {
		return
	}
	migrateOldFolders()

	a := &app{running: map[string]*exec.Cmd{}, started: map[string]time.Time{}, credited: map[string]time.Time{},
		jobs: map[string]*installJob{}, checkNow: make(chan struct{}, 1)}
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

	if dev := os.Getenv("IRONITE_DEV"); dev != "" {
		fmt.Println(url)
		select {}
	}

	// Open the window. If we own the browser process, quit when it closes.
	started := time.Now()
	if wait, ok := openWindow(url); ok {
		wait()
		if time.Since(started) > 5*time.Second {
			// Keep running in the background until open games close and
			// downloads finish, so play time and installs are recorded.
			for a.gamesRunning() || a.busy() {
				time.Sleep(2 * time.Second)
			}
			log.Printf("window closed, exiting")
			return
		}
	} else {
		openDefaultBrowser(url)
	}
	// Fallback: browser handed off to an existing process. Stay alive while
	// the page keeps polling (generous timeout for minimised windows).
	for {
		time.Sleep(5 * time.Second)
		idle := time.Since(time.Unix(a.lastSeen.Load(), 0))
		if a.gamesRunning() || a.busy() {
			continue
		}
		if (a.seen.Load() && idle > 3*time.Minute) || (!a.seen.Load() && time.Since(started) > 90*time.Second) {
			log.Printf("no activity, exiting")
			return
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
