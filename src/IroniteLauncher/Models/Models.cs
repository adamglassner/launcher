using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ironite.Launcher.Models;

// ---------------------------------------------------------------------------
// The game catalog (catalog/games.json in the GitHub repo, schema 2).
// ---------------------------------------------------------------------------

public sealed class Catalog
{
    public int Schema { get; set; }
    public List<CatalogCategory> Categories { get; set; } = new();
    public List<CatalogGame> Games { get; set; } = new();
}

public sealed class CatalogCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Blurb { get; set; }
    public string? Icon { get; set; }
}

public sealed class CatalogGame
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }            // "coming_soon" for games without a repo yet
    public bool Demo { get; set; }
    public List<string>? Platforms { get; set; }
    public string? Repo { get; set; }              // GitHub owner/name the game installs from
    public string? Bundle { get; set; }            // folder under Games\
    public string? Exe { get; set; }
    public string? Type { get; set; }              // "web" = plays online; "local" = the player's own game
    public string? Url { get; set; }
    public string? Players { get; set; }
    public string? Description { get; set; }

    // Featured banner (optional; the launcher also has built-in defaults).
    public string? Kicker { get; set; }
    public string? HeroTitle { get; set; }
    public string? HeroSmall { get; set; }

    [JsonIgnore] public bool IsWeb => Type == "web";
    [JsonIgnore] public bool IsLocal => Type == "local";
    [JsonIgnore] public bool IsPlaceholder => Subtitle == "New category";

    // Filled in for the player's own games (not part of the catalog file).
    [JsonIgnore] public CustomGame? Custom { get; set; }
    /// <summary>The category name shown on Non-Ironite Games for the player's own games.</summary>
    [JsonIgnore] public string? Group { get; set; }
    [JsonIgnore] public bool IsStore => Custom?.IsStore == true;
}

// ---------------------------------------------------------------------------
// settings.json in %APPDATA%\Ironite\Launcher (same format as launcher 2.x,
// so play stats, installs and your own games carry over).
// ---------------------------------------------------------------------------

public sealed class Settings
{
    public Dictionary<string, GameStats> Stats { get; set; } = new();
    public Dictionary<string, InstallRecord>? Installs { get; set; } = new();
    public bool? AutoUpdate { get; set; }
    public List<string>? Favorites { get; set; }
    public List<string>? Hidden { get; set; }
    public List<CustomGame>? Custom { get; set; }
    public string? SteamProfile { get; set; }
    public string? XblKey { get; set; }
    public string? Sort { get; set; }

    /// <summary>Anything a newer or older launcher wrote that this one doesn't know about is kept.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class GameStats
{
    public int PlayCount { get; set; }
    public long PlaySeconds { get; set; }
    public long LastPlayed { get; set; }
    public long InstalledAt { get; set; }
    public List<PlaySession>? Sessions { get; set; }
}

public sealed class PlaySession
{
    public long Start { get; set; }
    public long Secs { get; set; }
}

public sealed class InstallRecord
{
    public string Version { get; set; } = "";
    public string Dir { get; set; } = "";
    public string Kind { get; set; } = "zip";   // "zip", "nsis", "inno" or "legacy"
    public string Exe { get; set; } = "";
    public long InstalledAt { get; set; }
}

public sealed class CustomGame
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Path { get; set; }
    public string? AppId { get; set; }
    public string? Location { get; set; }
    public long AddedAt { get; set; }
    public string? Category { get; set; }
    public string? Group { get; set; }
    public string? Developer { get; set; }
    public string? Players { get; set; }
    public string? Description { get; set; }
    public string? Art { get; set; }
    public string? AchSource { get; set; }
    public string? SteamAppId { get; set; }
    public string? XboxTitleId { get; set; }

    [JsonIgnore] public bool IsStore => !string.IsNullOrEmpty(AppId);
}

