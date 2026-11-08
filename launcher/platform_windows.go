//go:build windows

package main

import (
	"bytes"
	"errors"
	"fmt"
	"image"
	"image/png"
	"io"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unicode/utf16"
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
var pGetProcessId = kernel32.NewProc("GetProcessId")

func helperPath() string { return filepath.Join(os.TempDir(), "IroniteLauncherUpdate.exe") }

// startSelfUpdate starts the new launcher setup silently. Setup closes this
// launcher and replaces it. A small helper (a copy of this program under
// another name, so setup doesn't close it) waits for setup to finish and then
// opens the new launcher as the signed-in user.
func startSelfUpdate(setup string) error {
	exe, err := os.Executable()
	helperOK := err == nil && copyFile(exe, helperPath()) == nil
	args := "/S"
	if !helperOK {
		args = "/S /relaunch" // let setup open the launcher instead
	}
	h, err := shellRun(setup, args, filepath.Dir(setup))
	if err != nil {
		return err
	}
	if h == 0 {
		return nil
	}
	defer pCloseHandle.Call(h)
	if !helperOK {
		return nil
	}
	pid, _, _ := pGetProcessId.Call(h)
	cmd := exec.Command(helperPath(), "--after-update", strconv.Itoa(int(pid)), exe)
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x00000008 | 0x00000200} // DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
	if err := cmd.Start(); err != nil {
		return fmt.Errorf("couldn't start the restart helper (%v)", err)
	}
	_ = cmd.Process.Release()
	return nil
}

// afterUpdate runs in the helper: wait for setup to finish, then open the
// (new) launcher.
func afterUpdate(pidText, exe string) {
	pid, _ := strconv.Atoi(pidText)
	waited := false
	if pid > 0 {
		if h, _, _ := pOpenProcess.Call(0x00100000, 0, uintptr(pid)); h != 0 { // SYNCHRONIZE
			pWaitForSingleObj.Call(h, 15*60*1000)
			pCloseHandle.Call(h)
			waited = true
		}
	}
	if !waited {
		time.Sleep(45 * time.Second)
	}
	time.Sleep(1500 * time.Millisecond) // let setup's files settle
	cmd := exec.Command(exe)
	cmd.Dir = filepath.Dir(exe)
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x00000008 | 0x00000200}
	if err := cmd.Start(); err != nil {
		log.Printf("restart after update: %v", err)
		return
	}
	log.Printf("restarted %s after update", exe)
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	if _, err := io.Copy(out, in); err != nil {
		out.Close()
		return err
	}
	return out.Close()
}

/* ---------- the player's own games: file dialog and program icons ---------- */

var (
	comdlg32           = syscall.NewLazyDLL("comdlg32.dll")
	pGetOpenFileName   = comdlg32.NewProc("GetOpenFileNameW")
	pCommDlgExtError   = comdlg32.NewProc("CommDlgExtendedError")
	ole32              = syscall.NewLazyDLL("ole32.dll")
	pCoInitializeEx    = ole32.NewProc("CoInitializeEx")
	pCoUninitialize    = ole32.NewProc("CoUninitialize")
	pPrivateExtractIco = user32.NewProc("PrivateExtractIconsW")
	pDestroyIcon       = user32.NewProc("DestroyIcon")
	pGetIconInfo       = user32.NewProc("GetIconInfo")
	gdi32              = syscall.NewLazyDLL("gdi32.dll")
	pGetObject         = gdi32.NewProc("GetObjectW")
	pGetDIBits         = gdi32.NewProc("GetDIBits")
	pDeleteObject      = gdi32.NewProc("DeleteObject")
	pCreateCompatDC    = gdi32.NewProc("CreateCompatibleDC")
	pDeleteDC          = gdi32.NewProc("DeleteDC")
)

type openFileNameW struct {
	lStructSize       uint32
	hwndOwner         uintptr
	hInstance         uintptr
	lpstrFilter       *uint16
	lpstrCustomFilter *uint16
	nMaxCustFilter    uint32
	nFilterIndex      uint32
	lpstrFile         *uint16
	nMaxFile          uint32
	lpstrFileTitle    *uint16
	nMaxFileTitle     uint32
	lpstrInitialDir   *uint16
	lpstrTitle        *uint16
	Flags             uint32
	nFileOffset       uint16
	nFileExtension    uint16
	lpstrDefExt       *uint16
	lCustData         uintptr
	lpfnHook          uintptr
	lpTemplateName    *uint16
	pvReserved        uintptr
	dwReserved        uint32
	FlagsEx           uint32
}

