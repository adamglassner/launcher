using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ironite.Launcher.Models;

namespace Ironite.Launcher.Services;

public sealed class RateLimitedException : Exception
{
    public RateLimitedException() : base("GitHub is busy right now; the launcher will check again in a few minutes") { }
}

/// <summary>
/// Reads game and launcher releases from GitHub, like a store front: every game lives in its own
/// repository and each GitHub Release is a version of it. Requests are unauthenticated (60 an hour per PC),
/// so the latest tag is read from github.com first (no API allowance used) and the API is only called for a
/// release that hasn't been seen. Responses are cached on disk (releases.json) with their ETag, which also
/// keeps the library working offline.
/// </summary>
public sealed class GitHubService
{
    public const string LauncherRepo = "adamglassner/launcher";
    public const string CatalogUrl = "https://raw.githubusercontent.com/" + LauncherRepo + "/main/catalog/games.json";
    public const string CoverUrl = "https://raw.githubusercontent.com/" + LauncherRepo + "/main/covers/";

    public static string UserAgent => "IroniteLauncher/" + App.VersionText;

    private static string ApiBase => (Environment.GetEnvironmentVariable("IRONITE_GITHUB_API") is { Length: > 0 } v ? v : "https://api.github.com").TrimEnd('/');
    private static string WebBase => (Environment.GetEnvironmentVariable("IRONITE_GITHUB_WEB") is { Length: > 0 } v ? v : "https://github.com").TrimEnd('/');

    private static readonly HttpClient Http = MakeClient(true, TimeSpan.FromSeconds(30));
    private static readonly HttpClient NoRedirect = MakeClient(false, TimeSpan.FromSeconds(20));

    private static HttpClient MakeClient(bool redirects, TimeSpan timeout)
    {
        var c = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = redirects, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return c;
    }

    private readonly object _gate = new();
    private DateTime _apiPausedUntil;
    public ReleaseCache Cache { get; private set; } = new();

    public void Load()
    {
        Cache = JsonStore.Load(AppPaths.ReleaseCache, () => new ReleaseCache());
        Cache.Repos ??= new();
        // A game list saved by an older launcher can be older than the one built into this one.
        if (Cache.CatalogVersion != App.VersionText) { Cache.Catalog = null; Cache.CatalogEtag = null; }
    }

