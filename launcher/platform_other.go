//go:build !windows

package main

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

// Non-Windows stubs, used only for development and testing.
func fatal(text string)                { fmt.Fprintln(os.Stderr, text); os.Exit(1) }
func singleInstance() bool             { return true }
func waitForLock() bool                { return true }
func focusWindow(string) bool          { return false }
func killLauncher(int)                 {}
func msgBox(text string, _ uintptr)    { fmt.Fprintln(os.Stderr, text) }
func openWindow(string) (func(), bool) { return nil, false }
func openDefaultBrowser(url string)    { fmt.Println("Open:", url) }

// Test stand-in for a Windows game: a process that stays open until killed.
func gameCommand(exe, dir string) *exec.Cmd {
	cmd := exec.Command("sleep", "86400")
	cmd.Dir = dir
	return cmd
}

// Test stand-in for a web game window.
func webGameCommand(url, profile string) (*exec.Cmd, error) {
	return exec.Command("sleep", "86400"), nil
}

// Setup programs only run on Windows.
func runAndWait(file, args, dir string) (int, error) {
	return -1, errors.New("setup programs only run on Windows")
}
func startSelfUpdate(setup string) error {
	fmt.Println("would run", setup, "/S and restart afterwards")
	return nil
}
func helperPath() string              { return filepath.Join(os.TempDir(), "IroniteLauncherUpdate") }
func afterUpdate(pidText, exe string) {}

// Picking a program with a file dialog works on Windows only.
func browseForGame(string) (string, error) {
	return "", errors.New("browsing for a program works on Windows; type its full path instead")
}
func exeIconPNG(string) ([]byte, error) { return nil, errors.New("icons are read on Windows only") }

// Xbox / Microsoft Store games are Windows-only. In development a sample
// list is shown so the page can be tried out.
func storeAppCommand(appID string) *exec.Cmd { return exec.Command("true") }
func processList() []procInfo                { return fakeProcs() }
func listStoreApps() ([]storeApp, error) {
	if os.Getenv("IRONITE_DEV") != "" {
		return []storeApp{
			{Name: "Forza Horizon 5", AppID: "Microsoft.SunriseBaseGame_8wekyb3d8bbwe!SunriseReleaseFinal", Location: `C:\XboxGames\Forza Horizon 5\Content`, Game: true},
			{Name: "Microsoft Solitaire Collection", AppID: "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe!App", Location: `C:\Program Files\WindowsApps\Microsoft.MicrosoftSolitaireCollection`, Game: true},
			{Name: "Minecraft Launcher", AppID: "Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft", Location: `C:\XboxGames\Minecraft Launcher\Content`, Game: true},
			{Name: "Calculator", AppID: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", Location: `C:\Program Files\WindowsApps\Microsoft.WindowsCalculator`},
			{Name: "Xbox", AppID: "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", Location: `C:\Program Files\WindowsApps\Microsoft.GamingApp`},
		}, nil
	}
	return nil, errors.New("Xbox and Microsoft Store games can be added on Windows")
}

// Desktop shortcuts are made on Windows; in development one is written to
// IRONITE_DESKTOP (if set) so the feature can be tried out.
func createShortcut(name, target, args, icon, desc string) (string, error) {
	dir := os.Getenv("IRONITE_DESKTOP")
	if dir == "" {
		return "", errors.New("desktop shortcuts are made on Windows")
	}
	p := filepath.Join(dir, name+".lnk.txt")
	return p, os.WriteFile(p, []byte(target+" "+args+"\nicon: "+icon+"\n"+desc+"\n"), 0o644)
}

// In development, IRONITE_FAKE_PROCS names a file of running programs
// ("path|package family" per line) so game detection can be tried out.
func fakeProcs() []procInfo {
	b, err := os.ReadFile(os.Getenv("IRONITE_FAKE_PROCS"))
	if err != nil {
		return nil
	}
	var out []procInfo
	for _, line := range strings.Split(string(b), "\n") {
		path, fam, _ := strings.Cut(strings.TrimSpace(line), "|")
		if path == "" && fam == "" {
			continue
		}
		path = strings.ToLower(path)
		out = append(out, procInfo{Name: strings.ToLower(filepath.Base(path)), Path: path, Family: strings.ToLower(fam)})
	}
	return out
}