// browseForGame shows the Windows "Open" dialog for picking a game's program.
// It returns "" if the player cancels.
func browseForGame(ownerTitle string) (string, error) {
	type result struct {
		path string
		err  error
	}
	done := make(chan result, 1)
	go func() {
		runtime.LockOSThread()
		defer runtime.UnlockOSThread()
		pCoInitializeEx.Call(0, 0x2) // COINIT_APARTMENTTHREADED
		defer pCoUninitialize.Call()

		filter := utf16.Encode([]rune("Games (*.exe)\x00*.exe\x00\x00"))
		buf := make([]uint16, 4096)
		initial := u16(os.Getenv("ProgramFiles"))
		owner, _, _ := pFindWindow.Call(0, uintptr(unsafe.Pointer(u16(ownerTitle))))
		ofn := openFileNameW{
			hwndOwner:       owner,
			lpstrFilter:     &filter[0],
			nFilterIndex:    1,
			lpstrFile:       &buf[0],
			nMaxFile:        uint32(len(buf)),
			lpstrInitialDir: initial,
			lpstrTitle:      u16("Choose a game to add to Ironite Launcher"),
			// FILEMUSTEXIST | PATHMUSTEXIST | NOCHANGEDIR | EXPLORER | DONTADDTORECENT
			Flags: 0x1000 | 0x800 | 0x8 | 0x80000 | 0x2000000,
		}
		ofn.lStructSize = uint32(unsafe.Sizeof(ofn))
		r, _, _ := pGetOpenFileName.Call(uintptr(unsafe.Pointer(&ofn)))
		if r == 0 {
			if code, _, _ := pCommDlgExtError.Call(); code != 0 {
				done <- result{"", fmt.Errorf("the file dialog failed (code %#x)", code)}
				return
			}
			done <- result{"", nil} // cancelled
			return
		}
		done <- result{syscall.UTF16ToString(buf), nil}
	}()
	res := <-done
	return res.path, res.err
}

type iconInfo struct {
	fIcon    int32
	xHotspot uint32
	yHotspot uint32
	hbmMask  uintptr
	hbmColor uintptr
}

type bitmapW struct {
	bmType       int32
	bmWidth      int32
	bmHeight     int32
	bmWidthBytes int32
	bmPlanes     uint16
	bmBitsPixel  uint16
	bmBits       uintptr
}

type bitmapInfoHeader struct {
	biSize          uint32
	biWidth         int32
	biHeight        int32
	biPlanes        uint16
	biBitCount      uint16
	biCompression   uint32
	biSizeImage     uint32
	biXPelsPerMeter int32
	biYPelsPerMeter int32
	biClrUsed       uint32
	biClrImportant  uint32
}

// exeIconPNG reads a program's icon (as large as it has, up to 256 px) as a PNG.
func exeIconPNG(path string) ([]byte, error) {
	var hicon uintptr
	var id uint32
	n, _, _ := pPrivateExtractIco.Call(uintptr(unsafe.Pointer(u16(path))), 0, 256, 256,
		uintptr(unsafe.Pointer(&hicon)), uintptr(unsafe.Pointer(&id)), 1, 0)
	if n == 0 || n == 0xFFFFFFFF || hicon == 0 {
		return nil, errors.New("the program has no icon")
	}
	defer pDestroyIcon.Call(hicon)
	var ii iconInfo
	if r, _, _ := pGetIconInfo.Call(hicon, uintptr(unsafe.Pointer(&ii))); r == 0 {
		return nil, errors.New("couldn't read the icon")
	}
	defer pDeleteObject.Call(ii.hbmMask)
	if ii.hbmColor == 0 {
		return nil, errors.New("the icon is monochrome")
	}
	defer pDeleteObject.Call(ii.hbmColor)
	var bm bitmapW
	pGetObject.Call(ii.hbmColor, unsafe.Sizeof(bm), uintptr(unsafe.Pointer(&bm)))
	w, h := int(bm.bmWidth), int(bm.bmHeight)
	if w <= 0 || h <= 0 || w > 1024 || h > 1024 {
		return nil, errors.New("unexpected icon size")
	}
	dc, _, _ := pCreateCompatDC.Call(0)
	defer pDeleteDC.Call(dc)
	read := func(hbm uintptr) []byte {
		bih := bitmapInfoHeader{biWidth: int32(w), biHeight: -int32(h), biPlanes: 1, biBitCount: 32}
		bih.biSize = uint32(unsafe.Sizeof(bih))
		px := make([]byte, w*h*4)
		// BITMAPINFO = header + room for a color table (unused at 32 bpp).
		info := make([]byte, int(bih.biSize)+16)
		*(*bitmapInfoHeader)(unsafe.Pointer(&info[0])) = bih
		pGetDIBits.Call(dc, hbm, 0, uintptr(h), uintptr(unsafe.Pointer(&px[0])), uintptr(unsafe.Pointer(&info[0])), 0)
		return px
	}
	color := read(ii.hbmColor)
	hasAlpha := false
	for i := 3; i < len(color); i += 4 {
		if color[i] != 0 {
			hasAlpha = true
			break
		}
	}
	var mask []byte
	if !hasAlpha {
		mask = read(ii.hbmMask) // old-style icon: transparency comes from the mask
	}
	img := image.NewNRGBA(image.Rect(0, 0, w, h))
	for i := 0; i < w*h; i++ {
		b, g, r, a := color[i*4], color[i*4+1], color[i*4+2], color[i*4+3]
		if !hasAlpha {
			a = 255
			if mask != nil && mask[i*4] != 0 {
				a = 0
			}
		}
		img.Pix[i*4], img.Pix[i*4+1], img.Pix[i*4+2], img.Pix[i*4+3] = r, g, b, a
	}
	var out bytes.Buffer
	if err := png.Encode(&out, img); err != nil {
		return nil, err
	}
	return out.Bytes(), nil
}
