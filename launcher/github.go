package main

// Reads game and launcher releases from GitHub, like a store front: every game
// lives in its own repository and each GitHub Release is a version of it.
// Requests are unauthenticated (60 an hour per PC), so responses are cached on
// disk with their ETag; a 304 "not modified" answer doesn't count against the
// limit, and the cache keeps the library working offline.

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"
)

const launcherRepo = "adamglassner/launcher"

// Where the latest game catalog is published, so new games show up without
// a launcher update. The copy built into the launcher is the fallback.
const remoteCatalogURL = "https://raw.githubusercontent.com/" + launcherRepo + "/main/launcher/web/games.json"

func apiBase() string {
	if v := os.Getenv("IRONITE_GITHUB_API"); v != "" { // tests use a local mock
		return strings.TrimRight(v, "/")
	}
	return "https://api.github.com"
}

type ghAsset struct {
	Name   string `json:"name"`
	URL    string `json:"browser_download_url"`
	Size   int64  `json:"size"`
	Digest string `json:"digest"` // "sha256:<hex>" on recent releases
}

type release struct {
	Tag         string    `json:"tag_name"`
	Name        string    `json:"name"`
	Body        string    `json:"body"`
	HTMLURL     string    `json:"html_url"`
	PublishedAt time.Time `json:"published_at"`
	Draft       bool      `json:"draft"`
	Prerelease  bool      `json:"prerelease"`
	Assets      []ghAsset `json:"assets"`
}

func (r *release) Version() string { return strings.TrimPrefix(strings.TrimPrefix(r.Tag, "v"), "V") }

// gameAsset picks the file the launcher installs: a Windows zip if there is
// one, otherwise a setup program.
func (r *release) gameAsset() *ghAsset {
	pick := func(ok func(n string) bool) *ghAsset {
		for i := range r.Assets {
			if ok(strings.ToLower(r.Assets[i].Name)) {
				return &r.Assets[i]
			}
		}
		return nil
	}
	if a := pick(func(n string) bool { return strings.HasSuffix(n, "-windows.zip") }); a != nil {
		return a
	}
	if a := pick(func(n string) bool { return strings.HasSuffix(n, ".zip") && !strings.Contains(n, "source") }); a != nil {
		return a
	}
	if a := pick(func(n string) bool { return strings.HasSuffix(n, ".exe") && strings.Contains(n, "setup") }); a != nil {
		return a
	}
	return pick(func(n string) bool { return strings.HasSuffix(n, ".exe") })
}

// launcherAsset is the launcher's own setup program.
func (r *release) launcherAsset() *ghAsset {
	for i := range r.Assets {
		n := strings.ToLower(r.Assets[i].Name)
		if strings.HasPrefix(n, "ironitelauncher-setup") && strings.HasSuffix(n, ".exe") {
			return &r.Assets[i]
		}
	}
	return nil
}

type cachedRelease struct {
	ETag    string    `json:"etag"`
	Release *release  `json:"release"` // nil = the repo has no published release yet
	Checked time.Time `json:"checked"`
}

type releaseCache struct {
	mu      sync.Mutex
	Repos   map[string]*cachedRelease `json:"repos"`
	Catalog json.RawMessage           `json:"catalog,omitempty"`
	CatETag string                    `json:"catalogEtag,omitempty"`
}

func cacheFile() string { return filepath.Join(cacheDir(), "releases.json") }

func loadReleaseCache() *releaseCache {
	c := &releaseCache{Repos: map[string]*cachedRelease{}}
	if b, err := os.ReadFile(cacheFile()); err == nil {
		_ = json.Unmarshal(b, c)
		if c.Repos == nil {
			c.Repos = map[string]*cachedRelease{}
		}
	}
	return c
}

func (c *releaseCache) save() { // caller holds c.mu
	_ = os.MkdirAll(cacheDir(), 0o755)
	b, _ := json.MarshalIndent(c, "", " ")
	tmp := cacheFile() + ".tmp"
	if os.WriteFile(tmp, b, 0o644) == nil {
		_ = os.Rename(tmp, cacheFile())
	}
}

