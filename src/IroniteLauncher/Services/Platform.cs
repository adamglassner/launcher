using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Ironite.Launcher.Services;

/// <summary>A running program (all lower case).</summary>
public readonly record struct ProcInfo(string Name, string Path, string Family);

/// <summary>Windows helpers: running programs, program icons, browsers, shortcuts, PowerShell.</summary>
public static class Platform
{
    // ---- running programs ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W pe);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W pe);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder buf, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr h, ref uint len, StringBuilder? name);

    /// <summary>Running programs with their full path and, for Store / Xbox apps, their package family name.</summary>
    public static List<ProcInfo> Processes()
    {
        var list = new List<ProcInfo>();
        var snap = CreateToolhelp32Snapshot(2, 0); // TH32CS_SNAPPROCESS
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            var pe = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            var buf = new StringBuilder(1024);
            for (var ok = Process32FirstW(snap, ref pe); ok; ok = Process32NextW(snap, ref pe))
            {
                string path = "", family = "";
                var h = OpenProcess(0x1000, false, pe.th32ProcessID); // QUERY_LIMITED_INFORMATION
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        buf.Clear(); uint size = (uint)buf.Capacity;
                        if (QueryFullProcessImageNameW(h, 0, buf, ref size)) path = buf.ToString().ToLowerInvariant();
                        buf.Clear(); size = (uint)buf.Capacity;
                        try { if (GetPackageFamilyName(h, ref size, buf) == 0) family = buf.ToString().ToLowerInvariant(); }
                        catch (EntryPointNotFoundException) { /* old Windows */ }
                    }
                    finally { CloseHandle(h); }
                }
                list.Add(new ProcInfo((pe.szExeFile ?? "").ToLowerInvariant(), path, family));
            }
        }
        finally { CloseHandle(snap); }
        return list;
    }

    // ---- program icons ----

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIconsW(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint n, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    /// <summary>A program's icon (as large as it has, up to 256 px) as PNG bytes.</summary>
    public static byte[]? ExeIconPng(string path)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        var n = PrivateExtractIconsW(path, 0, 256, 256, icons, ids, 1, 0);
        if (n == 0 || n == 0xFFFFFFFF || icons[0] == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"icon of {path}", ex);
            return null;
        }
        finally { DestroyIcon(icons[0]); }
    }

    // ---- browsers (for web games) ----

    public static string? FindBrowser()
    {
        var roots = new[] { "ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA" }
            .Select(Environment.GetEnvironmentVariable).Where(v => !string.IsNullOrEmpty(v)).ToList();
        foreach (var rel in new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe", @"BraveSoftware\Brave-Browser\Application\brave.exe" })
            foreach (var r in roots)
            {
                var p = Path.Combine(r!, rel);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    /// <summary>Opens a link in the player's normal browser.</summary>
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"open {url}", ex); }
    }

    public static void OpenFolder(string dir)
    {
        try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"open {dir}", ex); }
    }

    // ---- PowerShell ----

    public static string PowerShellPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Runs a PowerShell script hidden and returns its output (UTF-8).</summary>
    public static async Task<(int Code, string Output)> RunPowerShellAsync(string script, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + script)) })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell didn't start");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new TimeoutException("PowerShell took too long"); }
        var output = (await outTask).TrimStart('﻿').Trim();
        var err = (await errTask).Trim();
        return (p.ExitCode, output.Length > 0 ? output : err);
    }

    // ---- desktop shortcuts ----

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>Puts a shortcut on the desktop (the real one, even when it's moved to OneDrive) and returns its path.</summary>
    public static async Task<string> CreateShortcutAsync(string name, string target, string args, string icon, string desc)
    {
        var script = $@"$ErrorActionPreference = 'Stop'
$d = [Environment]::GetFolderPath('Desktop')
$p = Join-Path $d ({Q(name + ".lnk")})
$s = (New-Object -ComObject WScript.Shell).CreateShortcut($p)
$s.TargetPath = {Q(target)}
$s.Arguments = {Q(args)}
$s.WorkingDirectory = {Q(Path.GetDirectoryName(target) ?? "")}
$s.IconLocation = {Q(icon + ",0")}
$s.Description = {Q(desc)}
$s.Save()
Write-Output $p";
        var (code, output) = await RunPowerShellAsync(script, TimeSpan.FromSeconds(30));
        if (code != 0) throw new IOException($"couldn't make the shortcut ({output})");
        return output.Split('\n').Last().Trim();
    }

    public static string SafeFileName(string s)
    {
        var bad = "<>:\"/\\|?*";
        var t = new string(s.Where(c => c >= 32 && !bad.Contains(c)).ToArray()).TrimEnd('.', ' ').Trim();
        return t.Length == 0 ? "Ironite game" : t;
    }

    public static string JsonString(string s) => JsonSerializer.Serialize(s);
}
