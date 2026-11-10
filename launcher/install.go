package main

// Downloads a game's release file from GitHub and installs it:
//   - a .zip is unpacked into Games\<Bundle>\<version>
//   - a setup program (NSIS or Inno Setup) is run silently into Games\<Bundle>\app
// Updating is the same as installing the newer release over the old one.

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"sync/atomic"
	"time"
)

type installRecord struct {
	Version     string `json:"version"`
	Dir         string `json:"dir"`  // folder the game runs from
	Kind        string `json:"kind"` // "zip", "nsis", "inno" or "legacy"
	Exe         string `json:"exe"`  // path relative to Dir
	InstalledAt int64  `json:"installedAt"`
}

type installJob struct {
	phase       atomic.Value // string: "Downloading", "Installing"
	done, total atomic.Int64
	err         string
	update      bool
	cancelled   atomic.Bool
}

var errCancelled = errors.New("cancelled")

func (j *installJob) setPhase(p string) { j.phase.Store(p) }
func (j *installJob) getPhase() string {
	if v, ok := j.phase.Load().(string); ok {
		return v
	}
	return "Preparing"
}

func gamesRoot() string             { return filepath.Join(cacheDir(), "Games") }
func gameBase(g catalogGame) string { return filepath.Join(gamesRoot(), g.Bundle) }

