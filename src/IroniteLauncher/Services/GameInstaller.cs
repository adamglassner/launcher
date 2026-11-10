using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ironite.Launcher.Models;

namespace Ironite.Launcher.Services;

/// <summary>A download or install in progress (shown on the game's tile and buttons).</summary>
public sealed class InstallJob
{
    private volatile string _phase = "Preparing";
    public string Phase { get => _phase; set => _phase = value; }
    public long Done, Total;
    public string? Error;
    public bool Update;
    public readonly CancellationTokenSource Cancel = new();

    public int Progress
    {
        get
        {
            var t = Interlocked.Read(ref Total);
            return t > 0 ? (int)Math.Clamp(Interlocked.Read(ref Done) * 100 / t, 0, 100) : 0;
        }
    }
}

/// <summary>
/// Downloads a game's release file from GitHub and installs it:
///   - a .zip is unpacked into Games\&lt;Bundle&gt;\&lt;version&gt;
///   - a setup program (NSIS or Inno Setup) is run silently into Games\&lt;Bundle&gt;\app
/// Updating is the same as installing the newer release over the old one.
/// </summary>
public static class GameInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10, ConnectTimeout = TimeSpan.FromSeconds(20) })
        { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubService.UserAgent);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        return c;
    }

    public static string GameBase(CatalogGame g) => Path.Combine(AppPaths.Games, g.Bundle ?? g.Id);

    /// <summary>Downloads an asset into the Downloads folder, checking its size and SHA-256 when GitHub gives them.</summary>
    public static async Task<string> DownloadAsync(GhAsset asset, InstallJob job)
    {
        Directory.CreateDirectory(AppPaths.Downloads);
        var dest = Path.Combine(AppPaths.Downloads, Path.GetFileName(asset.Name));
        var tmp = dest + ".partial";
        TryDelete(tmp);
        var ct = job.Cancel.Token;
        try
        {
            using var resp = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                throw new IOException($"download failed: GitHub answered {(int)resp.StatusCode} {resp.ReasonPhrase}");
            var total = asset.Size > 0 ? asset.Size : resp.Content.Headers.ContentLength ?? 0;
            Interlocked.Exchange(ref job.Total, total);
            Interlocked.Exchange(ref job.Done, 0);

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long n = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buf = new byte[256 << 10];
                while (true)
                {
                    // A stalled connection gives up after two minutes without data.
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(TimeSpan.FromMinutes(2));
                    int k;
                    try { k = await src.ReadAsync(buf, idle.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("download interrupted (no data for two minutes)"); }
                    if (k == 0) break;
                    await dst.WriteAsync(buf.AsMemory(0, k), ct);
                    sha.AppendData(buf, 0, k);
                    n += k;
                    Interlocked.Exchange(ref job.Done, n);
                }
            }
            if (asset.Size > 0 && n != asset.Size)
                throw new IOException($"download incomplete ({n} of {asset.Size} bytes)");
            if (asset.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(d[7..], Convert.ToHexString(sha.GetHashAndReset()), StringComparison.OrdinalIgnoreCase))
                throw new IOException("the downloaded file is damaged (checksum mismatch); please try again");
            File.Move(tmp, dest, overwrite: true);
            return dest;
        }
        catch (HttpRequestException ex)
        {
            TryDelete(tmp);
            throw new IOException($"couldn't reach GitHub ({ex.Message})");
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>Tells NSIS and Inno Setup programs apart from their contents.</summary>
    public static string InstallerKind(string path)
    {
        try
        {
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            if (text.Contains("Inno Setup", StringComparison.Ordinal)) return "inno";
            if (text.Contains("Nullsoft", StringComparison.Ordinal)) return "nsis";
        }
        catch { /* unreadable */ }
        return "";
    }

    /// <summary>Installs a downloaded release file and returns the new install record.</summary>
    public static async Task<InstallRecord> InstallFileAsync(CatalogGame g, string version, string file, InstallJob job)
    {
        job.Phase = "Installing";
        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return await Task.Run(() => InstallZip(g, version, file, job));

        var kind = InstallerKind(file);
        if (kind == "") throw new InvalidDataException("the release file isn't a zip or a setup program the launcher knows");
        var dir = Path.Combine(GameBase(g), "app");
        Directory.CreateDirectory(dir);
        var args = kind == "inno"
            ? $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /DIR=\"{dir}\""
            : "/S /D=" + dir; // NSIS: /D must be last and unquoted
        Interlocked.Exchange(ref job.Total, 0);
        int code;
        try { code = await RunAndWaitAsync(file, args, dir); }
        catch (Exception ex) { throw new IOException($"couldn't run the setup program ({ex.Message})"); }
        if (code != 0) throw new IOException($"the setup program stopped with code {code}");
        var exe = FindExe(dir, g.Exe);
        if (exe == "") throw new FileNotFoundException("setup finished but the game's program wasn't found");
        RemoveOtherVersions(g, "app");
        return new InstallRecord { Version = version, Dir = dir, Kind = kind, Exe = exe, InstalledAt = Now() };
    }

    private static InstallRecord InstallZip(CatalogGame g, string version, string file, InstallJob job)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(file); }
        catch (Exception ex) { throw new InvalidDataException($"the download isn't a valid zip ({ex.Message})"); }
        using (zip)
        {
            // Most game zips hold a single top folder (e.g. Klondike/); unpack its contents.
            string? prefix = null;
            foreach (var e in zip.Entries)
            {
                var name = e.FullName.Replace('\\', '/').TrimStart('/');
                var slash = name.IndexOf('/');
                if (slash < 0) { prefix = null; break; }
                var top = name[..(slash + 1)];
                if (prefix == null) prefix = top;
                else if (prefix != top) { prefix = null; break; }
            }
            prefix ??= "";
            Interlocked.Exchange(ref job.Total, zip.Entries.Sum(e => e.Length));
            Interlocked.Exchange(ref job.Done, 0);

            var dest = Path.Combine(GameBase(g), SafeName(version));
            var tmp = dest + ".partial";
            TryDeleteDir(tmp);
            Directory.CreateDirectory(tmp);
            var root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
            try
            {
                var buf = new byte[256 << 10];
                foreach (var e in zip.Entries)
                {
                    var rel = e.FullName.Replace('\\', '/').TrimStart('/');
                    if (prefix.Length > 0 && rel.StartsWith(prefix, StringComparison.Ordinal)) rel = rel[prefix.Length..];
                    if (rel == "") continue;
                    var target = Path.GetFullPath(Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("the zip contains an unsafe path");
                    if (rel.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = e.Open();
                    using var output = File.Create(target);
                    int k;
                    while ((k = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, k);
                        Interlocked.Add(ref job.Done, k);
                    }
                }
                var exe = FindExe(tmp, g.Exe);
                if (exe == "") throw new FileNotFoundException("the game's program wasn't found in the download");
                TryDeleteDir(dest);
                Directory.Move(tmp, dest);
                RemoveOtherVersions(g, Path.GetFileName(dest));
                return new InstallRecord { Version = version, Dir = dest, Kind = "zip", Exe = exe, InstalledAt = Now() };
            }
            catch
            {
                TryDeleteDir(tmp);
                throw;
            }
        }
    }

    private static readonly Regex Unsafe = new(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);

    public static string SafeName(string s)
    {
        s = Unsafe.Replace(s, "_");
        return s is "" or "." or ".." ? "game" : s;
    }

    /// <summary>
    /// The game's program relative to dir: the catalog's exe name if present (at the top or further down),
    /// otherwise the only .exe at the top.
    /// </summary>
    public static string FindExe(string dir, string? want)
    {
        if (!Directory.Exists(dir)) return "";
        if (!string.IsNullOrEmpty(want))
        {
            if (File.Exists(Path.Combine(dir, want))) return want;
            try
            {
                var found = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(p => string.Equals(Path.GetFileName(p), want, StringComparison.OrdinalIgnoreCase));
                if (found != null) return Path.GetRelativePath(dir, found);
            }
            catch { /* unreadable folder */ }
        }
        var exes = Directory.GetFiles(dir, "*.exe").Select(Path.GetFileName)
            .Where(n => n != null && !n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return exes.Count == 1 ? exes[0]! : "";
    }

    /// <summary>Deletes older copies of a game after an update.</summary>
    public static void RemoveOtherVersions(CatalogGame g, string keep)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(GameBase(g)))
            {
                var name = Path.GetFileName(d);
                if (name != keep && name != "app") TryDeleteDir(d);
            }
        }
        catch { /* ignore */ }
    }

    private static readonly Regex Uninst = new(@"^(unins\d+|uninst.*|uninstall.*)\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Uninstalls a game: runs its own uninstaller for setup-program games, then deletes its folder.
    /// Saves live in the player's profile and are kept.
    /// </summary>
    public static async Task RemoveInstallAsync(CatalogGame g, InstallRecord? rec)
    {
        if (rec is { Kind: "nsis" or "inno" } && Directory.Exists(rec.Dir))
        {
            var un = Directory.GetFiles(rec.Dir, "*.exe").FirstOrDefault(p => Uninst.IsMatch(Path.GetFileName(p)));
            if (un != null)
            {
                var args = rec.Kind == "inno" ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" : "/S _?=" + rec.Dir;
                try { await RunAndWaitAsync(un, args, rec.Dir); } catch (Exception ex) { Log.Error($"uninstaller for {g.Id}", ex); }
            }
        }
        Exception? last = null;
        for (var i = 0; i < 10; i++) // an uninstaller may still be letting go of files
        {
            try
            {
                if (Directory.Exists(GameBase(g))) Directory.Delete(GameBase(g), true);
                return;
            }
            catch (Exception ex) { last = ex; await Task.Delay(500); }
        }
        if (last != null) throw last;
    }

    /// <summary>Finds a game unpacked by launcher 1.x (Games\&lt;Bundle&gt;\&lt;version&gt;).</summary>
    public static InstallRecord? FindLegacy(CatalogGame g)
    {
        if (string.IsNullOrEmpty(g.Bundle) || !Directory.Exists(GameBase(g))) return null;
        InstallRecord? best = null;
        foreach (var d in Directory.GetDirectories(GameBase(g)))
        {
            var name = Path.GetFileName(d);
            if (name.EndsWith(".partial", StringComparison.Ordinal) || name == "app") continue;
            var exe = FindExe(d, g.Exe);
            if (exe == "") continue;
            if (best == null || GitHubService.Newer(name, best.Version))
                best = new InstallRecord
                {
                    Version = name, Dir = d, Kind = "zip", Exe = exe,
                    InstalledAt = new DateTimeOffset(Directory.GetLastWriteTimeUtc(d)).ToUnixTimeSeconds(),
                };
        }
        return best;
    }

    /// <summary>
    /// Runs a setup program or uninstaller through the shell (so one that needs administrator rights shows
    /// the usual Windows prompt) and waits for it to finish.
    /// </summary>
    public static async Task<int> RunAndWaitAsync(string file, string args, string dir)
    {
        Process? p;
        try
        {
            p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = dir });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("cancelled at the Windows permission prompt");
        }
        if (p == null) return 0;
        using (p)
        {
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
    }

    public static long DirSize(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ } }
    public static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { /* ignore */ } }
}
