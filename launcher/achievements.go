package main

// Achievements from other launchers, for the player's own (non-Ironite) games.
//   - Steam: read from the player's public Steam Community profile (no key
//     needed). The profile's "Game details" must be public.
//   - Xbox: read through OpenXBL (xbl.io), with the player's own free API key,
//     since Xbox Live needs a signed-in account.
// Results are cached for 10 minutes so the Achievements page opens quickly.

import (
	"encoding/json"
	"encoding/xml"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

type achievement struct {
	Name        string `json:"name"`
	Description string `json:"description,omitempty"`
	Icon        string `json:"icon,omitempty"`
	Unlocked    bool   `json:"unlocked"`
	UnlockedAt  int64  `json:"unlockedAt,omitempty"`
	Score       int    `json:"score,omitempty"` // Xbox gamerscore
}

type achResult struct {
	ID         string        `json:"id"`
	Source     string        `json:"source"` // "steam" or "xbox"
	Total      int           `json:"total"`
	Unlocked   int           `json:"unlocked"`
	Score      int           `json:"score,omitempty"`
	ScoreTotal int           `json:"scoreTotal,omitempty"`
	Items      []achievement `json:"items"`
	Error      string        `json:"error,omitempty"`
	FetchedAt  int64         `json:"fetchedAt"`
}

var achCache = struct {
	sync.Mutex
	m map[string]*achResult
}{m: map[string]*achResult{}}

func achKey(cg *customGame) string {
	return cg.ID + "|" + cg.AchSource + "|" + cg.SteamAppID + "|" + cg.XboxTitleID
}

var achClient = &http.Client{Timeout: 20 * time.Second}

/* ---------- Steam ---------- */

var steamProfileRe = regexp.MustCompile(`(?i)(?:steamcommunity\.com/)?(profiles|id)/([A-Za-z0-9_\-]+)`)

// steamProfilePath turns what the player typed (a profile link, a SteamID64 or
// a custom URL name) into "profiles/<id>" or "id/<name>".
func steamProfilePath(s string) string {
	s = strings.TrimSpace(s)
	if s == "" {
		return ""
	}
	if m := steamProfileRe.FindStringSubmatch(s); m != nil {
		return strings.ToLower(m[1]) + "/" + m[2]
	}
	if _, err := strconv.ParseUint(s, 10, 64); err == nil && len(s) == 17 {
		return "profiles/" + s
	}
	if regexp.MustCompile(`^[A-Za-z0-9_\-]{2,32}$`).MatchString(s) {
		return "id/" + s
	}
	return ""
}

// steamInstallDir finds Steam (the usual places; the registry isn't needed).
func steamInstallDir() string {
	for _, d := range []string{
		filepath.Join(os.Getenv("ProgramFiles(x86)"), "Steam"),
		filepath.Join(os.Getenv("ProgramFiles"), "Steam"),
		`C:\Program Files (x86)\Steam`,
	} {
		if _, err := os.Stat(filepath.Join(d, "config", "loginusers.vdf")); err == nil {
			return d
		}
	}
	return ""
}

var vdfUserRe = regexp.MustCompile(`"(\d{17})"\s*\{([^}]*)\}`)
var vdfFieldRe = regexp.MustCompile(`"(\w+)"\s*"([^"]*)"`)

// detectSteamUser reads the account signed in to Steam on this PC.
func detectSteamUser() (id, name string) {
	dir := steamInstallDir()
	if dir == "" {
		return "", ""
	}
	b, err := os.ReadFile(filepath.Join(dir, "config", "loginusers.vdf"))
	if err != nil {
		return "", ""
	}
	for _, m := range vdfUserRe.FindAllStringSubmatch(string(b), -1) {
		fields := map[string]string{}
		for _, f := range vdfFieldRe.FindAllStringSubmatch(m[2], -1) {
			fields[strings.ToLower(f[1])] = f[2]
		}
		if id == "" || fields["mostrecent"] == "1" {
			id, name = m[1], fields["personaname"]
		}
	}
	return id, name
}

var acfAppRe = regexp.MustCompile(`"appid"\s*"(\d+)"`)
var acfDirRe = regexp.MustCompile(`"installdir"\s*"([^"]+)"`)
var acfNameRe = regexp.MustCompile(`"name"\s*"([^"]+)"`)

// detectSteamApp finds the Steam app id of a game installed by Steam: from
// steam_appid.txt next to it, or the library's appmanifest for its folder.
func detectSteamApp(exePath string) (appID, name string) {
	if exePath == "" {
		return "", ""
	}
	if b, err := os.ReadFile(filepath.Join(filepath.Dir(exePath), "steam_appid.txt")); err == nil {
		if v := strings.TrimSpace(string(b)); regexp.MustCompile(`^\d+$`).MatchString(v) {
			appID = v
		}
	}
	parts := strings.Split(filepath.ToSlash(filepath.Clean(exePath)), "/")
	for i := 0; i+2 < len(parts); i++ {
		if strings.EqualFold(parts[i], "steamapps") && strings.EqualFold(parts[i+1], "common") {
			lib := filepath.FromSlash(strings.Join(parts[:i+1], "/"))
			gameDir := parts[i+2]
			files, _ := filepath.Glob(filepath.Join(lib, "appmanifest_*.acf"))
			for _, f := range files {
				b, err := os.ReadFile(f)
				if err != nil {
					continue
				}
				if m := acfDirRe.FindStringSubmatch(string(b)); m != nil && strings.EqualFold(m[1], gameDir) {
					if a := acfAppRe.FindStringSubmatch(string(b)); a != nil {
						appID = a[1]
					}
					if n := acfNameRe.FindStringSubmatch(string(b)); n != nil {
						name = n[1]
					}
				}
			}
		}
	}
	return appID, name
}

type steamXML struct {
	XMLName      xml.Name `xml:"playerstats"`
	Error        string   `xml:"error"`
	Privacy      string   `xml:"privacyState"`
	Achievements []struct {
		Closed      string `xml:"closed,attr"`
		IconClosed  string `xml:"iconClosed"`
		IconOpen    string `xml:"iconOpen"`
		Name        string `xml:"name"`
		Description string `xml:"description"`
		Unlocked    int64  `xml:"unlockTimestamp"`
	} `xml:"achievements>achievement"`
}

func steamBase() string {
	if v := os.Getenv("IRONITE_STEAM_BASE"); v != "" { // tests
		return strings.TrimRight(v, "/")
	}
	return "https://steamcommunity.com"
}

func fetchSteamAchievements(profile, appID string) (*achResult, error) {
	if profile == "" {
		return nil, errors.New("add your Steam profile on the Achievements page")
	}
	if appID == "" {
		return nil, errors.New("set the game's Steam app ID in Edit details")
	}
	u := fmt.Sprintf("%s/%s/stats/%s/achievements/?xml=1&l=english", steamBase(), profile, url.PathEscape(appID))
	req, _ := http.NewRequest("GET", u, nil)
	req.Header.Set("User-Agent", "IroniteLauncher/"+version)
	resp, err := achClient.Do(req)
	if err != nil {
		return nil, errors.New("couldn't reach Steam")
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(io.LimitReader(resp.Body, 4<<20))
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("Steam answered %s", resp.Status)
	}
	var px steamXML
	if err := xml.Unmarshal(body, &px); err != nil {
		if strings.Contains(strings.ToLower(string(body)), "private") {
			return nil, errors.New("your Steam profile's game details are private; set Game details to Public in Steam's privacy settings")
		}
		return nil, errors.New("Steam didn't return achievements for this game (check the app ID)")
	}
	if px.Error != "" {
		return nil, errors.New("Steam: " + strings.TrimSpace(px.Error))
	}
	if strings.EqualFold(px.Privacy, "private") || strings.EqualFold(px.Privacy, "friendsonly") {
		return nil, errors.New("your Steam profile's game details aren't public; set Game details to Public in Steam's privacy settings")
	}
	r := &achResult{Source: "steam"}
	for _, a := range px.Achievements {
		it := achievement{Name: strings.TrimSpace(a.Name), Description: strings.TrimSpace(a.Description), Unlocked: a.Closed == "1", UnlockedAt: a.Unlocked}
		it.Icon = a.IconOpen
		if it.Unlocked && a.IconClosed != "" {
			it.Icon = a.IconClosed // Steam calls an achieved one "closed"
		}
		r.Items = append(r.Items, it)
	}
	if len(r.Items) == 0 {
		return nil, errors.New("this game has no Steam achievements (or the app ID is wrong)")
	}
	return r, nil
}

/* ---------- Xbox (via OpenXBL) ---------- */

func xblBase() string {
	if v := os.Getenv("IRONITE_XBL_BASE"); v != "" {
		return strings.TrimRight(v, "/")
	}
	return "https://xbl.io/api/v2"
}

func xblGet(key, path string, out any) error {
	if key == "" {
		return errors.New("add your OpenXBL key on the Achievements page to see Xbox achievements")
	}
	req, _ := http.NewRequest("GET", xblBase()+path, nil)
	req.Header.Set("X-Authorization", key)
	req.Header.Set("Accept", "application/json")
	req.Header.Set("Accept-Language", "en-US")
	req.Header.Set("User-Agent", "IroniteLauncher/"+version)
	resp, err := achClient.Do(req)
	if err != nil {
		return errors.New("couldn't reach OpenXBL")
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(io.LimitReader(resp.Body, 8<<20))
	switch {
	case resp.StatusCode == http.StatusUnauthorized || resp.StatusCode == http.StatusForbidden:
		return errors.New("OpenXBL didn't accept your key; check it on the Achievements page")
	case resp.StatusCode == http.StatusTooManyRequests:
		return errors.New("OpenXBL's hourly limit was reached; try again later")
	case resp.StatusCode != http.StatusOK:
		return fmt.Errorf("OpenXBL answered %s", resp.Status)
	}
	return json.Unmarshal(body, out)
}

type xblTitle struct {
	TitleID string `json:"titleId"`
	Name    string `json:"name"`
}

// xboxTitles lists the games on the player's Xbox account (title history).
func xboxTitles(key string) ([]xblTitle, error) {
	var out struct {
		Titles []xblTitle `json:"titles"`
	}
	if err := xblGet(key, "/player/titleHistory", &out); err != nil {
		return nil, err
	}
	return out.Titles, nil
}

var titleIDRe = regexp.MustCompile(`(?i)<TitleId>\s*([0-9A-F]{8})\s*</TitleId>`)

// detectXboxTitle finds an Xbox game's title id: from its MicrosoftGame.config,
// or by name in the player's Xbox title history.
func detectXboxTitle(location, name, key string) (string, string) {
	if location != "" {
		if b, err := os.ReadFile(filepath.Join(location, "MicrosoftGame.config")); err == nil {
			if m := titleIDRe.FindStringSubmatch(string(b)); m != nil {
				if v, err := strconv.ParseUint(m[1], 16, 32); err == nil {
					return strconv.FormatUint(v, 10), ""
				}
			}
		}
	}
	if key == "" {
		return "", ""
	}
	titles, err := xboxTitles(key)
	if err != nil {
		return "", ""
	}
	norm := func(s string) string {
		return strings.Join(strings.Fields(strings.ToLower(regexp.MustCompile(`[^\pL\pN ]+`).ReplaceAllString(s, " "))), " ")
	}
	want := norm(name)
	for _, t := range titles {
		if norm(t.Name) == want {
			return t.TitleID, t.Name
		}
	}
	for _, t := range titles {
		if n := norm(t.Name); want != "" && (strings.Contains(n, want) || strings.Contains(want, n)) {
			return t.TitleID, t.Name
		}
	}
	return "", ""
}

func fetchXboxAchievements(key, titleID string) (*achResult, error) {
	if titleID == "" {
		return nil, errors.New("set the game's Xbox title ID in Edit details")
	}
	var out struct {
		Achievements []struct {
			Name              string `json:"name"`
			Description       string `json:"description"`
			LockedDescription string `json:"lockedDescription"`
			ProgressState     string `json:"progressState"`
			Progression       struct {
				TimeUnlocked string `json:"timeUnlocked"`
			} `json:"progression"`
			MediaAssets []struct {
				URL string `json:"url"`
			} `json:"mediaAssets"`
			Rewards []struct {
				Type  string `json:"type"`
				Value string `json:"value"`
			} `json:"rewards"`
		} `json:"achievements"`
	}
	if err := xblGet(key, "/achievements/title/"+url.PathEscape(titleID), &out); err != nil {
		return nil, err
	}
	r := &achResult{Source: "xbox"}
	for _, a := range out.Achievements {
		it := achievement{Name: a.Name, Unlocked: strings.EqualFold(a.ProgressState, "Achieved")}
		it.Description = a.Description
		if !it.Unlocked && a.LockedDescription != "" {
			it.Description = a.LockedDescription
		}
		if len(a.MediaAssets) > 0 {
			it.Icon = a.MediaAssets[0].URL
		}
		if t, err := time.Parse(time.RFC3339, a.Progression.TimeUnlocked); err == nil && it.Unlocked && t.Year() > 2000 {
			it.UnlockedAt = t.Unix()
		}
		for _, rw := range a.Rewards {
			if strings.EqualFold(rw.Type, "Gamerscore") {
				it.Score, _ = strconv.Atoi(rw.Value)
			}
		}
		r.Items = append(r.Items, it)
	}
	if len(r.Items) == 0 {
		return nil, errors.New("Xbox has no achievements for this game on your account (check the title ID)")
	}
	return r, nil
}

/* ---------- putting it together ---------- */

func (a *app) achievementsFor(cg customGame, force bool) *achResult {
	key := achKey(&cg)
	achCache.Lock()
	if c := achCache.m[key]; c != nil && !force && time.Since(time.Unix(c.FetchedAt, 0)) < 10*time.Minute {
		achCache.Unlock()
		return c
	}
	achCache.Unlock()

	a.mu.Lock()
	profile, xbl := steamProfilePath(a.set.SteamProfile), a.set.XblKey
	a.mu.Unlock()
	if profile == "" {
		if id, _ := detectSteamUser(); id != "" {
			profile = "profiles/" + id
		}
	}
	var r *achResult
	var err error
	switch cg.AchSource {
	case "steam":
		r, err = fetchSteamAchievements(profile, cg.SteamAppID)
	case "xbox":
		r, err = fetchXboxAchievements(xbl, cg.XboxTitleID)
	default:
		return nil
	}
	if err != nil {
		r = &achResult{Source: cg.AchSource, Error: err.Error()}
		log.Printf("achievements for %s: %v", cg.ID, err)
	}
	r.ID = cg.ID
	r.FetchedAt = time.Now().Unix()
	for _, it := range r.Items {
		r.Total++
		r.ScoreTotal += it.Score
		if it.Unlocked {
			r.Unlocked++
			r.Score += it.Score
		}
	}
	// Unlocked first (newest first), then locked.
	sort.SliceStable(r.Items, func(i, j int) bool {
		if r.Items[i].Unlocked != r.Items[j].Unlocked {
			return r.Items[i].Unlocked
		}
		return r.Items[i].UnlockedAt > r.Items[j].UnlockedAt
	})
	achCache.Lock()
	achCache.m[key] = r
	achCache.Unlock()
	return r
}

func (a *app) achievementRoutes(mux *http.ServeMux, guard func(http.HandlerFunc) http.HandlerFunc, writeJSON func(http.ResponseWriter, any)) {
	accounts := func() map[string]any {
		a.mu.Lock()
		profile, key := a.set.SteamProfile, a.set.XblKey
		a.mu.Unlock()
		id, name := detectSteamUser()
		return map[string]any{
			"steamProfile": profile, "steamDetectedId": id, "steamDetectedName": name,
			"xblKeySet": key != "",
		}
	}
	mux.HandleFunc("/api/ach", guard(func(w http.ResponseWriter, r *http.Request) {
		force := r.URL.Query().Get("refresh") == "1"
		a.mu.Lock()
		var games []customGame
		for _, cg := range a.set.Custom {
			if cg.AchSource != "" {
				games = append(games, *cg)
			}
		}
		a.mu.Unlock()
		results := make([]*achResult, len(games))
		var wg sync.WaitGroup
		sem := make(chan struct{}, 4)
		for i, cg := range games {
			wg.Add(1)
			go func(i int, cg customGame) {
				defer wg.Done()
				sem <- struct{}{}
				defer func() { <-sem }()
				results[i] = a.achievementsFor(cg, force)
			}(i, cg)
		}
		wg.Wait()
		out := map[string]any{"games": results}
		for k, v := range accounts() {
			out[k] = v
		}
		writeJSON(w, out)
	}))
	mux.HandleFunc("/api/ach/accounts", guard(func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			var q struct {
				SteamProfile *string
				XblKey       *string
			}
			_ = json.NewDecoder(r.Body).Decode(&q)
			a.mu.Lock()
			if q.SteamProfile != nil {
				v := strings.TrimSpace(*q.SteamProfile)
				if v != "" && steamProfilePath(v) == "" {
					a.mu.Unlock()
					http.Error(w, "that doesn't look like a Steam profile link or ID", http.StatusConflict)
					return
				}
				a.set.SteamProfile = v
			}
			if q.XblKey != nil {
				a.set.XblKey = strings.TrimSpace(*q.XblKey)
			}
			a.saveSettings()
			a.mu.Unlock()
			achCache.Lock()
			achCache.m = map[string]*achResult{}
			achCache.Unlock()
		}
		writeJSON(w, accounts())
	}))
	// Suggests where a game's achievements come from.
	mux.HandleFunc("/api/ach/detect", guard(func(w http.ResponseWriter, r *http.Request) {
		var q struct{ ID string }
		_ = json.NewDecoder(r.Body).Decode(&q)
		a.mu.Lock()
		cg := a.customByID(q.ID)
		var c customGame
		if cg != nil {
			c = *cg
		}
		key := a.set.XblKey
		a.mu.Unlock()
		if cg == nil {
			http.Error(w, "unknown game", http.StatusConflict)
			return
		}
		out := map[string]any{}
		if c.isStore() {
			if t, name := detectXboxTitle(c.Location, c.Title, key); t != "" {
				out["source"], out["xboxTitleId"], out["found"] = "xbox", t, name
			}
		} else if id, name := detectSteamApp(c.Path); id != "" {
			out["source"], out["steamAppId"], out["found"] = "steam", id, name
		}
		writeJSON(w, out)
	}))
}
