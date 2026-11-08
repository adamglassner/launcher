package main

// Non-Ironite games: any game on this PC that the player adds by picking its
// program. They appear in their own category (and in Installed), launch like
// the other games, and get the same open count and play-time tracking.
// Removing one only takes it out of the launcher; its files are never touched.

import (
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
	"unicode"
)

const customCategoryID = "custom"

type customGame struct {
	ID    string `json:"id"`
	Title string `json:"title"`
	Path  string `json:"path,omitempty"`  // a program (.exe) ...
	AppID string `json:"appId,omitempty"` // ... or a Microsoft Store / Xbox app (shell:AppsFolder\<AppID>)
	// Where a Store/Xbox game is installed: its running processes are found
	// there, since the app itself is started by Windows and not by us.
	Location string `json:"location,omitempty"`
	AddedAt  int64  `json:"addedAt"`

	// Details the player can edit.
	Category    string `json:"category,omitempty"` // a launcher category id (shows the game there too)
	Group       string `json:"group,omitempty"`    // or the player's own category name
	Developer   string `json:"developer,omitempty"`
	Players     string `json:"players,omitempty"`
	Description string `json:"description,omitempty"`
	Art         string `json:"art,omitempty"` // file name of custom cover art in CustomIcons
}

func (cg *customGame) isStore() bool { return cg.AppID != "" }

var customCategory = json.RawMessage(`{"id": "custom", "name": "Non-Ironite Games", "blurb": "Your own games from this PC. Add any game's program to play it from the launcher, with play time tracked like everything else."}`)

func (g catalogGame) isLocal() bool { return g.Type == "local" }

func customIconDir() string { return filepath.Join(dataDir(), "CustomIcons") }

// mergeCustom rebuilds the catalog the page sees: the Ironite catalog plus the
// player's own games. Caller holds a.mu.
func (a *app) mergeCustom() {
	c := a.baseCat
	c.Categories = append([]json.RawMessage{}, a.baseCat.Categories...)
	has := false
	for _, raw := range c.Categories {
		var probe struct{ ID string }
		if json.Unmarshal(raw, &probe) == nil && probe.ID == customCategoryID {
			has = true
		}
	}
	if !has {
		c.Categories = append(c.Categories, customCategory)
	}
	c.Games = append([]catalogGame{}, a.baseCat.Games...)
	catNames := map[string]string{}
	for _, raw := range a.baseCat.Categories {
		var probe struct{ ID, Name string }
		if json.Unmarshal(raw, &probe) == nil {
			catNames[probe.ID] = probe.Name
		}
	}
	for _, cg := range a.set.Custom {
		g := catalogGame{
			ID: cg.ID, Title: cg.Title, Subtitle: cg.Developer, Category: customCategoryID,
			Platforms: []string{"windows"}, Players: cg.Players, Type: "local", Exe: cg.Path,
			Description: cg.Description, Group: cg.Group, Art: cg.Art != "", Store: cg.isStore(),
		}
		if name, ok := catNames[cg.Category]; ok && cg.Category != customCategoryID {
			g.Category, g.Group = cg.Category, name
		}
		if g.Subtitle == "" {
			g.Subtitle = "Your game"
		}
		if g.Players == "" {
			g.Players = "On this PC"
		}
		if g.Store {
			g.Exe = "Xbox / Microsoft Store app"
		}
		c.Games = append(c.Games, g)
	}
	if c.Schema == 0 {
		c.Schema = 2
	}
	a.cat = c
	a.catJSON, _ = json.Marshal(c)
	a.catVer++
}

func (a *app) customByID(id string) *customGame {
	for _, cg := range a.set.Custom {
		if cg.ID == id {
			return cg
		}
	}
	return nil
}

var allowedGameExt = map[string]bool{".exe": true}

