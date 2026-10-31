//go:build windows

package main

import (
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

var (
	user32          = syscall.NewLazyDLL("user32.dll")
	kernel32        = syscall.NewLazyDLL("kernel32.dll")
	pMessageBox     = user32.NewProc("MessageBoxW")
	pFindWindow     = user32.NewProc("FindWindowW")
	pShowWindow     = user32.NewProc("ShowWindow")
	pSetForeground  = user32.NewProc("SetForegroundWindow")
	pCreateMutex    = kernel32.NewProc("CreateMutexW")
	pSetDpiAwareCtx = user32.NewProc("SetProcessDpiAwarenessContext")
)

func u16(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }

func msgBox(text string, flags uintptr) {
	pMessageBox.Call(0, uintptr(unsafe.Pointer(u16(text))), uintptr(unsafe.Pointer(u16(productName))), flags)
}
func fatal(text string) { msgBox(text, 0x10); os.Exit(1) }

// singleInstance takes the launcher's lock. It returns false if another
// launcher already holds it.
func singleInstance() bool {
	if pSetDpiAwareCtx.Find() == nil {
		pSetDpiAwareCtx.Call(^uintptr(3)) // PER_MONITOR_AWARE_V2 (-4)
	}
	return takeLock()
}

func takeLock() bool {
	h, _, err := pCreateMutex.Call(0, 0, uintptr(unsafe.Pointer(u16(`Local\IroniteLauncher`))))
	if err == syscall.Errno(183) { // ERROR_ALREADY_EXISTS
		pCloseHandle.Call(h) // let go, so the lock disappears when the other launcher exits
		return false
	}
	return h != 0
}

// waitForLock waits up to 8 seconds for a closing launcher to let go.
func waitForLock() bool {
	for i := 0; i < 40; i++ {
		if takeLock() {
			return true
		}
		time.Sleep(200 * time.Millisecond)
	}
	return false
}

// focusWindow brings an open launcher window to the front.
func focusWindow(title string) bool {
	h, _, _ := pFindWindow.Call(0, uintptr(unsafe.Pointer(u16(title))))
	if h == 0 {
		return false
	}
	pShowWindow.Call(h, 9) // SW_RESTORE
	pSetForeground.Call(h)
	return true
}

var (
	pOpenProcess      = kernel32.NewProc("OpenProcess")
	pQueryImageName   = kernel32.NewProc("QueryFullProcessImageNameW")
	pTerminateProcess = kernel32.NewProc("TerminateProcess")
)

// killLauncher ends a launcher that stopped responding (only if that process
// really is IroniteLauncher.exe).
func killLauncher(pid int) {
	h, _, _ := pOpenProcess.Call(0x1000|0x0001, 0, uintptr(pid)) // QUERY_LIMITED_INFORMATION | TERMINATE
	if h == 0 {
		return
	}
	defer pCloseHandle.Call(h)
	buf := make([]uint16, 1024)
	n := uint32(len(buf))
	if r, _, _ := pQueryImageName.Call(h, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&n))); r == 0 {
		return
	}
	if !strings.EqualFold(filepath.Base(syscall.UTF16ToString(buf[:n])), "IroniteLauncher.exe") {
		return
	}
	pTerminateProcess.Call(h, 1)
}

func findBrowser() string {
	var roots []string
	for _, k := range []string{"ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"} {
		if v := os.Getenv(k); v != "" {
			roots = append(roots, v)
		}
	}
	for _, rel := range []string{`Microsoft\Edge\Application\msedge.exe`, `Google\Chrome\Application\chrome.exe`, `BraveSoftware\Brave-Browser\Application\brave.exe`} {
		for _, r := range roots {
			p := filepath.Join(r, rel)
			if _, err := os.Stat(p); err == nil {
				return p
			}
		}
	}
	return ""
}

