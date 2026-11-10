package main

// Smaller launcher features: the download queue, favorites and hidden games,
// size on disk, play sessions and weekly stats, and desktop shortcuts.

import (
	"encoding/json"
	"errors"
	"io/fs"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

func contains(list []string, s string) bool {
	for _, v := range list {
		if v == s {
			return true
		}
	}
	return false
}

func toggle(list []string, s string, on bool) []string {
	out := list[:0:0]
	for _, v := range list {
		if v != s {
			out = append(out, v)
		}
	}
	if on {
		out = append(out, s)
	}
	return out
}

/* ---------- download queue ---------- */

// enqueue adds a game to the download queue. Caller holds a.mu.
func (a *app) enqueue(id string, task func()) {
	a.tasks[id] = task
	a.queue = append(a.queue, id)
	a.queueCond.Signal()
}

// queueWorker downloads and installs queued games one at a time, in order.
func (a *app) queueWorker() {
	for {
		a.mu.Lock()
		for len(a.queue) == 0 {
			a.queueCond.Wait()
		}
		id := a.queue[0]
		task := a.tasks[id]
		a.mu.Unlock()

		if task != nil {
			task()
		}

		a.mu.Lock()
		if len(a.queue) > 0 && a.queue[0] == id {
			a.queue = a.queue[1:]
		}
		delete(a.tasks, id)
		a.mu.Unlock()
	}
}

/* ---------- size on disk ---------- */

func dirSize(dir string) int64 {
	var n int64
	_ = filepath.WalkDir(dir, func(_ string, d fs.DirEntry, err error) error {
		if err == nil && !d.IsDir() {
			if info, err := d.Info(); err == nil {
				n += info.Size()
			}
		}
		return nil
	})
	return n
}

// measureSize works out how much space an installed game takes.
func (a *app) measureSize(id string) {
	a.mu.Lock()
	rec := a.set.Installs[id]
	dir := ""
	if rec != nil {
		dir = rec.Dir
	}
	a.mu.Unlock()
	if dir == "" {
		return
	}
	n := dirSize(dir)
	a.mu.Lock()
	a.sizes[id] = n
	a.mu.Unlock()
}

func (a *app) measureAllSizes() {
	a.mu.Lock()
	var ids []string
	for id := range a.set.Installs {
		ids = append(ids, id)
	}
	a.mu.Unlock()
	for _, id := range ids {
		a.measureSize(id)
	}
}

/* ---------- routes ---------- */

func (a *app) extraRoutes(mux *http.ServeMux, guard func(http.HandlerFunc) http.HandlerFunc, writeJSON func(http.ResponseWriter, any)) {
	// Favorites and hidden games.
	mux.HandleFunc("/api/pref", guard(func(w http.ResponseWriter, r *http.Request) {
		var q struct {
			ID       string
			Favorite *bool
			Hidden   *bool
		}
		_ = json.NewDecoder(r.Body).Decode(&q)
		a.mu.Lock()
		if _, ok := a.find(q.ID); !ok {
			a.mu.Unlock()
			http.Error(w, "unknown game", http.StatusConflict)
			return
		}
		if q.Favorite != nil {
			a.set.Favorites = toggle(a.set.Favorites, q.ID, *q.Favorite)
		}
		if q.Hidden != nil {
			a.set.Hidden = toggle(a.set.Hidden, q.ID, *q.Hidden)
		}
		a.saveSettings()
		out := map[string]any{"games": a.states()}
		a.mu.Unlock()
		writeJSON(w, out)
	}))

	// A game's recent play sessions (newest first).
	mux.HandleFunc("/api/sessions", guard(func(w http.ResponseWriter, r *http.Request) {
		id := r.URL.Query().Get("id")
		a.mu.Lock()
		var list []session
		if s := a.set.Stats[id]; s != nil {
			list = append(list, s.Sessions...)
		}
		a.mu.Unlock()
		for i, j := 0, len(list)-1; i < j; i, j = i+1, j-1 {
			list[i], list[j] = list[j], list[i]
		}
		writeJSON(w, map[string]any{"sessions": list})
	}))

	// Play time per game per week, for the Stats page.
	mux.HandleFunc("/api/stats", guard(func(w http.ResponseWriter, r *http.Request) {
		const weeks = 8
		now := time.Now()
		// Weeks start on Monday, local time.
		day := time.Date(now.Year(), now.Month(), now.Day(), 0, 0, 0, 0, now.Location())
		offset := (int(day.Weekday()) + 6) % 7
		thisWeek := day.AddDate(0, 0, -offset)
		starts := make([]int64, weeks)
		for i := 0; i < weeks; i++ {
			starts[i] = thisWeek.AddDate(0, 0, -7*(weeks-1-i)).Unix()
		}
		type row struct {
			ID       string  `json:"id"`
			Weeks    []int64 `json:"weeks"` // seconds per week, oldest first
			Total    int64   `json:"total"` // all-time play time
			Sessions int     `json:"sessions"`
		}
		var rows []row
		a.mu.Lock()
		for id, s := range a.set.Stats {
			if s.PlaySeconds == 0 && len(s.Sessions) == 0 {
				continue
			}
			rw := row{ID: id, Weeks: make([]int64, weeks), Total: s.PlaySeconds, Sessions: len(s.Sessions)}
			for _, se := range s.Sessions {
				for i := weeks - 1; i >= 0; i-- {
					if se.Start >= starts[i] {
						rw.Weeks[i] += se.Secs
						break
					}
				}
			}
			rows = append(rows, rw)
		}
		var disk int64
		for _, n := range a.sizes {
			disk += n
		}
		a.mu.Unlock()
		sort.Slice(rows, func(i, j int) bool { return rows[i].Total > rows[j].Total })
		writeJSON(w, map[string]any{"weekStarts": starts, "games": rows, "diskTotal": disk, "since": a.sessionsSince()})
	}))

	// Desktop shortcut that starts a game through the launcher.
	mux.HandleFunc("/api/shortcut", guard(func(w http.ResponseWriter, r *http.Request) {
		var q struct{ ID string }
		_ = json.NewDecoder(r.Body).Decode(&q)
		a.mu.Lock()
		g, ok := a.find(q.ID)
		icon := ""
		if rec := a.set.Installs[q.ID]; rec != nil {
			icon = filepath.Join(rec.Dir, rec.Exe)
		}
		if cg := a.customByID(q.ID); cg != nil && cg.Path != "" {
			icon = cg.Path
		}
		a.mu.Unlock()
		if !ok {
			http.Error(w, "unknown game", http.StatusConflict)
			return
		}
		exe, err := os.Executable()
		if err != nil {
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		if icon == "" {
			icon = exe
		}
		path, err := createShortcut(safeFileName(g.Title), exe, "--play "+q.ID, icon, "Play "+g.Title+" with Ironite Launcher")
		if err != nil {
			log.Printf("shortcut for %s: %v", q.ID, err)
			http.Error(w, err.Error(), http.StatusConflict)
			return
		}
		log.Printf("made desktop shortcut %s", path)
		writeJSON(w, map[string]any{"path": path})
	}))
}

// sessionsSince is when play sessions started being recorded (the oldest one).
func (a *app) sessionsSince() int64 {
	a.mu.Lock()
	defer a.mu.Unlock()
	var oldest int64
	for _, s := range a.set.Stats {
		for _, se := range s.Sessions {
			if oldest == 0 || se.Start < oldest {
				oldest = se.Start
			}
		}
	}
	return oldest
}

func safeFileName(s string) string {
	s = strings.Map(func(r rune) rune {
		if strings.ContainsRune(`<>:"/\|?*`, r) || r < 32 {
			return -1
		}
		return r
	}, s)
	s = strings.TrimSpace(strings.TrimRight(s, ". "))
	if s == "" {
		return "Ironite game"
	}
	return s
}

/* ---------- starting a game from a shortcut ---------- */

// playFromArgs returns the game id in "--play <id>".
func playFromArgs() string {
	for i, arg := range os.Args {
		if arg == "--play" && i+1 < len(os.Args) {
			return os.Args[i+1]
		}
	}
	return ""
}

// askRunningToPlay asks an already running launcher to start a game.
func askRunningToPlay(in instanceInfo, id string) error {
	base, token, _ := strings.Cut(in.URL, "/?t=")
	body := strings.NewReader(`{"ID":` + jsonString(id) + `}`)
	req, _ := http.NewRequest("POST", base+"/api/launch", body)
	req.Header.Set("X-Launcher-Token", token)
	req.Header.Set("Content-Type", "application/json")
	resp, err := (&http.Client{Timeout: 5 * time.Second}).Do(req)
	if err != nil {
		return err
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK && resp.StatusCode != http.StatusConflict {
		return errors.New(resp.Status)
	}
	return nil
}

func jsonString(s string) string { b, _ := json.Marshal(s); return string(b) }
