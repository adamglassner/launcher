//go:build !windows

package main

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
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
