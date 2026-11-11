using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ironite.Launcher.Models;

namespace Ironite.Launcher.Services;

/// <summary>
/// Achievements in one place.
///   - Ironite games: each game writes its own file to %APPDATA%\Ironite\Achievements\&lt;game id&gt;.json, with its
///     icons in a folder of the same name beside it (see <see cref="Ironite"/>). Nothing to set up.
/// And from other launchers, for the player's own (non-Ironite) games:
///   - Steam: read from the player's public Steam Community profile (no key needed).
///     The profile's "Game details" must be public.
///   - Xbox: read through OpenXBL (xbl.io), with the player's own free API key, since Xbox Live needs a
///     signed-in account.
/// Results are cached for 10 minutes so the Achievements page opens quickly.
/// </summary>
public static class AchievementService
{
    private static readonly HttpClient Http = MakeClient();
    private static readonly Dictionary<string, AchievementResult> Cache = new();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubService.UserAgent);
        return c;
    }

    private static string SteamBase => (Environment.GetEnvironmentVariable("IRONITE_STEAM_BASE") is { Length: > 0 } v ? v : "https://steamcommunity.com").TrimEnd('/');
    private static string XblBase => (Environment.GetEnvironmentVariable("IRONITE_XBL_BASE") is { Length: > 0 } v ? v : "https://xbl.io/api/v2").TrimEnd('/');

    public static void ClearCache() { lock (Cache) Cache.Clear(); }

    // ---------- Ironite games ----------

    /// <summary>Where Ironite games share their achievements (IRONITE_ACHIEVEMENTS_DIR overrides it, for tests).</summary>
    public static string IroniteDir { get; set; } = Environment.GetEnvironmentVariable("IRONITE_ACHIEVEMENTS_DIR") is { Length: > 0 } d
        ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ironite", "Achievements");

    public static bool HasIronite(string gameId) => File.Exists(Path.Combine(IroniteDir, gameId + ".json"));

    /// <summary>
    /// An Ironite game's achievements, read from the file the game keeps up to date:
    /// <code>
    /// { "schema": 1, "game": "solarsiege", "title": "Solar Siege", "updated": 1762790000, "icon_hidden": "solarsiege/hidden.png",
    ///   "achievements": [ { "id": "sunrise", "name": "Sunrise", "description": "...", "category": "Campaign", "hidden": false,
    ///                       "unlocked": true, "unlocked_at": 1762790000, "progress": 1, "target": 1,
    ///                       "icon": "solarsiege/sunrise.png", "icon_locked": "solarsiege/sunrise_locked.png" } ] }
    /// </code>
    /// Icon paths are relative to the file. Returns null when the game hasn't written one (not played yet).
    /// </summary>
    public static AchievementResult? Ironite(string gameId)
    {
        var path = Path.Combine(IroniteDir, gameId + ".json");
        if (!File.Exists(path)) return null;
        var r = new AchievementResult { Id = gameId, Source = "ironite", FetchedAt = DateTime.UtcNow };
        try
        {
            string body;
            // the game may be writing it right now
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var rd = new StreamReader(fs)) body = rd.ReadToEnd();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? Icon(string rel)
            {
                if (string.IsNullOrWhiteSpace(rel)) return null;
                var full = Path.GetFullPath(Path.Combine(IroniteDir, rel.Replace('/', Path.DirectorySeparatorChar)));
                return full.StartsWith(Path.GetFullPath(IroniteDir), StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? new Uri(full).AbsoluteUri : null;
            }
            var hiddenIcon = Icon(Str(root, "icon_hidden"));
            if (root.TryGetProperty("achievements", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var a in list.EnumerateArray())
                {
                    var unlocked = a.TryGetProperty("unlocked", out var u) && u.ValueKind == JsonValueKind.True;
                    var hidden = a.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True;
                    long.TryParse(Str(a, "unlocked_at"), out var at);
                    int.TryParse(Str(a, "progress"), out var prog);
                    int.TryParse(Str(a, "target"), out var target);
                    var it = new Achievement
                    {
                        Name = Str(a, "name"), Description = Str(a, "description"), Category = Str(a, "category"),
                        Unlocked = unlocked, UnlockedAt = at, Hidden = hidden, Progress = Math.Max(0, prog), Target = Math.Max(1, target),
                        Icon = unlocked ? Icon(Str(a, "icon")) : Icon(Str(a, "icon_locked")) ?? Icon(Str(a, "icon")),
                    };
                    if (hidden && !unlocked)
                    {
                        // secret ones stay secret here too, as they do in the game
                        it.Name = "Secret achievement";
                        it.Description = "Keep playing to discover this one.";
                        it.Icon = hiddenIcon;
                        it.Progress = 0;
                        it.Target = 1;
                    }
                    if (it.Name.Length > 0) r.Items.Add(it);
                }
            if (r.Items.Count == 0) r.Error = "this game hasn't listed any achievements yet";
        }
        catch (Exception ex)
        {
            Log.Info($"ironite achievements {gameId}: {ex.Message}");
            r.Error = "couldn't read this game's achievements; play it again to refresh them";
        }
        return Finish(r);
    }

    private static AchievementResult Finish(AchievementResult r)
    {
        r.Total = r.Unlocked = r.Score = r.ScoreTotal = 0;
        foreach (var it in r.Items)
        {
            r.Total++;
            r.ScoreTotal += it.Score;
            if (it.Unlocked) { r.Unlocked++; r.Score += it.Score; }
        }
        return r;
    }

    // ---------- Steam ----------

    private static readonly Regex SteamProfileRe = new(@"(?:steamcommunity\.com/)?(profiles|id)/([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);

    /// <summary>Turns a profile link, SteamID64 or custom URL name into "profiles/&lt;id&gt;" or "id/&lt;name&gt;".</summary>
    public static string SteamProfilePath(string? s)
    {
        s = (s ?? "").Trim();
        if (s == "") return "";
        var m = SteamProfileRe.Match(s);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant() + "/" + m.Groups[2].Value;
        if (s.Length == 17 && ulong.TryParse(s, out _)) return "profiles/" + s;
        if (Regex.IsMatch(s, @"^[A-Za-z0-9_\-]{2,32}$")) return "id/" + s;
        return "";
    }

    private static string? SteamInstallDir()
    {
        foreach (var d in new[]
                 {
                     Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", "Steam"),
                     Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "", "Steam"),
                     @"C:\Program Files (x86)\Steam",
                 })
            if (File.Exists(Path.Combine(d, "config", "loginusers.vdf"))) return d;
        return null;
    }

    /// <summary>The account signed in to Steam on this PC.</summary>
    public static (string Id, string Name) DetectSteamUser()
    {
        var dir = SteamInstallDir();
        if (dir == null) return ("", "");
        try
        {
            var text = File.ReadAllText(Path.Combine(dir, "config", "loginusers.vdf"));
            string id = "", name = "";
            foreach (Match m in Regex.Matches(text, @"""(\d{17})""\s*\{([^}]*)\}"))
            {
                var fields = Regex.Matches(m.Groups[2].Value, @"""(\w+)""\s*""([^""]*)""")
                    .GroupBy(f => f.Groups[1].Value.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
                if (id == "" || fields.GetValueOrDefault("mostrecent") == "1")
                {
                    id = m.Groups[1].Value;
                    name = fields.GetValueOrDefault("personaname") ?? "";
                }
            }
            return (id, name);
        }
        catch { return ("", ""); }
    }

    /// <summary>The Steam app id of a game installed by Steam: steam_appid.txt next to it, or the library's appmanifest.</summary>
    public static (string AppId, string Name) DetectSteamApp(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return ("", "");
        string appId = "", name = "";
        try
        {
            var txt = Path.Combine(Path.GetDirectoryName(exePath)!, "steam_appid.txt");
            if (File.Exists(txt) && Regex.IsMatch(File.ReadAllText(txt).Trim(), @"^\d+$")) appId = File.ReadAllText(txt).Trim();
            var parts = Path.GetFullPath(exePath).Split('\\', '/');
            for (var i = 0; i + 2 < parts.Length; i++)
            {
                if (!parts[i].Equals("steamapps", StringComparison.OrdinalIgnoreCase) || !parts[i + 1].Equals("common", StringComparison.OrdinalIgnoreCase)) continue;
                var lib = string.Join("\\", parts.Take(i + 1));
                var gameDir = parts[i + 2];
                foreach (var f in Directory.GetFiles(lib, "appmanifest_*.acf"))
                {
                    var b = File.ReadAllText(f);
                    var d = Regex.Match(b, @"""installdir""\s*""([^""]+)""");
                    if (!d.Success || !d.Groups[1].Value.Equals(gameDir, StringComparison.OrdinalIgnoreCase)) continue;
                    var a = Regex.Match(b, @"""appid""\s*""(\d+)""");
                    if (a.Success) appId = a.Groups[1].Value;
                    var n = Regex.Match(b, @"""name""\s*""([^""]+)""");
                    if (n.Success) name = n.Groups[1].Value;
                }
            }
        }
        catch { /* not a Steam game */ }
        return (appId, name);
    }

    private static async Task<AchievementResult> SteamAsync(string profile, string? appId)
    {
        if (profile == "") throw new InvalidOperationException("add your Steam profile in Settings");
        if (string.IsNullOrEmpty(appId)) throw new InvalidOperationException("set the game's Steam app ID in Edit details");
        HttpResponseMessage resp;
        try { resp = await Http.GetAsync($"{SteamBase}/{profile}/stats/{Uri.EscapeDataString(appId)}/achievements/?xml=1&l=english"); }
        catch { throw new InvalidOperationException("couldn't reach Steam"); }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"Steam answered {(int)resp.StatusCode} {resp.ReasonPhrase}");
            XElement root;
            try { root = XElement.Parse(body); }
            catch
            {
                if (body.Contains("private", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("your Steam profile's game details are private; set Game details to Public in Steam's privacy settings");
                throw new InvalidOperationException("Steam didn't return achievements for this game (check the app ID)");
            }
            var err = root.Element("error")?.Value;
            if (!string.IsNullOrWhiteSpace(err)) throw new InvalidOperationException("Steam: " + err.Trim());
            var privacy = root.Element("privacyState")?.Value ?? "";
            if (privacy.Equals("private", StringComparison.OrdinalIgnoreCase) || privacy.Equals("friendsonly", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("your Steam profile's game details aren't public; set Game details to Public in Steam's privacy settings");
            var r = new AchievementResult { Source = "steam" };
            foreach (var a in root.Element("achievements")?.Elements("achievement") ?? Enumerable.Empty<XElement>())
            {
                var unlocked = (string?)a.Attribute("closed") == "1";
                long.TryParse(a.Element("unlockTimestamp")?.Value, out var at);
                var it = new Achievement
                {
                    Name = (a.Element("name")?.Value ?? "").Trim(),
                    Description = a.Element("description")?.Value?.Trim(),
                    Unlocked = unlocked,
                    UnlockedAt = at,
                    Icon = a.Element("iconOpen")?.Value,
                };
                if (unlocked && a.Element("iconClosed")?.Value is { Length: > 0 } closed) it.Icon = closed; // Steam calls an achieved one "closed"
                r.Items.Add(it);
            }
            if (r.Items.Count == 0) throw new InvalidOperationException("this game has no Steam achievements (or the app ID is wrong)");
            return r;
        }
    }

    // ---------- Xbox (OpenXBL) ----------

    private static async Task<string> XblGetAsync(string? key, string path)
    {
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("add your OpenXBL key in Settings to see Xbox achievements");
        using var req = new HttpRequestMessage(HttpMethod.Get, XblBase + path);
        req.Headers.TryAddWithoutValidation("X-Authorization", key);
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.AcceptLanguage.ParseAdd("en-US");
        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch { throw new InvalidOperationException("couldn't reach OpenXBL"); }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => throw new InvalidOperationException("OpenXBL didn't accept your key; check it in Settings"),
                HttpStatusCode.TooManyRequests => throw new InvalidOperationException("OpenXBL's hourly limit was reached; try again later"),
                HttpStatusCode.OK => body,
                _ => throw new InvalidOperationException($"OpenXBL answered {(int)resp.StatusCode} {resp.ReasonPhrase}"),
            };
        }
    }

    private static string Norm(string s) =>
        string.Join(" ", Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N} ]+", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>An Xbox game's title id: from its MicrosoftGame.config, or by name in the player's Xbox title history.</summary>
    public static async Task<(string TitleId, string Name)> DetectXboxTitleAsync(string? location, string name, string? key)
    {
        try
        {
            var cfg = Path.Combine(location ?? "", "MicrosoftGame.config");
            if (!string.IsNullOrEmpty(location) && File.Exists(cfg))
            {
                var m = Regex.Match(File.ReadAllText(cfg), @"<TitleId>\s*([0-9A-F]{8})\s*</TitleId>", RegexOptions.IgnoreCase);
                if (m.Success && uint.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, null, out var v)) return (v.ToString(), "");
            }
        }
        catch { /* unreadable */ }
        if (string.IsNullOrEmpty(key)) return ("", "");
        try
        {
            using var doc = JsonDocument.Parse(await XblGetAsync(key, "/player/titleHistory"));
            var titles = doc.RootElement.TryGetProperty("titles", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => (Id: Str(x, "titleId"), Name: Str(x, "name"))).ToList()
                : new();
            var want = Norm(name);
            foreach (var x in titles) if (Norm(x.Name) == want) return (x.Id, x.Name);
            foreach (var x in titles)
            {
                var n = Norm(x.Name);
                if (want != "" && n != "" && (n.Contains(want) || want.Contains(n))) return (x.Id, x.Name);
            }
        }
        catch (Exception ex) { Log.Info($"xbox title history: {ex.Message}"); }
        return ("", "");
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";

    private static async Task<AchievementResult> XboxAsync(string? key, string? titleId)
    {
        if (string.IsNullOrEmpty(titleId)) throw new InvalidOperationException("set the game's Xbox title ID in Edit details");
        var body = await XblGetAsync(key, "/achievements/title/" + Uri.EscapeDataString(titleId));
        var r = new AchievementResult { Source = "xbox" };
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("achievements", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var a in list.EnumerateArray())
                {
                    var it = new Achievement
                    {
                        Name = Str(a, "name"),
                        Unlocked = Str(a, "progressState").Equals("Achieved", StringComparison.OrdinalIgnoreCase),
                        Description = Str(a, "description"),
                    };
                    if (!it.Unlocked && Str(a, "lockedDescription") is { Length: > 0 } ld) it.Description = ld;
                    if (a.TryGetProperty("mediaAssets", out var media) && media.ValueKind == JsonValueKind.Array && media.GetArrayLength() > 0)
                        it.Icon = Str(media[0], "url");
                    if (it.Unlocked && a.TryGetProperty("progression", out var prog) &&
                        DateTimeOffset.TryParse(Str(prog, "timeUnlocked"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) && when.Year > 2000)
                        it.UnlockedAt = when.ToUnixTimeSeconds();
                    if (a.TryGetProperty("rewards", out var rewards) && rewards.ValueKind == JsonValueKind.Array)
                        foreach (var rw in rewards.EnumerateArray())
                            if (Str(rw, "type").Equals("Gamerscore", StringComparison.OrdinalIgnoreCase) && int.TryParse(Str(rw, "value"), out var sc))
                                it.Score = sc;
                    r.Items.Add(it);
                }
        }
        catch (JsonException ex)
        {
            Log.Info($"xbox achievements {titleId}: {ex.Message}: {Clip(body)}");
            throw new InvalidOperationException("OpenXBL sent something the launcher couldn't read");
        }
        if (r.Items.Count == 0)
        {
            // Logged so a "none" answer can be looked into.
            Log.Info($"xbox achievements {titleId}: none in the reply: {Clip(body)}");
            throw new InvalidOperationException("Xbox has no achievements for this game on your account (check the title ID)");
        }
        return r;
    }

    private static string Clip(string s) => s.Length > 400 ? s[..400] : s;

    // ---------- putting it together ----------

    public static async Task<AchievementResult?> ForGameAsync(CustomGame cg, string? steamProfile, string? xblKey, bool force)
    {
        if (string.IsNullOrEmpty(cg.AchSource)) return null;
        var key = $"{cg.Id}|{cg.AchSource}|{cg.SteamAppId}|{cg.XboxTitleId}";
        lock (Cache)
            if (!force && Cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.FetchedAt < TimeSpan.FromMinutes(10)) return c;

        var profile = SteamProfilePath(steamProfile);
        if (profile == "" && DetectSteamUser().Id is { Length: > 0 } id) profile = "profiles/" + id;
        AchievementResult r;
        try
        {
            r = cg.AchSource switch
            {
                "steam" => await SteamAsync(profile, cg.SteamAppId),
                "xbox" => await XboxAsync(xblKey, cg.XboxTitleId),
                _ => throw new InvalidOperationException("unknown achievements source"),
            };
        }
        catch (Exception ex)
        {
            r = new AchievementResult { Source = cg.AchSource, Error = ex.Message };
            Log.Info($"achievements for {cg.Id}: {ex.Message}");
        }
        r.Id = cg.Id;
        r.FetchedAt = DateTime.UtcNow;
        Finish(r);
        // Unlocked first (newest first), then locked.
        r.Items = r.Items.OrderByDescending(i => i.Unlocked).ThenByDescending(i => i.UnlockedAt).ToList();
        lock (Cache) Cache[key] = r;
        return r;
    }
}
