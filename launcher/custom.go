package main

// Non-Ironite games: any game on this PC that the player adds by picking its
// program. They appear in their own category (and in Installed), launch like
// the other games, and get the same open count and play-time tracking.
// Removing one only takes it out of the launcher; its files are never touched.

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
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
	ID      string `json:"id"`
	Title   string `json:"title"`
	Path    string `json:"path"`
	AddedAt int64  `json:"addedAt"`
}

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
	for _, cg := range a.set.Custom {
		c.Games = append(c.Games, catalogGame{
			ID: cg.ID, Title: cg.Title, Subtitle: "Your game", Category: customCategoryID,
			Platforms: []string{"windows"}, Players: "On this PC", Type: "local", Exe: cg.Path,
			Description: "A game from this PC that you added to the launcher.",
		})
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

// addCustom adds a game program to the launcher.
func (a *app) addCustom(title, path string) (*customGame, error) {
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
		if strings.EqualFold(cg.Path, path) {
			return nil, errors.New(cg.Title + " is already in the launcher")
		}
	}
	b := make([]byte, 6)
	_, _ = rand.Read(b)
	cg := &customGame{ID: "custom-" + hex.EncodeToString(b), Title: title, Path: path, AddedAt: time.Now().Unix()}
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
	type req struct{ ID, Title, Path string }
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
		cg, err := a.addCustom(q.Title, q.Path)
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
		http.ServeFile(w, r, filepath.Join(customIconDir(), id+".png"))
	})
}