// openWindow starts an app-mode browser window with its own profile so the
// window gets its own process we can wait on.
func openWindow(url string) (func(), bool) {
	b := findBrowser()
	if b == "" {
		return nil, false
	}
	profile := filepath.Join(cacheDir(), "WebView")
	_ = os.MkdirAll(profile, 0o755)
	cmd := exec.Command(b,
		"--app="+url,
		"--user-data-dir="+profile,
		"--window-size=1320,860",
		"--no-first-run", "--no-default-browser-check", "--disable-extensions",
		"--disable-background-mode", // don't keep running after the window closes
		"--disable-features=Translate,msEdgeSidebarV2,msUndersideButton,msHubApps",
		"--disable-pinch", "--overscroll-history-navigation=0",
	)
	if err := cmd.Start(); err != nil {
		return nil, false
	}
	return func() { _ = cmd.Wait() }, true
}

func openDefaultBrowser(url string) {
	_ = exec.Command("rundll32", "url.dll,FileProtocolHandler", url).Start()
}

func gameCommand(exe, dir string) *exec.Cmd {
	cmd := exec.Command(exe)
	cmd.Dir = dir
	return cmd
}

// webGameCommand opens a web game in an app-mode window with its own browser
// profile, so the window has its own process (for play-time tracking).
func webGameCommand(url, profile string) (*exec.Cmd, error) {
	b := findBrowser()
	if b == "" {
		return nil, errors.New("Microsoft Edge or Google Chrome is needed to play web games")
	}
	return exec.Command(b,
		"--app="+url,
		"--user-data-dir="+profile,
		"--window-size=1280,860",
		"--no-first-run", "--no-default-browser-check", "--disable-background-mode",
		"--disable-features=Translate,msEdgeSidebarV2,msUndersideButton,msHubApps",
	), nil
}

var (
	shell32           = syscall.NewLazyDLL("shell32.dll")
	pShellExecuteEx   = shell32.NewProc("ShellExecuteExW")
	pWaitForSingleObj = kernel32.NewProc("WaitForSingleObject")
	pGetExitCode      = kernel32.NewProc("GetExitCodeProcess")
	pCloseHandle      = kernel32.NewProc("CloseHandle")
)

type shellExecuteInfo struct {
	cbSize       uint32
	fMask        uint32
	hwnd         uintptr
	lpVerb       *uint16
	lpFile       *uint16
	lpParameters *uint16
	lpDirectory  *uint16
	nShow        int32
	hInstApp     uintptr
	lpIDList     uintptr
	lpClass      *uint16
	hkeyClass    uintptr
	dwHotKey     uint32
	hIcon        uintptr
	hProcess     uintptr
}

// shellRun starts a program through the shell, so a setup program that needs
// administrator rights shows the usual Windows prompt. It returns the process
// handle (the caller closes it).
func shellRun(file, args, dir string) (uintptr, error) {
	sei := shellExecuteInfo{
		fMask:        0x40 | 0x100 | 0x400, // NOCLOSEPROCESS | NOASYNC | FLAG_NO_UI
		lpVerb:       u16("open"),
		lpFile:       u16(file),
		lpParameters: u16(args),
		lpDirectory:  u16(dir),
		nShow:        1,
	}
	sei.cbSize = uint32(unsafe.Sizeof(sei))
	r, _, err := pShellExecuteEx.Call(uintptr(unsafe.Pointer(&sei)))
	if r == 0 {
		if err == syscall.Errno(1223) { // ERROR_CANCELLED: "No" on the Windows prompt
			return 0, errors.New("cancelled at the Windows permission prompt")
		}
		return 0, err
	}
	return sei.hProcess, nil
}

// runAndWait runs a setup program or uninstaller and waits for it to finish.
func runAndWait(file, args, dir string) (int, error) {
	h, err := shellRun(file, args, dir)
	if err != nil {
		return -1, err
	}
	if h == 0 {
		return 0, nil
	}
	defer pCloseHandle.Call(h)
	pWaitForSingleObj.Call(h, 0xFFFFFFFF)
	var code uint32
	pGetExitCode.Call(h, uintptr(unsafe.Pointer(&code)))
	return int(code), nil
}

// startSelfUpdate starts the new launcher setup silently; it closes this
// launcher, installs the update and opens the launcher again.
func startSelfUpdate(setup string) error {
	h, err := shellRun(setup, "/S /relaunch", filepath.Dir(setup))
	if err != nil {
		return err
	}
	if h != 0 {
		pCloseHandle.Call(h)
	}
	return nil
}