    private void Save() // caller holds _gate
    {
        try { JsonStore.Save(AppPaths.ReleaseCache, Cache); } catch (Exception ex) { Log.Error("save releases.json", ex); }
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    public GhRelease? Get(string? repo)
    {
        if (string.IsNullOrEmpty(repo)) return null;
        lock (_gate) return Cache.Repos.TryGetValue(repo.ToLowerInvariant(), out var e) ? e.Release : null;
    }

    /// <summary>
    /// Asks github.com which tag a repo's latest release has: /&lt;repo&gt;/releases/latest redirects to
    /// /&lt;repo&gt;/releases/tag/&lt;tag&gt;. Returns (null, true) when the repo has no published release.
    /// </summary>
    public static async Task<(string? Tag, bool Answered)> LatestTagAsync(string repo)
    {
        var url = $"{WebBase}/{repo}/releases/latest";
        for (var hop = 0; hop < 3; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await NoRedirect.SendAsync(req);
            var code = (int)resp.StatusCode;
            var loc = resp.Headers.Location?.OriginalString ?? "";
            if (code is >= 300 and < 400 && loc.Length > 0)
            {
                var i = loc.IndexOf("/releases/tag/", StringComparison.Ordinal);
                if (i >= 0)
                {
                    var t = loc[(i + "/releases/tag/".Length)..];
                    var j = t.IndexOfAny(new[] { '?', '#' });
                    if (j >= 0) t = t[..j];
                    return (Uri.UnescapeDataString(t), true);
                }
                if (loc.EndsWith("/releases/latest", StringComparison.Ordinal)) // the repo was renamed
                {
                    url = loc.StartsWith('/') ? WebBase + loc : loc;
                    continue;
                }
                return (null, true); // redirected to the empty releases page
            }
            if (resp.StatusCode == HttpStatusCode.NotFound) return (null, true);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitedException();
            throw new HttpRequestException($"{repo}: github.com answered {code} {resp.ReasonPhrase}");
        }
        throw new HttpRequestException($"{repo}: too many redirects");
    }

    /// <summary>Brings a repo's cached latest release up to date.</summary>
    public async Task FetchLatestAsync(string repo)
    {
        var key = repo.ToLowerInvariant();
        string? tag = null;
        Exception? tagError = null;
        try { (tag, _) = await LatestTagAsync(repo); }
        catch (Exception ex) { tagError = ex; }

        CachedRelease? prev;
        string? etag;
        lock (_gate)
        {
            Cache.Repos.TryGetValue(key, out prev);
            if (tagError == null)
            {
                if (tag == null)
                {
                    Cache.Repos[key] = new CachedRelease { Checked = Now() }; // "coming soon"
                    Save();
                    return;
                }
                if (prev?.Release != null && prev.Release.Tag == tag)
                {
                    prev.Checked = Now(); // nothing new
                    Save();
                    return;
                }
            }
            if (DateTime.UtcNow < _apiPausedUntil) throw new RateLimitedException();
            etag = prev?.Etag;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(etag)) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        // CI runs the tests with its own token (the shared runners use up the unauthenticated allowance).
        if (Environment.GetEnvironmentVariable("IRONITE_GITHUB_TOKEN") is { Length: > 0 } token)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        lock (_gate)
        {
            switch (resp.StatusCode)
            {
                case HttpStatusCode.NotModified when prev != null:
                    prev.Checked = Now();
                    break;
                case HttpStatusCode.OK:
                    var r = JsonSerializer.Deserialize<GhRelease>(body, JsonStore.Options)
                            ?? throw new InvalidDataException($"{repo}: empty release");
                    Cache.Repos[key] = r.Draft || r.Prerelease
                        ? new CachedRelease { Checked = Now() }
                        : new CachedRelease { Etag = resp.Headers.ETag?.ToString(), Release = r, Checked = Now() };
                    break;
                case HttpStatusCode.NotFound:
                    Cache.Repos[key] = new CachedRelease { Checked = Now() }; // no release yet: "coming soon"
                    break;
                case HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests:
                    // Out of API requests for now: wait until GitHub says the allowance resets.
                    var until = DateTime.UtcNow.AddMinutes(10);
                    if (resp.Headers.TryGetValues("X-RateLimit-Reset", out var vals) && long.TryParse(vals.FirstOrDefault(), out var reset) && reset > 0)
                        until = DateTimeOffset.FromUnixTimeSeconds(reset).UtcDateTime.AddSeconds(5);
                    _apiPausedUntil = until;
                    Log.Info($"GitHub API allowance used up; pausing API calls until {until.ToLocalTime():HH:mm:ss}");
                    throw new RateLimitedException();
                default:
                    throw new HttpRequestException($"{repo}: GitHub answered {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }
            Save();
        }
    }

    /// <summary>Downloads the published game catalog (only catalogs written for this launcher generation).</summary>
    public async Task<bool> FetchCatalogAsync()
    {
        var url = Environment.GetEnvironmentVariable("IRONITE_CATALOG_URL") is { Length: > 0 } v ? v : CatalogUrl;
        if (url == "off") return false;
        try
        {
            string? etag;
            lock (_gate) etag = Cache.CatalogEtag;
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(etag)) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode != HttpStatusCode.OK) return false;
            var text = await resp.Content.ReadAsStringAsync();
            var probe = JsonSerializer.Deserialize<Catalog>(text, JsonStore.Options);
            if (probe == null || probe.Schema < 2 || probe.Games.Count == 0) return false;
            using var doc = JsonDocument.Parse(text);
            lock (_gate)
            {
                Cache.Catalog = doc.RootElement.Clone();
                Cache.CatalogEtag = resp.Headers.ETag?.ToString();
                Cache.CatalogVersion = App.VersionText;
                Save();
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"published catalog: {ex.Message}");
            return false;
        }
    }

    public string? CachedCatalogJson()
    {
        lock (_gate) return Cache.Catalog is { ValueKind: JsonValueKind.Object } c ? c.GetRawText() : null;
    }

    private static readonly Regex Num = new(@"\d+", RegexOptions.Compiled);

    /// <summary>Whether version a is newer than b ("1.10.0" &gt; "1.9.2").</summary>
    public static bool Newer(string? a, string? b)
    {
        var pa = Num.Matches(a ?? "").Select(m => long.TryParse(m.Value, out var x) ? x : 0).ToArray();
        var pb = Num.Matches(b ?? "").Select(m => long.TryParse(m.Value, out var x) ? x : 0).ToArray();
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