// addStoreApp adds a Microsoft Store / Xbox app game to the launcher.
func (a *app) addStoreApp(title, appID, location string, d customDetails) (*customGame, error) {
	if !strings.Contains(appID, "!") {
		return nil, errors.New("choose a game from the list")
	}
	if strings.TrimSpace(title) == "" {
		return nil, errors.New("give the game a name")
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	for _, cg := range a.set.Custom {
		if strings.EqualFold(cg.AppID, appID) {
			return nil, errors.New(cg.Title + " is already in the launcher")
		}
	}
	cg := &customGame{ID: newCustomID(), Title: strings.TrimSpace(title), AppID: appID, Location: location, AddedAt: time.Now().Unix()}
	d.apply(cg)
	a.set.Custom = append(a.set.Custom, cg)
	a.saveSettings()
	a.mergeCustom()
	go func() {
		if png, err := storeAppArt(location); err == nil {
			_ = os.MkdirAll(customIconDir(), 0o755)
			_ = os.WriteFile(filepath.Join(customIconDir(), cg.ID+".png"), png, 0o644)
		} else {
			log.Printf("art for %s: %v", cg.ID, err)
		}
	}()
	log.Printf("added %s: %s (%s)", cg.ID, appID, location)
	return cg, nil
}

func newCustomID() string {
	b := make([]byte, 6)
	_, _ = rand.Read(b)
	return "custom-" + hex.EncodeToString(b)
}

// customDetails are the parts of a game the player can edit.
type customDetails struct {
	Title, Category, Group, Developer, Players, Description string
}

func clip(s string, n int) string {
	s = strings.TrimSpace(s)
	if r := []rune(s); len(r) > n {
		return string(r[:n])
	}
	return s
}

func (d customDetails) apply(cg *customGame) {
	if t := clip(d.Title, 60); t != "" {
		cg.Title = t
	}
	cg.Category = clip(d.Category, 40)
	cg.Group = clip(d.Group, 40)
	if cg.Category != "" {
		cg.Group = ""
	}
	cg.Developer = clip(d.Developer, 60)
	cg.Players = clip(d.Players, 40)
	cg.Description = clip(d.Description, 1200)
}

func (a *app) updateCustom(id string, d customDetails) error {
	if strings.TrimSpace(d.Title) == "" {
		return errors.New("give the game a name")
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	cg := a.customByID(id)
	if cg == nil {
		return errors.New("unknown game")
	}
	d.apply(cg)
	a.saveSettings()
	a.mergeCustom()
	return nil
}

var artTypes = map[string]string{"image/png": ".png", "image/jpeg": ".jpg", "image/webp": ".webp", "image/gif": ".gif"}

// setCustomArt saves cover art for one of the player's games ("" removes it).
func (a *app) setCustomArt(id, dataURL string) error {
	a.mu.Lock()
	defer a.mu.Unlock()
	cg := a.customByID(id)
	if cg == nil {
		return errors.New("unknown game")
	}
	if cg.Art != "" {
		_ = os.Remove(filepath.Join(customIconDir(), cg.Art))
		cg.Art = ""
	}
	if dataURL != "" {
		head, b64, ok := strings.Cut(dataURL, ";base64,")
		ext := artTypes[strings.TrimPrefix(head, "data:")]
		if !ok || ext == "" {
			return errors.New("use a PNG, JPEG, WebP or GIF picture")
		}
		data, err := base64.StdEncoding.DecodeString(b64)
		if err != nil || len(data) == 0 {
			return errors.New("that picture couldn't be read")
		}
		if len(data) > 12<<20 {
			return errors.New("that picture is too big (12 MB at most)")
		}
		_ = os.MkdirAll(customIconDir(), 0o755)
		name := fmt.Sprintf("%s-art-%d%s", cg.ID, time.Now().Unix(), ext)
		if err := os.WriteFile(filepath.Join(customIconDir(), name), data, 0o644); err != nil {
			return err
		}
		cg.Art = name
	}
	a.saveSettings()
	a.mergeCustom()
	return nil
}

// addCustom adds a game program to the launcher.
func (a *app) addCustom(title, path string, d customDetails) (*customGame, error) {
	path = strings.TrimSpace(strings.Trim(strings.TrimSpace(path), `"`))
	if path == "" {
		return nil, errors.New("choose the game's program first")
	}
	if !filepath.IsAbs(path) {
		return nil, errors.New("use the full path to the game's program (for example C:\\Games\\MyGame\\MyGame.exe)")
	}
	if !allowedGameExt[strings.ToLower(filepath.Ext(path))] {
		return nil, errors.New("choose the game's program (an .exe file)")
	}
	if st, err := os.Stat(path); err != nil || st.IsDir() {
		return nil, errors.New("that program wasn't found")
	}
	title = strings.TrimSpace(title)
	if title == "" {
		title = guessTitle(path)
	}
	if len([]rune(title)) > 60 {
		title = string([]rune(title)[:60])
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	for _, cg := range a.set.Custom {
		if cg.Path != "" && strings.EqualFold(cg.Path, path) {
			return nil, errors.New(cg.Title + " is already in the launcher")
		}
	}
	cg := &customGame{ID: newCustomID(), Title: title, Path: path, AddedAt: time.Now().Unix()}
	d.Title = ""
	d.apply(cg)
	a.set.Custom = append(a.set.Custom, cg)
	a.saveSettings()
	a.mergeCustom()
	go saveCustomIcon(cg) // the program's own icon, for its tile
	log.Printf("added %s: %s", cg.ID, path)
	return cg, nil
}

// removeCustom takes a game out of the launcher (its files stay).
func (a *app) removeCustom(id string) error {
	a.mu.Lock()
	defer a.mu.Unlock()
	if a.running[id] != nil {
		return errors.New("close the game before removing it")
	}
	for i, cg := range a.set.Custom {
		if cg.ID == id {
			a.set.Custom = append(a.set.Custom[:i], a.set.Custom[i+1:]...)
			delete(a.set.Stats, id)
			delete(a.launchErr, id)
			a.saveSettings()
			a.mergeCustom()
			_ = os.Remove(filepath.Join(customIconDir(), id+".png"))
			if cg.Art != "" {
				_ = os.Remove(filepath.Join(customIconDir(), cg.Art))
			}
			return nil
		}
	}
	return errors.New("unknown game")
}

func (a *app) renameCustom(id, title string) error {
	title = strings.TrimSpace(title)
	if title == "" {
		return errors.New("give the game a name")
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	cg := a.customByID(id)
	if cg == nil {
		return errors.New("unknown game")
	}
	cg.Title = title
	a.saveSettings()
	a.mergeCustom()
	return nil
}

func saveCustomIcon(cg *customGame) {
	png, err := exeIconPNG(cg.Path)
	if err != nil {
		log.Printf("icon for %s: %v", cg.ID, err)
		return
	}
	_ = os.MkdirAll(customIconDir(), 0o755)
	_ = os.WriteFile(filepath.Join(customIconDir(), cg.ID+".png"), png, 0o644)
}

var splitWords = regexp.MustCompile(`[_\-.]+`)

// guessTitle turns "C:\Games\SuperTuxKart\supertuxkart_x64.exe" into a name.
func guessTitle(path string) string {
	base := strings.TrimSuffix(filepath.Base(path), filepath.Ext(path))
	for _, junk := range []string{"_x64", "-x64", "x64", "_win64", "-win64", "_win32", "-Win64-Shipping", "_launcher", "Launcher"} {
		if strings.HasSuffix(strings.ToLower(base), strings.ToLower(junk)) && len(base) > len(junk) {
			base = base[:len(base)-len(junk)]
		}
	}
	base = splitWords.ReplaceAllString(base, " ")
	// Split CamelCase: "MyCoolGame" -> "My Cool Game".
	var out []rune
	rs := []rune(base)
	for i, r := range rs {
		if i > 0 && unicode.IsUpper(r) && unicode.IsLower(rs[i-1]) {
			out = append(out, ' ')
		}
		out = append(out, r)
	}
	t := strings.Join(strings.Fields(string(out)), " ")
	if t == "" {
		return "My game"
	}
	if r := []rune(t); unicode.IsLower(r[0]) {
		r[0] = unicode.ToUpper(r[0])
		t = string(r)
	}
	return t
}

// customRoutes adds the endpoints for the player's own games.
func (a *app) customRoutes(mux *http.ServeMux, guard func(http.HandlerFunc) http.HandlerFunc, writeJSON func(http.ResponseWriter, any)) {
	type req struct {
		ID, Title, Path, AppID, Location    string
		Category, Group, Developer, Players string
		Description, Data                   string
	}
	read := func(w http.ResponseWriter, r *http.Request) (req, bool) {
		var q req
		if r.Method != http.MethodPost {
			http.Error(w, "POST only", http.StatusMethodNotAllowed)
			return q, false
		}
		_ = json.NewDecoder(r.Body).Decode(&q)
		return q, true
	}
	reply := func(w http.ResponseWriter, extra map[string]any) {
		a.mu.Lock()
		out := map[string]any{"games": a.states(), "catalogSize": a.catVer}
		a.mu.Unlock()
		for k, v := range extra {
			out[k] = v
		}
		writeJSON(w, out)
	}
	mux.HandleFunc("/api/custom/browse", guard(func(w http.ResponseWriter, r *http.Request) {
		if _, ok := read(w, r); !ok {
			return
		}
		path, err := browseForGame(productName + " " + version)
		if err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		out := map[string]any{"path": path}
		if path != "" {
			out["title"] = guessTitle(path)
		}
		writeJSON(w, out)
	}))
	mux.HandleFunc("/api/custom/add", guard(func(w http.ResponseWriter, r *http.Request) {
		q, ok := read(w, r)
		if !ok {
			return
		}
		d := customDetails{Category: q.Category, Group: q.Group, Developer: q.Developer, Players: q.Players, Description: q.Description}
		var cg *customGame
		var err error
		if q.AppID != "" {
			cg, err = a.addStoreApp(q.Title, q.AppID, q.Location, d)
		} else {
			cg, err = a.addCustom(q.Title, q.Path, d)
		}
		if err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		reply(w, map[string]any{"id": cg.ID})
	}))
	mux.HandleFunc("/api/custom/remove", guard(func(w http.ResponseWriter, r *http.Request) {
		q, ok := read(w, r)
		if !ok {
			return
		}
		if err := a.removeCustom(q.ID); err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		reply(w, nil)
	}))
	mux.HandleFunc("/api/custom/update", guard(func(w http.ResponseWriter, r *http.Request) {
		q, ok := read(w, r)
		if !ok {
			return
		}
		d := customDetails{Title: q.Title, Category: q.Category, Group: q.Group, Developer: q.Developer, Players: q.Players, Description: q.Description}
		if err := a.updateCustom(q.ID, d); err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		reply(w, nil)
	}))
	mux.HandleFunc("/api/custom/setart", guard(func(w http.ResponseWriter, r *http.Request) {
		r.Body = http.MaxBytesReader(w, r.Body, 20<<20)
		q, ok := read(w, r)
		if !ok {
			return
		}
		if err := a.setCustomArt(q.ID, q.Data); err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		reply(w, nil)
	}))
	mux.HandleFunc("/api/custom/storeapps", guard(func(w http.ResponseWriter, r *http.Request) {
		apps, err := listStoreApps()
		if err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		a.mu.Lock()
		added := map[string]bool{}
		for _, cg := range a.set.Custom {
			if cg.AppID != "" {
				added[strings.ToLower(cg.AppID)] = true
			}
		}
		a.mu.Unlock()
		for i := range apps {
			apps[i].Added = added[strings.ToLower(apps[i].AppID)]
		}
		writeJSON(w, map[string]any{"apps": apps})
	}))
	mux.HandleFunc("/api/custom/rename", guard(func(w http.ResponseWriter, r *http.Request) {
		q, ok := read(w, r)
		if !ok {
			return
		}
		if err := a.renameCustom(q.ID, q.Title); err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		reply(w, nil)
	}))
	// <img> tags can't send the token header, so it comes in the query string.
	mux.HandleFunc("/api/custom/icon", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Query().Get("t") != a.token {
			http.Error(w, "forbidden", http.StatusForbidden)
			return
		}
		id := r.URL.Query().Get("id")
		if !strings.HasPrefix(id, "custom-") || strings.ContainsAny(id, `/\.`) {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Cache-Control", "no-store")
		if r.URL.Query().Get("kind") == "art" {
			a.mu.Lock()
			cg := a.customByID(id)
			name := ""
			if cg != nil {
				name = cg.Art
			}
			a.mu.Unlock()
			if name == "" {
				http.NotFound(w, r)
				return
			}
			http.ServeFile(w, r, filepath.Join(customIconDir(), name))
			return
		}
		http.ServeFile(w, r, filepath.Join(customIconDir(), id+".png"))
	})
}

// A Microsoft Store / Xbox app installed on this PC.
type storeApp struct {
	Name     string `json:"name"`
	AppID    string `json:"appId"`
	Location string `json:"location"`
	Game     bool   `json:"game"` // an Xbox PC game (it has a MicrosoftGame.config)
	Added    bool   `json:"added"`
}

// waitForStoreGame follows a Store / Xbox game Windows started for us: it
// waits (up to 2 minutes) for a process from the game's install folder to
// appear, then until all of them have closed.
func waitForStoreGame(dir string, start time.Time) {
	if dir == "" {
		return
	}
	seen := false
	quiet := 0
	for {
		time.Sleep(3 * time.Second)
		n := processesUnder(dir)
		switch {
		case n > 0:
			seen, quiet = true, 0
		case seen:
			quiet++
			if quiet >= 2 { // gone for two checks in a row (some games restart themselves)
				return
			}
		case time.Since(start) > 2*time.Minute:
			log.Printf("no process from %s appeared", dir)
			return
		}
	}
}

var (
	logoAttrRe = regexp.MustCompile(`(?i)\b(Square480x480Logo|Square310x310Logo|Square150x150Logo|Square44x44Logo|StoreLogo)\s*=\s*"([^"]+)"`)
	logoElemRe = regexp.MustCompile(`(?i)<Logo>([^<]+)</Logo>`)
)

// storeAppArt finds the best square logo a Store / Xbox game ships with
// (MicrosoftGame.config first, then AppxManifest.xml) and returns it as PNG.
func storeAppArt(location string) ([]byte, error) {
	if location == "" {
		return nil, errors.New("no install folder")
	}
	rank := map[string]int{"square480x480logo": 5, "square310x310logo": 4, "square150x150logo": 3, "storelogo": 2, "square44x44logo": 1}
	type cand struct {
		path string
		rank int
	}
	var cands []cand
	for _, f := range []string{"MicrosoftGame.config", "AppxManifest.xml"} {
		b, err := os.ReadFile(filepath.Join(location, f))
		if err != nil {
			continue
		}
		for _, m := range logoAttrRe.FindAllStringSubmatch(string(b), -1) {
			cands = append(cands, cand{m[2], rank[strings.ToLower(m[1])]})
		}
		for _, m := range logoElemRe.FindAllStringSubmatch(string(b), -1) {
			cands = append(cands, cand{m[1], 2})
		}
	}
	best, bestRank, bestSize := "", -1, int64(0)
	for _, c := range cands {
		p := resolveScaledAsset(location, c.path)
		if p == "" {
			continue
		}
		st, _ := os.Stat(p)
		if c.rank > bestRank || (c.rank == bestRank && st != nil && st.Size() > bestSize) {
			best, bestRank = p, c.rank
			if st != nil {
				bestSize = st.Size()
			}
		}
	}
	if best == "" {
		return nil, errors.New("no logo found")
	}
	return os.ReadFile(best)
}

// resolveScaledAsset finds "Assets\Logo.png" as shipped: the file itself or its
// largest ".scale-200" / ".targetsize-256" variant.
func resolveScaledAsset(root, rel string) string {
	rel = filepath.FromSlash(strings.ReplaceAll(rel, `\`, "/"))
	p := filepath.Join(root, rel)
	if !strings.HasPrefix(p, filepath.Clean(root)+string(os.PathSeparator)) {
		return ""
	}
	if st, err := os.Stat(p); err == nil && !st.IsDir() {
		return p
	}
	ext := filepath.Ext(p)
	base := strings.TrimSuffix(filepath.Base(p), ext)
	entries, err := os.ReadDir(filepath.Dir(p))
	if err != nil {
		return ""
	}
	best, bestSize := "", int64(0)
	for _, e := range entries {
		n := e.Name()
		if e.IsDir() || !strings.EqualFold(filepath.Ext(n), ext) || !strings.HasPrefix(strings.ToLower(n), strings.ToLower(base)+".") {
			continue
		}
		if strings.Contains(strings.ToLower(n), "contrast") || strings.Contains(strings.ToLower(n), "altform-lightunplated") {
			continue
		}
		if info, err := e.Info(); err == nil && info.Size() > bestSize {
			best, bestSize = filepath.Join(filepath.Dir(p), n), info.Size()
		}
	}
	return best
}