// download fetches an asset into the Downloads folder, reporting progress and
// checking its size and SHA-256 when GitHub provides them.
func download(a *ghAsset, job *installJob) (string, error) {
	dir := filepath.Join(cacheDir(), "Downloads")
	_ = os.MkdirAll(dir, 0o755)
	dest := filepath.Join(dir, filepath.Base(a.Name))
	tmp := dest + ".partial"
	_ = os.Remove(tmp)

	req, _ := http.NewRequest("GET", a.URL, nil)
	req.Header.Set("User-Agent", "IroniteLauncher/"+version)
	req.Header.Set("Accept", "application/octet-stream")
	client := &http.Client{Timeout: 0} // big files on slow connections
	resp, err := client.Do(req)
	if err != nil {
		return "", fmt.Errorf("couldn't reach GitHub (%v)", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("download failed: GitHub answered %s", resp.Status)
	}
	total := a.Size
	if total <= 0 {
		total = resp.ContentLength
	}
	job.total.Store(total)
	job.done.Store(0)

	f, err := os.Create(tmp)
	if err != nil {
		return "", err
	}
	h := sha256.New()
	buf := make([]byte, 256<<10)
	var n int64
	idle := time.AfterFunc(2*time.Minute, func() { resp.Body.Close() }) // stalled connection
	for {
		if job.cancelled.Load() {
			idle.Stop()
			f.Close()
			os.Remove(tmp)
			return "", errCancelled
		}
		k, rerr := resp.Body.Read(buf)
		if k > 0 {
			idle.Reset(2 * time.Minute)
			if _, werr := f.Write(buf[:k]); werr != nil {
				idle.Stop()
				f.Close()
				os.Remove(tmp)
				return "", werr
			}
			h.Write(buf[:k])
			n += int64(k)
			job.done.Store(n)
		}
		if rerr == io.EOF {
			break
		}
		if rerr != nil {
			idle.Stop()
			f.Close()
			os.Remove(tmp)
			return "", fmt.Errorf("download interrupted (%v)", rerr)
		}
	}
	idle.Stop()
	if err := f.Close(); err != nil {
		os.Remove(tmp)
		return "", err
	}
	if a.Size > 0 && n != a.Size {
		os.Remove(tmp)
		return "", fmt.Errorf("download incomplete (%d of %d bytes)", n, a.Size)
	}
	if d, ok := strings.CutPrefix(a.Digest, "sha256:"); ok && !strings.EqualFold(d, hex.EncodeToString(h.Sum(nil))) {
		os.Remove(tmp)
		return "", errors.New("the downloaded file is damaged (checksum mismatch); please try again")
	}
	_ = os.Remove(dest)
	if err := os.Rename(tmp, dest); err != nil {
		return "", err
	}
	return dest, nil
}

// installerKind tells NSIS and Inno Setup programs apart from their contents.
func installerKind(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	switch {
	case bytes.Contains(b, []byte("Inno Setup")):
		return "inno"
	case bytes.Contains(b, []byte("Nullsoft")) || bytes.Contains(b, []byte("NullsoftInst")):
		return "nsis"
	}
	return ""
}

// installFile installs a downloaded release file and returns the new record.
func installFile(g catalogGame, ver, file string, job *installJob) (*installRecord, error) {
	job.setPhase("Installing")
	if strings.HasSuffix(strings.ToLower(file), ".zip") {
		return installZip(g, ver, file, job)
	}
	kind := installerKind(file)
	if kind == "" {
		return nil, errors.New("the release file isn't a zip or a setup program the launcher knows")
	}
	dir := filepath.Join(gameBase(g), "app")
	_ = os.MkdirAll(dir, 0o755)
	var args string
	if kind == "inno" {
		args = `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /DIR="` + dir + `"`
	} else {
		args = `/S /D=` + dir // NSIS: /D must be last and unquoted
	}
	job.total.Store(0)
	code, err := runAndWait(file, args, dir)
	if err != nil {
		return nil, fmt.Errorf("couldn't run the setup program (%v)", err)
	}
	if code != 0 {
		return nil, fmt.Errorf("the setup program stopped with code %d", code)
	}
	exe := findExe(dir, g.Exe)
	if exe == "" {
		return nil, errors.New("setup finished but the game's program wasn't found")
	}
	removeOtherVersions(g, "app")
	return &installRecord{Version: ver, Dir: dir, Kind: kind, Exe: exe, InstalledAt: time.Now().Unix()}, nil
}

func installZip(g catalogGame, ver, file string, job *installJob) (*installRecord, error) {
	zr, err := zip.OpenReader(file)
	if err != nil {
		return nil, fmt.Errorf("the download isn't a valid zip (%v)", err)
	}
	defer zr.Close()

	// Most game zips hold a single top folder (e.g. Klondike/); unpack its contents.
	prefix := ""
	for i, f := range zr.File {
		top, _, found := strings.Cut(strings.TrimLeft(f.Name, "/"), "/")
		if !found {
			prefix = ""
			break
		}
		if i == 0 {
			prefix = top + "/"
		} else if top+"/" != prefix {
			prefix = ""
			break
		}
	}
	var total int64
	for _, f := range zr.File {
		total += int64(f.UncompressedSize64)
	}
	job.total.Store(total)
	job.done.Store(0)

	dest := filepath.Join(gameBase(g), safeName(ver))
	tmp := dest + ".partial"
	_ = os.RemoveAll(tmp)
	if err := os.MkdirAll(tmp, 0o755); err != nil {
		return nil, err
	}
	fail := func(err error) (*installRecord, error) { _ = os.RemoveAll(tmp); return nil, err }
	for _, f := range zr.File {
		rel := strings.TrimPrefix(strings.TrimLeft(f.Name, "/"), prefix)
		if rel == "" {
			continue
		}
		target := filepath.Join(tmp, filepath.FromSlash(rel))
		if !strings.HasPrefix(target, tmp+string(os.PathSeparator)) {
			return fail(errors.New("the zip contains an unsafe path"))
		}
		if f.FileInfo().IsDir() {
			if err := os.MkdirAll(target, 0o755); err != nil {
				return fail(err)
			}
			continue
		}
		_ = os.MkdirAll(filepath.Dir(target), 0o755)
		if err := extractOne(f, target, job); err != nil {
			return fail(err)
		}
	}
	exe := findExe(tmp, g.Exe)
	if exe == "" {
		return fail(errors.New("the game's program wasn't found in the download"))
	}
	_ = os.RemoveAll(dest)
	if err := os.Rename(tmp, dest); err != nil {
		return fail(err)
	}
	removeOtherVersions(g, filepath.Base(dest))
	return &installRecord{Version: ver, Dir: dest, Kind: "zip", Exe: exe, InstalledAt: time.Now().Unix()}, nil
}

func extractOne(f *zip.File, target string, job *installJob) error {
	in, err := f.Open()
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.OpenFile(target, os.O_CREATE|os.O_TRUNC|os.O_WRONLY, 0o755)
	if err != nil {
		return err
	}
	buf := make([]byte, 256<<10)
	for {
		n, rerr := in.Read(buf)
		if n > 0 {
			if _, werr := out.Write(buf[:n]); werr != nil {
				out.Close()
				return werr
			}
			job.done.Add(int64(n))
		}
		if rerr == io.EOF {
			break
		}
		if rerr != nil {
			out.Close()
			return rerr
		}
	}
	return out.Close()
}

var unsafeChars = regexp.MustCompile(`[^A-Za-z0-9._-]`)

func safeName(s string) string {
	s = unsafeChars.ReplaceAllString(s, "_")
	if s == "" || s == "." || s == ".." {
		return "game"
	}
	return s
}

// findExe returns the game's program relative to dir: the catalog's exe name
// if present (at the top or one level down), otherwise the only .exe at the top.
func findExe(dir, want string) string {
	if want != "" {
		if _, err := os.Stat(filepath.Join(dir, want)); err == nil {
			return want
		}
		var found string
		_ = filepath.WalkDir(dir, func(p string, d os.DirEntry, err error) error {
			if err != nil || found != "" {
				return nil
			}
			if !d.IsDir() && strings.EqualFold(d.Name(), want) {
				found, _ = filepath.Rel(dir, p)
				return filepath.SkipAll
			}
			return nil
		})
		if found != "" {
			return found
		}
	}
	entries, _ := os.ReadDir(dir)
	var exes []string
	for _, e := range entries {
		n := strings.ToLower(e.Name())
		if !e.IsDir() && strings.HasSuffix(n, ".exe") && !strings.HasPrefix(n, "unins") && !strings.HasPrefix(n, "uninstall") {
			exes = append(exes, e.Name())
		}
	}
	if len(exes) == 1 {
		return exes[0]
	}
	return ""
}

// removeOtherVersions deletes older copies of a game after an update.
func removeOtherVersions(g catalogGame, keep string) {
	entries, _ := os.ReadDir(gameBase(g))
	for _, e := range entries {
		if e.IsDir() && e.Name() != keep && e.Name() != "app" {
			_ = os.RemoveAll(filepath.Join(gameBase(g), e.Name()))
		}
	}
}

var uninstRe = regexp.MustCompile(`(?i)^(unins\d+|uninst.*|uninstall.*)\.exe$`)

// removeInstall uninstalls a game: runs its own uninstaller for setup-program
// games, then deletes its folder. Saves live in the user's profile and are kept.
func removeInstall(g catalogGame, rec *installRecord) error {
	if rec != nil && (rec.Kind == "nsis" || rec.Kind == "inno") {
		entries, _ := os.ReadDir(rec.Dir)
		for _, e := range entries {
			if e.IsDir() || !uninstRe.MatchString(e.Name()) {
				continue
			}
			args := `/S _?=` + rec.Dir
			if rec.Kind == "inno" {
				args = `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`
			}
			_, _ = runAndWait(filepath.Join(rec.Dir, e.Name()), args, rec.Dir)
			break
		}
	}
	var err error
	for i := 0; i < 10; i++ { // an uninstaller may still be letting go of files
		if err = os.RemoveAll(gameBase(g)); err == nil {
			return nil
		}
		time.Sleep(500 * time.Millisecond)
	}
	return err
}

// findLegacy finds a game unpacked by launcher 1.x (Games\<Bundle>\<version>).
func findLegacy(g catalogGame) *installRecord {
	if g.Bundle == "" {
		return nil
	}
	entries, err := os.ReadDir(gameBase(g))
	if err != nil {
		return nil
	}
	var best *installRecord
	for _, e := range entries {
		if !e.IsDir() || strings.HasSuffix(e.Name(), ".partial") || e.Name() == "app" {
			continue
		}
		dir := filepath.Join(gameBase(g), e.Name())
		exe := findExe(dir, g.Exe)
		if exe == "" {
			continue
		}
		if best == nil || newer(e.Name(), best.Version) {
			info, _ := e.Info()
			var at int64
			if info != nil {
				at = info.ModTime().Unix()
			}
			best = &installRecord{Version: e.Name(), Dir: dir, Kind: "zip", Exe: exe, InstalledAt: at}
		}
	}
	return best
}