func (c *releaseCache) get(repo string) *release {
	c.mu.Lock()
	defer c.mu.Unlock()
	if e := c.Repos[strings.ToLower(repo)]; e != nil {
		return e.Release
	}
	return nil
}

// known reports whether the repo has been checked at least once.
func (c *releaseCache) known(repo string) bool {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.Repos[strings.ToLower(repo)] != nil
}

var httpClient = &http.Client{Timeout: 30 * time.Second}

var errRateLimited = errors.New("GitHub is busy right now (rate limit); the launcher will try again later")

// fetchLatest asks GitHub for a repo's latest published release.
func (c *releaseCache) fetchLatest(repo string) error {
	key := strings.ToLower(repo)
	c.mu.Lock()
	prev := c.Repos[key]
	etag := ""
	if prev != nil {
		etag = prev.ETag
	}
	c.mu.Unlock()

	req, _ := http.NewRequest("GET", apiBase()+"/repos/"+repo+"/releases/latest", nil)
	req.Header.Set("Accept", "application/vnd.github+json")
	req.Header.Set("X-GitHub-Api-Version", "2022-11-28")
	req.Header.Set("User-Agent", "IroniteLauncher/"+version)
	if etag != "" {
		req.Header.Set("If-None-Match", etag)
	}
	resp, err := httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(io.LimitReader(resp.Body, 4<<20))

	c.mu.Lock()
	defer c.mu.Unlock()
	switch {
	case resp.StatusCode == http.StatusNotModified && prev != nil:
		prev.Checked = time.Now()
	case resp.StatusCode == http.StatusOK:
		var r release
		if err := json.Unmarshal(body, &r); err != nil {
			return fmt.Errorf("%s: %v", repo, err)
		}
		if r.Draft || r.Prerelease {
			c.Repos[key] = &cachedRelease{Checked: time.Now()}
		} else {
			c.Repos[key] = &cachedRelease{ETag: resp.Header.Get("ETag"), Release: &r, Checked: time.Now()}
		}
	case resp.StatusCode == http.StatusNotFound:
		// No release yet (or the repo is private): the game is "coming soon".
		c.Repos[key] = &cachedRelease{Checked: time.Now()}
	case resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusTooManyRequests:
		return errRateLimited
	default:
		return fmt.Errorf("%s: GitHub answered %s", repo, resp.Status)
	}
	c.save()
	return nil
}

// fetchCatalog downloads the published game catalog. It returns nil when the
// catalog hasn't changed or can't be read.
func (c *releaseCache) fetchCatalog() {
	url := remoteCatalogURL
	if v := os.Getenv("IRONITE_CATALOG_URL"); v != "" {
		url = v
	}
	if url == "off" {
		return
	}
	c.mu.Lock()
	etag := c.CatETag
	c.mu.Unlock()
	req, _ := http.NewRequest("GET", url, nil)
	req.Header.Set("User-Agent", "IroniteLauncher/"+version)
	if etag != "" {
		req.Header.Set("If-None-Match", etag)
	}
	resp, err := httpClient.Do(req)
	if err != nil {
		return
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return
	}
	b, err := io.ReadAll(io.LimitReader(resp.Body, 2<<20))
	if err != nil {
		return
	}
	var probe catalog
	if json.Unmarshal(b, &probe) != nil || probe.Schema < 2 || len(probe.Games) == 0 {
		return // only catalogs written for this launcher generation
	}
	c.mu.Lock()
	c.Catalog, c.CatETag = b, resp.Header.Get("ETag")
	c.save()
	c.mu.Unlock()
}

/* ---------- versions ---------- */

var numRe = regexp.MustCompile(`\d+`)

// newer reports whether version a is newer than b ("1.10.0" > "1.9.2").
func newer(a, b string) bool {
	pa, pb := numRe.FindAllString(a, -1), numRe.FindAllString(b, -1)
	for i := 0; i < len(pa) || i < len(pb); i++ {
		var x, y int
		if i < len(pa) {
			x, _ = strconv.Atoi(pa[i])
		}
		if i < len(pb) {
			y, _ = strconv.Atoi(pb[i])
		}
		if x != y {
			return x > y
		}
	}
	return false
}

func logErr(what string, err error) {
	if err != nil {
		log.Printf("%s: %v", what, err)
	}
}
