using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ironite.Launcher.Services;

/// <summary>
/// CI's end-to-end run (<c>IroniteLauncher.exe --tour &lt;folder&gt;</c>): starts the launcher on a copy of
/// launcher 2.x's data, checks GitHub, installs a real game, plays it (counting the session), and saves a
/// screenshot of every page and dialog as PNGs in the folder, with tour.txt listing the checks.
/// Exit code = number of failed checks.
/// </summary>
public static class Tour
{
    public static async Task<int> RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        var failed = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failed++;
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
            File.WriteAllText(Path.Combine(dir, "tour.txt"), sb.ToString());
        }
        sb.AppendLine($"Ironite Launcher {App.VersionText} tour {DateTime.Now:u}");

        var data = Path.Combine(dir, "data");
        AppPaths.DataDir = Path.Combine(data, "roaming");
        AppPaths.CacheDir = Path.Combine(data, "local");
        Directory.CreateDirectory(AppPaths.DataDir);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        // Play history and own games, written the way launcher 2.16 wrote them.
        var settings = new JsonObject
        {
            ["stats"] = new JsonObject
            {
                ["hearts"] = Stats(9, 4 * 3600 + 1200, now - 86400, new[] { (now - 86400 * 1, 3000L), (now - 86400 * 9, 5400L), (now - 86400 * 20, 4800L) }),
                ["euchre"] = Stats(4, 2 * 3600, now - 3 * 86400, new[] { (now - 86400 * 3, 3600L), (now - 86400 * 16, 3600L) }),
                ["dookieclicker"] = Stats(3, 1500, now - 6 * 86400, new[] { (now - 86400 * 6, 1500L) }),
                ["custom-a1a1a1a1a1a1"] = Stats(2, 900, now - 2 * 86400, new[] { (now - 86400 * 2, 900L) }),
            },
            ["favorites"] = new JsonArray("hearts", "solarsiege"),
            ["hidden"] = new JsonArray("carnival"),
            ["custom"] = new JsonArray(
                new JsonObject { ["id"] = "custom-a1a1a1a1a1a1", ["title"] = "Notepad Adventure", ["path"] = notepad, ["addedAt"] = now - 86400 * 30, ["group"] = "Classics", ["developer"] = "Microsoft" },
                new JsonObject { ["id"] = "custom-b2b2b2b2b2b2", ["title"] = "Moved Game", ["path"] = @"C:\Games\Gone\Gone.exe", ["addedAt"] = now - 86400 * 10, ["category"] = "arcade" }),
            ["futureSetting"] = "kept",
        };
        File.WriteAllText(AppPaths.Settings, settings.ToJsonString());
        await WriteSampleAchievementsAsync(Path.Combine(data, "achievements"), now);

        var core = App.Core;
        MainWindow? w = null;
        try
        {
            core.Load();
            core.StartBackground();
            Environment.SetEnvironmentVariable("IRONITE_FRIENDS_URL", "fake"); // a pretend Friends server
            App.StartFriends();
            Check("Starts on 2.x data", core.Catalog.Games.Any(g => g.Id == "custom-a1a1a1a1a1a1"));
            w = new MainWindow { Width = 1320, Height = 860, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
            Application.Current.MainWindow = w;
            w.Show();
            await Until(() => core.LastCheck != null, 90);
            Check("Checked GitHub", core.LastCheck != null && core.CheckError.Length == 0, core.CheckError);
            var st = core.States();
            Check("Games available from GitHub", st.Count(s => s.Value.Available && !s.Value.Installed) >= 10, $"{st.Count(s => s.Value.Available)} available");
            w.Refresh(force: true);
            await Settle();
            Shot(w, dir, "01-library");
            w.MainScroll.ScrollToVerticalOffset(900);
            await Settle();
            Shot(w, dir, "02-library-scrolled");
            w.MainScroll.ScrollToVerticalOffset(2600);
            await Settle();
            Shot(w, dir, "03-library-more");
            w.MainScroll.ScrollToTop();
            w._search.Focus();
            w._search.Text = "Kl";
            w._search.CaretIndex = 2;
            await Settle();
            Shot(w, dir, "03b-search");
            w._search.Text = "";

            // Install a real game (Klondike) and watch it download.
            core.Install("klondike");
            await Until(() => core.States()["klondike"] is { Installing: true, Progress: > 5 }, 60);
            w.Go("all");
            w.Refresh(force: true);
            await Settle();
            Shot(w, dir, "04-downloading");
            await Until(() => core.States()["klondike"].Installed || core.States()["klondike"].Error != null, 240);
            var k = core.States()["klondike"];
            Check("Installs Klondike from GitHub", k.Installed, k.Error ?? $"v{k.InstalledVersion}");

            w.Go("installed");
            await Settle();
            Shot(w, dir, "05-installed");

            // Play it: the open and the session are counted.
            if (k.Installed)
            {
                core.Launch("klondike");
                await Task.Delay(10000);
                w.Refresh(force: true);
                await Settle();
                Shot(w, dir, "06-playing");
                Check("Shows the game running", core.States()["klondike"].Running);
                var exe = core.Settings.Installs!["klondike"];
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (string.Equals(p.MainModule?.FileName, Path.Combine(exe.Dir, exe.Exe), StringComparison.OrdinalIgnoreCase)) p.Kill(true);
                    }
                    catch { /* other users' programs */ }
                }
                await Until(() => !core.States()["klondike"].Running, 30);
                var stats = core.Settings.Stats["klondike"];
                Check("Counts the open and the session", stats.PlayCount == 1 && stats.Sessions?.Count == 1 && stats.PlaySeconds >= 5,
                    $"opened {stats.PlayCount}, {stats.PlaySeconds}s, {stats.Sessions?.Count ?? 0} session(s)");
            }

            w.Go("all");
            await Settle();
            w.Details("klondike");
            await Settle();
            Shot(w, dir, "07-details");
            w.Details("hearts");
            await Settle();
            Shot(w, dir, "08-details-not-installed");
            w.Details("solarsiege");
            await Settle(1500);
            Shot(w, dir, "08b-details-achievements");
            w.Details("custom-a1a1a1a1a1a1");
            await Settle();
            Shot(w, dir, "09-details-own-game");
            w.EditDialog(core.Find("custom-a1a1a1a1a1a1")!);
            await Settle();
            Shot(w, dir, "10-edit-own-game");
            w.AddGameDialog();
            await Settle();
            Shot(w, dir, "11-add-game");
            w.CloseModal();

            foreach (var (view, name) in new[] { ("custom", "12-non-ironite"), ("card", "13-card-games"), ("sim", "14-simulation"), ("stats", "15-stats"),
                         ("achievements", "16-achievements"), ("news", "18-whats-new"), ("about", "19-about"), ("settings", "20-settings") })
            {
                w.Go(view);
                await Settle(view == "achievements" ? 3000 : 900);
                Shot(w, dir, name);
            }

            // Friends: register, confirm with the emailed code, see friends and requests.
            w.Go("friends");
            await Settle();
            Shot(w, dir, "17-friends-sign-in");
            var fr = App.Friends;
            await fr.SignUpAsync("player@example.com", "correct-horse", "AdamG");
            await fr.RefreshAsync();
            Check("Friends: register and sign in", fr.SignedIn && fr.Username == "AdamG" && fr.Friends.Count == 3, $"{fr.Username}, {fr.Friends.Count} friends");
            Check("Friends: shows what friends are playing", fr.Friends.Any(f => f.PlayingTitle == "Solar Siege"));
            w.Refresh(force: true);
            w.Go("friends");
            await Settle();
            Shot(w, dir, "17b-friends");
            var (prof, lib) = await fr.LoadProfileAsync("f1");
            Check("Friends: a friend's profile and games", prof?.Username == "NovaPilot" && lib.Games.Count == 4, $"{lib.Games.Count} games");
            await fr.SaveProfileAsync("violet", "klondike", "Klondike", null);
            Check("Friends: save your profile", fr.MyProfile?.Theme == "violet" && fr.MyProfile.FavoriteTitle == "Klondike");
            var mine = await App.Core.SharedGamesAsync();
            Check("Friends: shares played games", mine.Any(g => g.Id == "hearts" && g.PlaySeconds > 0), $"{mine.Count} games");
            w.OpenProfileForTour("f1");
            await Settle(1500);
            Shot(w, dir, "17c-friend-profile");
            w.Go("friends");
            await Settle();
            Shot(w, dir, "17d-friends-themed");
            w.EditProfileForTour();
            await Settle();
            Shot(w, dir, "17e-edit-profile");
            w.CloseModal();
            await fr.SignOutAsync();
            Check("Friends: sign out", !fr.SignedIn);

            // Uninstall again.
            await core.UninstallAsync("klondike");
            Check("Uninstalls", !core.States()["klondike"].Installed && !Directory.Exists(Path.Combine(AppPaths.Games, "Klondike")));

            var saved = JsonNode.Parse(File.ReadAllText(AppPaths.Settings))!;
            Check("Keeps settings it doesn't know", saved["futureSetting"]?.GetValue<string>() == "kept");
            Check("Keeps play history", saved["stats"]?["hearts"]?["playCount"]?.GetValue<int>() == 9);
        }
        catch (Exception ex)
        {
            Check("Tour crashed", false, ex.ToString());
            if (w != null) Shot(w, dir, "99-crash");
        }
        sb.AppendLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
        File.WriteAllText(Path.Combine(dir, "tour.txt"), sb.ToString());
        try { File.Copy(AppPaths.Log, Path.Combine(dir, "launcher.log"), true); } catch { /* ignore */ }
        return failed;
    }

    private static JsonObject Stats(int count, long secs, long last, (long Start, long Secs)[] sessions) => new()
    {
        ["playCount"] = count, ["playSeconds"] = secs, ["lastPlayed"] = last,
        ["sessions"] = new JsonArray(sessions.Select(s => (JsonNode)new JsonObject { ["start"] = s.Start, ["secs"] = s.Secs }).ToArray()),
    };

    /// <summary>
    /// A Solar Siege achievements file like the game writes, with its real medal icons from the game's repository,
    /// so the Achievements page and Solar Siege's details have something to show.
    /// </summary>
    private static async Task WriteSampleAchievementsAsync(string dir, long now)
    {
        AchievementService.IroniteDir = dir;
        var icons = Path.Combine(dir, "solarsiege");
        Directory.CreateDirectory(icons);
        var list = new (string Id, string Name, string Desc, string Cat, bool Got, int Prog, int Target, bool Hidden)[]
        {
            ("first_contact", "First Contact", "Clear Mercury.", "Campaign", true, 1, 1, false),
            ("homeworld", "Homeworld Secured", "Clear Earth.", "Campaign", true, 1, 1, false),
            ("edge_of_darkness", "The Edge of Darkness", "Reach Pluto.", "Campaign", false, 0, 1, false),
            ("sunrise", "Sunrise", "Defeat Erebus and save the solar system.", "Campaign", false, 0, 1, false),
            ("ghostbuster", "Ghostbuster", "Destroy 100 Wraiths.", "Combat", false, 37, 100, false),
            ("saucer_hunter", "Saucer Hunter", "Shoot down 10 bonus saucers.", "Combat", true, 10, 10, false),
            ("arsenal", "Arsenal", "Have all 8 timed power-ups active at once.", "Power-Ups", true, 1, 1, false),
            ("collector", "Collector", "Pick up every type of power-up.", "Power-Ups", false, 9, 11, false),
            ("squadron", "Squadron", "Clear a planet with 4 pilots.", "Multiplayer", false, 0, 1, false),
            ("turtle", "Turtle", "Clear a planet without leaving the cover of one shield.", "Secret", false, 0, 1, true),
            ("lore_reader", "Lore Reader", "Watch the whole story without skipping.", "Secret", true, 1, 1, true),
        };
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubService.UserAgent);
        foreach (var n in list.SelectMany(a => new[] { a.Id, a.Id + "_locked" }).Append("hidden"))
        {
            try
            {
                var bytes = await http.GetByteArrayAsync($"https://raw.githubusercontent.com/adamglassner/Solar-Siege/main/assets/sprites/achievements/{n}.png");
                await File.WriteAllBytesAsync(Path.Combine(icons, n + ".png"), bytes);
            }
            catch { /* the page falls back to plain stars */ }
        }
        var arr = new JsonArray();
        var i = 0;
        foreach (var a in list)
            arr.Add(new JsonObject
            {
                ["id"] = a.Id, ["name"] = a.Name, ["description"] = a.Desc, ["category"] = a.Cat, ["hidden"] = a.Hidden, ["unlocked"] = a.Got,
                ["unlocked_at"] = a.Got ? now - 3600 * (++i) : 0, ["progress"] = a.Prog, ["target"] = a.Target,
                ["icon"] = $"solarsiege/{a.Id}.png", ["icon_locked"] = $"solarsiege/{a.Id}_locked.png",
            });
        var file = new JsonObject { ["schema"] = 1, ["game"] = "solarsiege", ["title"] = "Solar Siege", ["version"] = "1.4.0", ["updated"] = now, ["icon_hidden"] = "solarsiege/hidden.png", ["achievements"] = arr };
        await File.WriteAllTextAsync(Path.Combine(dir, "solarsiege.json"), file.ToJsonString());
    }

    private static async Task Until(Func<bool> done, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.Elapsed.TotalSeconds < seconds) await Task.Delay(500);
    }

    private static async Task Settle(int ms = 900)
    {
        await Task.Delay(ms);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Shot(Window w, string dir, string name)
    {
        try
        {
            w.UpdateLayout();
            var root = (FrameworkElement)w.Content;
            var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var f = File.Create(Path.Combine(dir, name + ".png"));
            enc.Save(f);
        }
        catch (Exception ex) { Log.Error("screenshot " + name, ex); }
    }
}
