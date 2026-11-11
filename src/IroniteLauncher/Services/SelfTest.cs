using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ironite.Launcher.Models;
using Ironite.Launcher.Views;

namespace Ironite.Launcher.Services;

/// <summary>
/// Headless checks run by CI (<c>IroniteLauncher.exe --self-test result.txt</c>). Exercises the real code paths:
/// reading launcher 2.x's settings and release cache, the built-in catalog and covers, version compare, zip
/// install, GitHub (latest tag, API, a real game download with checksum and install), running programs, icons.
/// Never touches the player's data folders. Exit code = number of failed checks.
/// </summary>
public static class SelfTest
{
    private const string GameRepo = "adamglassner/klondike";

    // A settings.json as launcher 2.16 (Go) writes it.
    private const string GoSettings = """
    {
      "stats": {
        "klondike": { "playCount": 12, "playSeconds": 5400, "lastPlayed": 1762700000, "installedAt": 1762000000,
                      "sessions": [ { "start": 1762600000, "secs": 1800 }, { "start": 1762700000, "secs": 600 } ] },
        "custom-0a1b2c3d4e5f": { "playCount": 2, "playSeconds": 120, "lastPlayed": 1762500000 }
      },
      "installs": {
        "klondike": { "version": "1.4.0", "dir": "C:\\Users\\x\\AppData\\Local\\Ironite\\Launcher\\Games\\Klondike\\1.4.0", "kind": "zip", "exe": "Klondike.exe", "installedAt": 1762000000 }
      },
      "autoUpdate": false,
      "favorites": [ "klondike", "hearts" ],
      "hidden": [ "lifeboat" ],
      "custom": [
        { "id": "custom-0a1b2c3d4e5f", "title": "Balatro", "appId": "PlayStack.Balatro_3wcqaesafpzfy!Game", "location": "C:\\XboxGames\\Balatro\\Content",
          "addedAt": 1762400000, "group": "Roguelikes", "developer": "LocalThunk", "achSource": "xbox", "xboxTitleId": "1234567890" },
        { "id": "custom-ffeeddccbbaa", "title": "Notepad", "path": "C:\\Windows\\notepad.exe", "addedAt": 1762400001, "category": "puzzle", "art": "custom-ffeeddccbbaa-art-1.png" }
      ],
      "steamProfile": "steamcommunity.com/id/someone",
      "xblKey": "abc123",
      "futureSetting": { "kept": true }
    }
    """;

    public static async Task<int> RunAsync(string outputFile)
    {
        var sb = new StringBuilder();
        var failed = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failed++;
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        }
        void Info(string name, bool ok, string detail = "") =>
            sb.AppendLine($"{(ok ? "PASS" : "INFO")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        async Task Try(string name, Func<Task> body)
        {
            try { await body(); }
            catch (Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); }
        }

