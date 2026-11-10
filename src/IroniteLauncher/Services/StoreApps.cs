using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ironite.Launcher.Models;

namespace Ironite.Launcher.Services;

/// <summary>
/// How to recognise one of the player's games while it runs: by package (the reliable way for Store / Xbox
/// games), by install folder, or by program name - or, for games added by their .exe, the exact program.
/// </summary>
public sealed class GameMatch
{
    public string Family = "";
    public string Dir = "";
    public HashSet<string> Exes = new();
    public string Path = "";

    public static GameMatch ForPath(string path) => new() { Path = path };

    private static readonly Regex ExeAttr = new(@"(?:<Application\b[^>]*\bExecutable|<Executable\b[^>]*\bName)\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static GameMatch ForStore(string appId, string dir)
    {
        var m = new GameMatch { Dir = dir ?? "", Family = appId.Split('!')[0] };
        foreach (var f in new[] { "AppxManifest.xml", "MicrosoftGame.config" })
        {
            try
            {
                var p = System.IO.Path.Combine(m.Dir, f);
                if (!File.Exists(p)) continue;
                foreach (Match x in ExeAttr.Matches(File.ReadAllText(p)))
                {
                    var name = System.IO.Path.GetFileName(x.Groups[1].Value.Replace('\\', '/')).ToLowerInvariant();
                    if (name.EndsWith(".exe") && name != "gamelaunchhelper.exe") m.Exes.Add(name);
                }
            }
            catch { /* unreadable manifest */ }
        }
        return m;
    }

    public static GameMatch For(CustomGame cg) => cg.IsStore ? ForStore(cg.AppId!, cg.Location ?? "") : ForPath(cg.Path ?? "");

    /// <summary>How many running programs belong to the game, and one of their names (for the log).</summary>
    public (int Count, string What) Matches(IEnumerable<ProcInfo> procs)
    {
        var dir = Dir.Length > 0 ? System.IO.Path.GetFullPath(Dir).TrimEnd('\\').ToLowerInvariant() + "\\" : "";
        var fam = Family.ToLowerInvariant();
        var path = Path.Length > 0 ? System.IO.Path.GetFullPath(Path).ToLowerInvariant() : "";
        int n = 0;
        var what = "";
        foreach (var p in procs)
        {
            if (path.Length > 0)
            {
                if (p.Path == path) { n++; what = p.Name; }
                continue;
            }
            if (fam.Length > 0 && p.Family == fam) { n++; what = p.Name + " (package)"; }
            else if (dir.Length > 0 && p.Path.StartsWith(dir, StringComparison.Ordinal)) { n++; what = p.Name + " (folder)"; }
            else if (Exes.Contains(p.Name)) { n++; what = p.Name; }
        }
        return (n, what);
    }
}

/// <summary>Microsoft Store / Xbox apps installed on this PC.</summary>
public static class StoreApps
{
    private const string Script = @"$ErrorActionPreference = 'SilentlyContinue'
$pk = @{}
foreach ($p in Get-AppxPackage) { if ($p.InstallLocation) { $pk[$p.PackageFamilyName] = $p.InstallLocation } }
$out = @()
foreach ($a in Get-StartApps) {
  $id = $a.AppID
  if ($id -notlike '*!*') { continue }
  $loc = $pk[$id.Split('!')[0]]
  $game = $false
  if ($loc) { $game = Test-Path -LiteralPath (Join-Path $loc 'MicrosoftGame.config') }
  $out += [pscustomobject]@{ name = $a.Name; appId = $id; location = [string]$loc; game = $game }
}
ConvertTo-Json -InputObject @($out) -Compress";

    private static List<StoreApp>? _cache;
    private static DateTime _cachedAt;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The Store / Xbox apps in the Start menu, games first.</summary>
    public static async Task<List<StoreApp>> ListAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_cache != null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1))
                return _cache.Select(Clone).ToList();
            var (_, output) = await Platform.RunPowerShellAsync(Script, TimeSpan.FromMinutes(1));
            List<StoreApp> apps;
            try { apps = JsonSerializer.Deserialize<List<StoreApp>>(output, JsonStore.Options) ?? new(); }
            catch (Exception ex) { throw new InvalidDataException($"couldn't read the list of apps ({ex.Message})"); }
            apps = apps.OrderByDescending(a => a.Game).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _cache = apps;
            _cachedAt = DateTime.UtcNow;
            return apps.Select(Clone).ToList();
        }
        finally { Gate.Release(); }
    }

    private static StoreApp Clone(StoreApp a) => new() { Name = a.Name, AppId = a.AppId, Location = a.Location, Game = a.Game };

    private static readonly Regex LogoAttr = new(@"\b(Square480x480Logo|Square310x310Logo|Square150x150Logo|Square44x44Logo|StoreLogo)\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LogoElem = new(@"<Logo>([^<]+)</Logo>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The best square logo a Store / Xbox game ships with (MicrosoftGame.config first, then AppxManifest.xml).</summary>
    public static byte[]? Art(string? location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["square480x480logo"] = 5, ["square310x310logo"] = 4, ["square150x150logo"] = 3, ["storelogo"] = 2, ["square44x44logo"] = 1 };
        var cands = new List<(string Path, int Rank)>();
        foreach (var f in new[] { "MicrosoftGame.config", "AppxManifest.xml" })
        {
            try
            {
                var p = Path.Combine(location, f);
                if (!File.Exists(p)) continue;
                var text = File.ReadAllText(p);
                foreach (Match m in LogoAttr.Matches(text)) cands.Add((m.Groups[2].Value, rank[m.Groups[1].Value]));
                foreach (Match m in LogoElem.Matches(text)) cands.Add((m.Groups[1].Value, 2));
            }
            catch { /* unreadable */ }
        }
        string? best = null;
        int bestRank = -1;
        long bestSize = 0;
        foreach (var c in cands)
        {
            var p = ResolveScaledAsset(location, c.Path);
            if (p == null) continue;
            var size = new FileInfo(p).Length;
            if (c.Rank > bestRank || (c.Rank == bestRank && size > bestSize)) { best = p; bestRank = c.Rank; bestSize = size; }
        }
        try { return best == null ? null : File.ReadAllBytes(best); } catch { return null; }
    }

    /// <summary>Finds "Assets\Logo.png" as shipped: the file itself or its largest ".scale-200" / ".targetsize-256" variant.</summary>
    private static string? ResolveScaledAsset(string root, string rel)
    {
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', '\\')));
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            if (File.Exists(full)) return full;
            var dir = Path.GetDirectoryName(full)!;
            if (!Directory.Exists(dir)) return null;
            var ext = Path.GetExtension(full);
            var stem = Path.GetFileNameWithoutExtension(full) + ".";
            return Directory.GetFiles(dir)
                .Where(p =>
                {
                    var n = Path.GetFileName(p).ToLowerInvariant();
                    return n.StartsWith(stem.ToLowerInvariant()) && string.Equals(Path.GetExtension(p), ext, StringComparison.OrdinalIgnoreCase)
                           && !n.Contains("contrast") && !n.Contains("altform-lightunplated");
                })
                .OrderByDescending(p => new FileInfo(p).Length)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