// ---------------------------------------------------------------------------
// releases.json in %LOCALAPPDATA%\Ironite\Launcher (cached GitHub releases).
// ---------------------------------------------------------------------------

public sealed class ReleaseCache
{
    public Dictionary<string, CachedRelease> Repos { get; set; } = new();
    public JsonElement? Catalog { get; set; }
    public string? CatalogEtag { get; set; }
    [JsonPropertyName("catalogLauncherVersion")] public string? CatalogVersion { get; set; }
}

public sealed class CachedRelease
{
    public string? Etag { get; set; }
    public GhRelease? Release { get; set; }
    /// <summary>When the repo was last checked (RFC 3339; kept as text because launcher 2.x wrote nanoseconds).</summary>
    public string? Checked { get; set; }
}

public sealed class GhRelease
{
    [JsonPropertyName("tag_name")] public string Tag { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("published_at")] public DateTime? PublishedAt { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = new();

    [JsonIgnore] public string Version => Tag.TrimStart('v', 'V');

    /// <summary>The file the launcher installs: a Windows zip if there is one, otherwise a setup program.</summary>
    public GhAsset? GameAsset()
    {
        GhAsset? Pick(Func<string, bool> ok) => Assets.FirstOrDefault(a => ok(a.Name.ToLowerInvariant()));
        return Pick(n => n.EndsWith("-windows.zip"))
            ?? Pick(n => n.EndsWith(".zip") && !n.Contains("source"))
            ?? Pick(n => n.EndsWith(".exe") && n.Contains("setup"))
            ?? Pick(n => n.EndsWith(".exe"));
    }

    /// <summary>The launcher's own setup program.</summary>
    /// <remarks>Named IroniteLauncher-Setup-&lt;version&gt;.exe, which launcher 2.x also looks for.</remarks>
    public GhAsset? LauncherAsset() =>
        Assets.FirstOrDefault(a => a.Name.StartsWith("IroniteLauncher-Setup", StringComparison.OrdinalIgnoreCase)
                                   && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
}

public sealed class GhAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

// ---------------------------------------------------------------------------
// Live state of a game, worked out from all of the above (shown in the UI).
// ---------------------------------------------------------------------------

public sealed class GameState
{
    public bool Available, Installed, Update, Installing, Updating, Running, Favorite, Hidden;
    public string? InstalledVersion, LatestVersion, Phase, Notes, ReleaseName, ReleaseUrl, Error, LaunchError;
    public int Progress, QueuePos, QueueLen;
    public long Size, DiskSize, PublishedAt, InstalledAt, SessionStart, PlaySeconds, LastPlayed;
    public int PlayCount;

    public string Signature() => string.Join("|", Available, Installed, Update, Installing, Updating, Running, Favorite, Hidden,
        InstalledVersion, LatestVersion, Phase, Error, LaunchError, Progress, QueuePos, QueueLen, DiskSize, PlaySeconds / 60, PlayCount, LastPlayed);
}

public sealed class StatsRow
{
    public string Id = "";
    public long[] Weeks = Array.Empty<long>();
    public long Total;
    public int Sessions;
}

public sealed class LauncherUpdateInfo
{
    public string Version = "", Notes = "", Url = "";
    public bool Downloading, Installing;
    public long Done, Total;
    public int Progress;
    public string? Error;
    public string Signature() => $"{Version}|{Downloading}|{Installing}|{Progress}|{Error}";
}

public sealed class StoreApp
{
    public string Name { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Location { get; set; } = "";
    public bool Game { get; set; }
    [JsonIgnore] public bool Added { get; set; }
}

public sealed class Achievement
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public bool Unlocked { get; set; }
    public long UnlockedAt { get; set; }
    public int Score { get; set; }
}

public sealed class AchievementResult
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public int Total, Unlocked, Score, ScoreTotal;
    public List<Achievement> Items { get; set; } = new();
    public string? Error { get; set; }
    public DateTime FetchedAt { get; set; }
}