        sb.AppendLine($"Ironite Launcher {App.VersionText} self-test {DateTime.Now:u}");
        var temp = Path.Combine(Path.GetTempPath(), "ironite-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        AppPaths.DataDir = Path.Combine(temp, "data");
        AppPaths.CacheDir = Path.Combine(temp, "cache");
        try
        {
            // ---- pure logic ----
            Check("Version is set", !string.IsNullOrWhiteSpace(App.VersionText) && App.VersionText != "0.0.0", App.VersionText);
            Check("Version 1.10.0 > 1.9.2", GitHubService.Newer("1.10.0", "1.9.2"));
            Check("Version v2.0 > 1.99", GitHubService.Newer("v2.0", "1.99"));
            Check("Version 1.0.0 == v1.0.0", !GitHubService.Newer("1.0.0", "v1.0.0") && !GitHubService.Newer("v1.0.0", "1.0.0"));
            Check("3.0.0 is newer than 2.16.0", GitHubService.Newer("3.0.0", "2.16.0"));
            Check("Guess title (_x64)", LauncherCore.GuessTitle(@"C:\Games\SuperTuxKart\supertuxkart_x64.exe") == "Supertuxkart", LauncherCore.GuessTitle(@"C:\Games\SuperTuxKart\supertuxkart_x64.exe"));
            Check("Guess title (CamelCase, Shipping)", LauncherCore.GuessTitle(@"C:\G\MyCoolGame-Win64-Shipping.exe") == "My Cool Game", LauncherCore.GuessTitle(@"C:\G\MyCoolGame-Win64-Shipping.exe"));
            Check("Steam profile link", AchievementService.SteamProfilePath("https://steamcommunity.com/id/someone/") == "id/someone");
            Check("Steam ID64", AchievementService.SteamProfilePath("76561197960287930") == "profiles/76561197960287930");
            Check("Steam bad text", AchievementService.SteamProfilePath("not a profile!") == "");

            // ---- Ironite games' achievements (the file Solar Siege 1.4.0+ writes) ----
            {
                var achDir = Path.Combine(temp, "achievements");
                Directory.CreateDirectory(Path.Combine(achDir, "solarsiege"));
                File.WriteAllBytes(Path.Combine(achDir, "solarsiege", "sunrise.png"), Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
                File.WriteAllText(Path.Combine(achDir, "solarsiege.json"), """
                {"schema":1,"game":"solarsiege","title":"Solar Siege","updated":1762790000,"icon_hidden":"solarsiege/hidden.png","achievements":[
                  {"id":"sunrise","name":"Sunrise","description":"Defeat Erebus.","category":"Campaign","hidden":false,"unlocked":true,"unlocked_at":1762790000,"progress":1,"target":1,"icon":"solarsiege/sunrise.png","icon_locked":"solarsiege/sunrise_locked.png"},
                  {"id":"ghostbuster","name":"Ghostbuster","description":"Destroy 100 Wraiths.","category":"Combat","hidden":false,"unlocked":false,"unlocked_at":0,"progress":37,"target":100,"icon":"solarsiege/ghostbuster.png","icon_locked":"solarsiege/ghostbuster_locked.png"},
                  {"id":"turtle","name":"Turtle","description":"Clear a planet without leaving one shield.","category":"Secret","hidden":true,"unlocked":false,"unlocked_at":0,"progress":0,"target":1,"icon":"solarsiege/turtle.png","icon_locked":"solarsiege/turtle_locked.png"},
                  {"id":"evil","name":"Escapes","description":"","category":"Secret","hidden":false,"unlocked":true,"unlocked_at":1,"icon":"../../outside.png"}
                ]}
                """);
                var was = AchievementService.IroniteDir;
                AchievementService.IroniteDir = achDir;
                try
                {
                    var r = AchievementService.Ironite("solarsiege");
                    Check("Reads an Ironite game's achievements", r is { Error: null, Total: 4, Unlocked: 2, Source: "ironite" }, r == null ? "null" : $"{r.Unlocked}/{r.Total} {r.Error}");
                    var sun = r?.Items.FirstOrDefault(i => i.Name == "Sunrise");
                    Check("Ironite achievement icon resolves to the file", sun?.Icon is { } ic && new Uri(ic).IsFile && File.Exists(new Uri(ic).LocalPath), sun?.Icon ?? "none");
                    var gb = r?.Items.FirstOrDefault(i => i.Name == "Ghostbuster");
                    Check("Ironite counting achievement keeps its progress", gb is { Progress: 37, Target: 100, Unlocked: false });
                    Check("Secret Ironite achievements stay secret", r?.Items.Any(i => i.Name == "Turtle") == false && r?.Items.Any(i => i.Name == "Secret achievement") == true);
                    Check("Ironite icon paths can't leave the folder", r?.Items.FirstOrDefault(i => i.Name == "Escapes")?.Icon == null);
                    Check("A game without a file has no Ironite achievements", AchievementService.Ironite("hearts") == null);
                    File.WriteAllText(Path.Combine(achDir, "broken.json"), "{ not json");
                    Check("A broken file shows an error, not a crash", AchievementService.Ironite("broken")?.Error != null);
                }
                finally { AchievementService.IroniteDir = was; }
            }
            Check("Safe file name", Platform.SafeFileName("Gin: Rummy?") == "Gin Rummy");

            var rel = new GhRelease
            {
                Tag = "v1.2.0",
                Assets = { new() { Name = "Source code.zip" }, new() { Name = "Game-1.2.0-Setup.exe" }, new() { Name = "Game-1.2.0-windows.zip" } },
            };
            Check("Picks the Windows zip", rel.GameAsset()?.Name == "Game-1.2.0-windows.zip", rel.GameAsset()?.Name ?? "none");
            rel.Assets.RemoveAt(2);
            Check("Falls back to the setup program", rel.GameAsset()?.Name == "Game-1.2.0-Setup.exe", rel.GameAsset()?.Name ?? "none");
            var lrel = new GhRelease { Tag = "v3.0.0", Assets = { new() { Name = "IroniteLauncher.exe" }, new() { Name = "IroniteLauncher-Setup-3.0.0.exe" } } };
            Check("Launcher setup asset", lrel.LauncherAsset()?.Name == "IroniteLauncher-Setup-3.0.0.exe");

            // ---- launcher 2.x data carries over ----
            await Try("Read 2.x settings", () =>
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(AppPaths.Settings, GoSettings);
                var s = JsonStore.Load(AppPaths.Settings, () => new Settings());
                Check("2.x stats", s.Stats["klondike"].PlayCount == 12 && s.Stats["klondike"].PlaySeconds == 5400 && s.Stats["klondike"].Sessions?.Count == 2);
                Check("2.x installs", s.Installs?["klondike"].Version == "1.4.0" && s.Installs["klondike"].Exe == "Klondike.exe" && s.Installs["klondike"].Kind == "zip");
                Check("2.x prefs", s.AutoUpdate == false && s.Favorites?.Count == 2 && s.Hidden?[0] == "lifeboat");
                Check("2.x own games", s.Custom?.Count == 2 && s.Custom[0].IsStore && s.Custom[0].Group == "Roguelikes" && s.Custom[1].Path == @"C:\Windows\notepad.exe"
                                       && s.Custom[1].Category == "puzzle" && s.Custom[0].XboxTitleId == "1234567890");
                Check("2.x accounts", s.SteamProfile == "steamcommunity.com/id/someone" && s.XblKey == "abc123");
                JsonStore.Save(AppPaths.Settings, s);
                var back = JsonNode.Parse(File.ReadAllText(AppPaths.Settings))!.AsObject();
                Check("Unknown settings are kept", back["futureSetting"]?["kept"]?.GetValue<bool>() == true);
                Check("Writes 2.x field names", back["stats"]!["klondike"]!["playSeconds"]?.GetValue<long>() == 5400
                                                && back["installs"]!["klondike"]!["installedAt"] != null && back["custom"]![0]!["appId"] != null);
                Check("No nulls written", !File.ReadAllText(AppPaths.Settings).Contains("null"));
                return Task.CompletedTask;
            });

            await Try("Read 2.x release cache", () =>
            {
                const string cache = """
                {"repos":{"adamglassner/klondike":{"etag":"W/\"abc\"","release":{"tag_name":"v1.4.0","name":"Klondike 1.4.0","body":"notes","html_url":"https://github.com/adamglassner/klondike/releases/tag/v1.4.0",
                "published_at":"2026-11-01T10:00:00Z","draft":false,"prerelease":false,"assets":[{"name":"Klondike-1.4.0-windows.zip","browser_download_url":"https://x/y.zip","size":123,"digest":"sha256:00"}]},
                "checked":"2026-11-09T12:00:00.123456789-05:00"},"adamglassner/lifeboat":{"etag":"","release":null,"checked":"2026-11-09T12:00:00Z"}},"catalogLauncherVersion":"2.16.0"}
                """;
                Directory.CreateDirectory(AppPaths.CacheDir);
                File.WriteAllText(AppPaths.ReleaseCache, cache);
                var gh = new GitHubService();
                gh.Load();
                var r = gh.Get("adamglassner/Klondike");
                Check("2.x release cache", r?.Version == "1.4.0" && r.GameAsset()?.Size == 123 && gh.Get("adamglassner/lifeboat") == null, r?.Tag ?? "missing");
                return Task.CompletedTask;
            });

            // ---- built-in catalog and art ----
            var catalog = JsonSerializer.Deserialize<Catalog>(LauncherCore.EmbeddedText("games.json"), JsonStore.Options)!;
            Check("Built-in catalog", catalog.Schema >= 2 && catalog.Games.Count >= 20 && catalog.Categories.Count >= 9, $"{catalog.Games.Count} games");
            var noCover = catalog.Games.Where(g => Ui.Pack($"Covers/{g.Id}.jpg") == null).Select(g => g.Id).ToList();
            Check("Every game has a cover", noCover.Count == 0, noCover.Count == 0 ? $"{catalog.Games.Count} covers" : "missing: " + string.Join(", ", noCover));
            var heroes = new[] { "klondike", "rewindlife", "euchre", "oldmaid", "spades", "cribbage", "ginrummy", "hearts", "mahjong", "solarsiege" };
            var noHero = heroes.Where(id => Ui.Pack($"Covers/{id}-hero.jpg") == null).ToList();
            Check("Featured banners", noHero.Count == 0, noHero.Count == 0 ? "10 banners" : "missing: " + string.Join(", ", noHero));
            Check("Logo and icon", Ui.Pack("Assets/ironite-logo.png") != null && Ui.Pack("Assets/icon-1024.png") != null);
            Check("Changelog has this version", LauncherCore.EmbeddedText("CHANGELOG.md").Contains($"## [{App.VersionText}]"));

            // ---- core start-up with 2.x data ----
            await Try("Core loads 2.x data", () =>
            {
                var core = new LauncherCore();
                core.Load();
                var st = core.States();
                Check("Core: catalog + own games", core.Catalog.Games.Count == catalog.Games.Count + 2 && core.Catalog.Categories.Any(c => c.Id == "custom"));
                Check("Core: own game in a launcher category", core.Find("custom-ffeeddccbbaa")?.Category == "puzzle" && core.Find("custom-ffeeddccbbaa")?.Group == "Puzzle");
                Check("Core: missing install forgotten", !st["klondike"].Installed, "the 2.x path doesn't exist here");
                Check("Core: stats", st["klondike"].PlayCount == 12 && st["klondike"].Favorite && st["lifeboat"].Hidden);
                Check("Core: web game available", st["dookieclicker"].Available);
                var (weeks, rows, since) = core.Stats();
                Check("Core: weekly stats", weeks.Length == 8 && rows.Count >= 1 && since == 1762600000);
                return Task.CompletedTask;
            });

            // ---- zip install ----
            await Try("Zip install", async () =>
            {
                var zipPath = Path.Combine(temp, "Test-1.0.0-windows.zip");
                using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    void Add(string name, string text)
                    {
                        using var w = new StreamWriter(z.CreateEntry(name).Open());
                        w.Write(text);
                    }
                    Add("TestGame/TestGame.exe", "MZ");
                    Add("TestGame/data/level1.txt", "hello");
                }
                var g = new CatalogGame { Id = "testgame", Bundle = "TestGame", Exe = "TestGame.exe" };
                Directory.CreateDirectory(Path.Combine(AppPaths.Games, "TestGame", "0.9.0"));
                var rec = await GameInstaller.InstallFileAsync(g, "1.0.0", zipPath, new InstallJob());
                Check("Zip: unpacked without its top folder", File.Exists(Path.Combine(rec.Dir, "TestGame.exe")) && File.Exists(Path.Combine(rec.Dir, "data", "level1.txt")), rec.Dir);
                Check("Zip: record", rec.Kind == "zip" && rec.Exe == "TestGame.exe" && rec.Version == "1.0.0");
                Check("Zip: old version removed", !Directory.Exists(Path.Combine(AppPaths.Games, "TestGame", "0.9.0")));
                Check("Legacy install found", GameInstaller.FindLegacy(g)?.Version == "1.0.0");
            });

            // ---- GitHub ----
            await Try("GitHub", async () =>
            {
                var (tag, answered) = await GitHubService.LatestTagAsync(GameRepo);
                Check("Latest tag from github.com (no API)", answered && !string.IsNullOrEmpty(tag), tag ?? "no release");
                var (ltag, _) = await GitHubService.LatestTagAsync(GitHubService.LauncherRepo);
                Check("Launcher's latest tag", !string.IsNullOrEmpty(ltag), ltag ?? "none");
                var gh = new GitHubService();
                await gh.FetchLatestAsync(GameRepo);
                var r = gh.Get(GameRepo);
                Check("GitHub latest release", r != null && r.Tag == tag, r?.Tag ?? "none");
                await gh.FetchLatestAsync(GitHubService.LauncherRepo);
                var lr = gh.Get(GitHubService.LauncherRepo);
                Check("Launcher release has a setup", lr?.LauncherAsset() != null, lr?.LauncherAsset()?.Name ?? "none");
                Info("Published catalog", await gh.FetchCatalogAsync(), GitHubService.CatalogUrl);
                if (r?.GameAsset() is { } asset)
                {
                    var job = new InstallJob();
                    var file = await GameInstaller.DownloadAsync(asset, job);
                    Check("Download with checksum", File.Exists(file) && new FileInfo(file).Length == asset.Size, $"{asset.Name} {Ui.Size(asset.Size)}{(asset.Digest != null ? ", sha256 checked" : "")}");
                    var g = catalog.Games.First(x => x.Repo == GameRepo);
                    var rec = await GameInstaller.InstallFileAsync(g, r.Version, file, job);
                    Check("Real game installs", File.Exists(Path.Combine(rec.Dir, rec.Exe)), Path.Combine(rec.Dir, rec.Exe));
                    await GameInstaller.RemoveInstallAsync(g, rec);
                    Check("Real game uninstalls", !Directory.Exists(GameInstaller.GameBase(g)));
                }
                else Check("Game has a download", false);
            });

            // ---- Windows ----
            await Try("Windows", async () =>
            {
                var procs = Platform.Processes();
                var me = (Environment.ProcessPath ?? "").ToLowerInvariant();
                Check("Running programs", procs.Count > 10 && procs.Any(p => p.Path == me), $"{procs.Count} running");
                Check("Match by program path", GameMatch.ForPath(Environment.ProcessPath!).Matches(procs).Count >= 1);
                var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
                var png = Platform.ExeIconPng(File.Exists(notepad) ? notepad : Environment.ProcessPath!);
                Check("Program icon", png is { Length: > 100 }, $"{png?.Length ?? 0} bytes");
                Check("Saved sign-in is encrypted (DPAPI)", Friends.FriendsService.DpapiWorks());
                Info("Browser for web games", Platform.FindBrowser() != null, Platform.FindBrowser() ?? "none");
                try
                {
                    var apps = await StoreApps.ListAsync();
                    Info("Xbox / Store apps listed", apps.Count > 0, $"{apps.Count} apps, {apps.Count(a => a.Game)} games");
                }
                catch (Exception ex) { Info("Xbox / Store apps listed", false, ex.Message); }
                var (sid, _) = AchievementService.DetectSteamUser();
                Info("Steam account", sid.Length > 0, sid.Length > 0 ? "found" : "Steam not installed");
            });
        }
        catch (Exception ex)
        {
            Check("Self-test crashed", false, ex.ToString());
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* ignore */ }
        }
        sb.AppendLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        File.WriteAllText(outputFile, sb.ToString());
        return failed;
    }
}
