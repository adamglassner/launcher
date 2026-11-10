using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ironite.Launcher.Models;

namespace Ironite.Launcher.Services;

/// <summary>A game the launcher sees running.</summary>
internal sealed class RunningGame
{
    public DateTime Start;
    public DateTime Credited; // last time play time was saved
}

/// <summary>
/// Everything the launcher does, apart from drawing it: the catalog, installs and updates from GitHub,
/// the download queue, launching games and counting play time, the player's own games, and the launcher's
/// own updates. All state is guarded by one lock; the UI reads <see cref="States"/> and redraws when it changes.
/// </summary>
public sealed class LauncherCore
{
    public const string CustomCategoryId = "custom";
    public const int MaxSessions = 200;

    private readonly object _mu = new();
    private Catalog _base = new();
    private string _baseJson = "";
    public Catalog Catalog { get; private set; } = new();
    /// <summary>Bumps whenever the catalog changes (games added, edited, art changed).</summary>
    public int CatalogVersion { get; private set; }

    public Settings Settings { get; private set; } = new();
    public GitHubService GitHub { get; } = new();

    private readonly Dictionary<string, RunningGame> _running = new();
    private readonly Dictionary<string, DateTime> _starting = new();       // Store / Xbox games Windows is starting
    private readonly Dictionary<string, InstallJob> _jobs = new();
    private readonly Dictionary<string, string> _launchErr = new();
    private readonly List<string> _queue = new();                          // first one is downloading
    private readonly Dictionary<string, Func<Task>> _tasks = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly Dictionary<string, long> _sizes = new();
    private readonly Dictionary<string, CancellationTokenSource> _stopWatch = new();
    private readonly SemaphoreSlim _checkNow = new(0);
    private InstallJob? _selfJob;

    public bool Checking { get; private set; }
    public DateTime? LastCheck { get; private set; }
    public string CheckError { get; private set; } = "";
    public bool CheckBusy { get; private set; }

    public bool AutoUpdate => Settings.AutoUpdate != false;

    // ---------------------------------------------------------------- start-up

    public void Load()
    {
        Settings = JsonStore.Load(AppPaths.Settings, () => new Settings());
        Settings.Stats ??= new();
        Settings.Installs ??= new();
        GitHub.Load();
        lock (_mu)
        {
            var cached = GitHub.CachedCatalogJson();
            if (cached == null || !TrySetCatalog(cached))
                if (!TrySetCatalog(EmbeddedText("games.json")))
                    throw new InvalidDataException("The built-in game catalog is damaged.");
            SaveSettings();
        }
        GameInstaller.TryDeleteDir(AppPaths.Downloads); // leftovers from an interrupted download
    }

    public void StartBackground()
    {
        _ = Task.Run(QueueWorker);
        _ = Task.Run(UpdateLoop);
        _ = Task.Run(MeasureAllSizes);
        _ = Task.Run(WatchGames);
        _ = Task.Run(async () => { await Task.Delay(15000); GameInstaller.TryDelete(AppPaths.OldUpdateHelper); }); // left by a 2.x update
    }

    public static string EmbeddedText(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public void SaveSettings() // caller holds _mu
    {
        try { JsonStore.Save(AppPaths.Settings, Settings); }
        catch (Exception ex) { Log.Error("save settings", ex); }
    }

    // ---------------------------------------------------------------- catalog

    /// <summary>Switches to a catalog (built in, or the newer published one). Caller holds _mu.</summary>
    private bool TrySetCatalog(string json)
    {
        Catalog? c;
        try { c = JsonSerializer.Deserialize<Catalog>(json, JsonStore.Options); }
        catch (Exception ex) { Log.Error("catalog", ex); return false; }
        if (c == null || c.Games.Count == 0) return false;
        _base = c;
        _baseJson = json;
        // Games installed by launcher 1.x (unpacked from inside the launcher) keep working.
        foreach (var g in c.Games)
        {
            if (g.IsWeb || string.IsNullOrEmpty(g.Bundle)) continue;
            if (Settings.Installs!.TryGetValue(g.Id, out var rec))
            {
                if (File.Exists(Path.Combine(rec.Dir, rec.Exe))) continue;
                Settings.Installs.Remove(g.Id); // files were removed by hand
            }
            if (GameInstaller.FindLegacy(g) is { } l)
            {
                if (Settings.Stats.TryGetValue(g.Id, out var s) && s.InstalledAt != 0) l.InstalledAt = s.InstalledAt;
                Settings.Installs[g.Id] = l;
                Log.Info($"found {g.Id} {l.Version} from an earlier launcher");
            }
        }
        MergeCustom();
        return true;
    }

    /// <summary>Rebuilds the catalog the UI shows: the Ironite catalog plus the player's own games. Caller holds _mu.</summary>
    private void MergeCustom()
    {
        var c = new Catalog { Schema = Math.Max(2, _base.Schema), Categories = _base.Categories.ToList(), Games = _base.Games.ToList() };
        if (c.Categories.All(x => x.Id != CustomCategoryId))
            c.Categories.Add(new CatalogCategory
            {
                Id = CustomCategoryId, Name = "Non-Ironite Games",
                Blurb = "Your own games from this PC. Add any game's program to play it from the launcher, with play time tracked like everything else.",
            });
        var names = _base.Categories.ToDictionary(x => x.Id, x => x.Name);
        foreach (var cg in Settings.Custom ?? new())
        {
            var g = new CatalogGame
            {
                Id = cg.Id, Title = cg.Title, Subtitle = string.IsNullOrEmpty(cg.Developer) ? "Your game" : cg.Developer,
                Category = CustomCategoryId, Platforms = new() { "windows" },
                Players = string.IsNullOrEmpty(cg.Players) ? "On this PC" : cg.Players, Type = "local",
                Exe = cg.IsStore ? "Xbox / Microsoft Store app" : cg.Path, Description = cg.Description, Group = cg.Group, Custom = cg,
            };
            if (!string.IsNullOrEmpty(cg.Category) && cg.Category != CustomCategoryId && names.TryGetValue(cg.Category, out var name))
            {
                g.Category = cg.Category;
                g.Group = name;
            }
            c.Games.Add(g);
        }
        Catalog = c;
        CatalogVersion++;
    }

    public CatalogGame? Find(string id)
    {
        lock (_mu) return Catalog.Games.FirstOrDefault(g => g.Id == id);
    }

    private CustomGame? CustomById(string id) => Settings.Custom?.FirstOrDefault(c => c.Id == id); // caller holds _mu

    // ---------------------------------------------------------------- state

    public Dictionary<string, GameState> States()
    {
        lock (_mu) return StatesLocked();
    }

    private Dictionary<string, GameState> StatesLocked()
    {
        var output = new Dictionary<string, GameState>();
        var now = DateTime.UtcNow;
        foreach (var g in Catalog.Games)
        {
            var st = new GameState { Running = _running.ContainsKey(g.Id) || _starting.ContainsKey(g.Id) };
            if (g.IsLocal)
            {
                st.Available = st.Installed = true;
                if (CustomById(g.Id) is { } cg)
                {
                    st.InstalledAt = cg.AddedAt;
                    if (cg.IsStore)
                    {
                        if (!string.IsNullOrEmpty(cg.Location) && !Directory.Exists(cg.Location))
                            st.LaunchError = "This Xbox / Microsoft Store game doesn't seem to be installed any more. Reinstall it from the Xbox app or Microsoft Store, or remove it from the launcher.";
                    }
                    else if (!File.Exists(cg.Path))
                        st.LaunchError = $"The game's program wasn't found at {cg.Path}. If it moved, remove it from the launcher and add it again.";
                }
            }
            else if (g.IsWeb)
            {
                st.Available = !string.IsNullOrEmpty(g.Url);
            }
            else if (!string.IsNullOrEmpty(g.Repo))
            {
                if (Settings.Installs!.TryGetValue(g.Id, out var rec))
                {
                    st.Installed = true;
                    st.InstalledVersion = rec.Version;
                    st.InstalledAt = rec.InstalledAt;
                }
                if (GitHub.Get(g.Repo) is { } r && r.GameAsset() is { } asset)
                {
                    st.Available = true;
                    st.LatestVersion = r.Version;
                    st.Notes = r.Body;
                    st.ReleaseName = r.Name;
                    st.ReleaseUrl = r.HtmlUrl;
                    st.PublishedAt = r.PublishedAt is { } p ? new DateTimeOffset(p).ToUnixTimeSeconds() : 0;
                    st.Size = asset.Size;
                    st.Update = st.Installed && GitHubService.Newer(st.LatestVersion, st.InstalledVersion);
                }
                if (st.Installed) st.Available = true;
            }
            st.Favorite = Settings.Favorites?.Contains(g.Id) == true;
            st.Hidden = Settings.Hidden?.Contains(g.Id) == true;
            if (!st.Available && !st.Installed && !Settings.Stats.ContainsKey(g.Id) && !st.Favorite && !st.Hidden)
            {
                output[g.Id] = st; // coming soon
                continue;
            }
            long live = 0;
            if (_running.TryGetValue(g.Id, out var run))
            {
                st.SessionStart = new DateTimeOffset(run.Start).ToUnixTimeSeconds();
                live = (long)(now - run.Credited).TotalSeconds;
            }
            if (_jobs.TryGetValue(g.Id, out var j))
            {
                if (j.Error != null) st.Error = j.Error;
                else
                {
                    st.Installing = true;
                    st.Updating = j.Update;
                    st.Phase = j.Phase;
                    st.Progress = j.Progress;
                }
            }
            if (st.Installed) st.DiskSize = _sizes.GetValueOrDefault(g.Id);
            var qi = _queue.IndexOf(g.Id);
            if (qi >= 0) { st.QueuePos = qi + 1; st.QueueLen = _queue.Count; }
            if (_launchErr.TryGetValue(g.Id, out var le)) st.LaunchError = le;
            if (Settings.Stats.TryGetValue(g.Id, out var s))
            {
                st.PlayCount = s.PlayCount;
                st.LastPlayed = s.LastPlayed;
                st.PlaySeconds = s.PlaySeconds + Math.Max(0, live);
            }
            output[g.Id] = st;
        }
        return output;
    }

    public bool GamesRunning() { lock (_mu) return _running.Count > 0 || _starting.Count > 0; }

    /// <summary>A game is downloading or installing.</summary>
    public bool Busy() { lock (_mu) return _jobs.Values.Any(j => j.Error == null) || _selfJob is { Error: null }; }

    // ---------------------------------------------------------------- install / uninstall

    /// <summary>Downloads the game's latest release and installs it (or updates it). Throws with a message for the player.</summary>
    public void Install(string id)
    {
        lock (_mu)
        {
            var g = Catalog.Games.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("unknown game");
            if (g.IsWeb) throw new InvalidOperationException("web games play online and don't need installing");
            if (g.IsLocal) throw new InvalidOperationException("that game is already on this PC");
            var r = GitHub.Get(g.Repo);
            if (string.IsNullOrEmpty(g.Repo) || r?.GameAsset() == null)
                throw new InvalidOperationException(Settings.Installs!.ContainsKey(id) ? "there's no newer version to download right now" : "that game is coming soon");
            if (_running.ContainsKey(id)) throw new InvalidOperationException("close the game before updating it");
            if (_jobs.TryGetValue(id, out var existing) && existing.Error == null) return; // already installing
            if (Settings.Installs!.TryGetValue(id, out var cur) && !GitHubService.Newer(r.Version, cur.Version)) return; // up to date
            var job = new InstallJob { Update = Settings.Installs.ContainsKey(id), Phase = "Queued" };
            _jobs[id] = job;
            var asset = r.GameAsset()!;
            var ver = r.Version;
            // Downloads wait their turn: one at a time, in the order they were asked for.
            Enqueue(id, async () =>
            {
                job.Phase = "Downloading";
                InstallRecord? rec = null;
                Exception? err = null;
                try
                {
                    var file = await GameInstaller.DownloadAsync(asset, job);
                    try { rec = await GameInstaller.InstallFileAsync(g, ver, file, job); }
                    finally { GameInstaller.TryDelete(file); }
                }
                catch (Exception ex) { err = ex; }
                lock (_mu)
                {
                    if (err != null || rec == null)
                    {
                        Log.Error($"install {id} {ver}", err);
                        job.Error = err is OperationCanceledException ? "Install cancelled." : "Install failed: " + err?.Message;
                        return;
                    }
                    _jobs.Remove(id);
                    if (Settings.Installs!.TryGetValue(id, out var old) && old.InstalledAt != 0) rec.InstalledAt = old.InstalledAt; // first install date
                    Settings.Installs[id] = rec;
                    var s = StatsFor(id);
                    s.InstalledAt = rec.InstalledAt;
                    SaveSettings();
                    Log.Info($"installed {id} {ver} ({rec.Kind}) in {rec.Dir}");
                }
                _ = Task.Run(() => MeasureSize(id));
            });
        }
    }

    /// <summary>Removes a game's files. Saves and settings, which games keep in the player's profile, stay.</summary>
    public async Task UninstallAsync(string id)
    {
        CatalogGame g;
        InstallRecord? rec;
        lock (_mu)
        {
            g = Catalog.Games.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("unknown game");
            if (g.IsWeb) throw new InvalidOperationException("web games play online and have nothing to uninstall");
            if (g.IsLocal) { RemoveCustomLocked(id); return; }
            if (_running.ContainsKey(id)) throw new InvalidOperationException("close the game before uninstalling it");
            if (_jobs.TryGetValue(id, out var j) && j.Error == null) throw new InvalidOperationException("the game is still installing");
            _jobs.Remove(id);
            Settings.Installs!.TryGetValue(id, out rec);
        }
        Exception? err = null;
        try { await GameInstaller.RemoveInstallAsync(g, rec); } catch (Exception ex) { err = ex; }
        lock (_mu)
        {
            Settings.Installs!.Remove(id);
            _sizes.Remove(id);
            if (Settings.Stats.TryGetValue(id, out var s)) s.InstalledAt = 0;
            SaveSettings();
        }
        if (err != null) throw err;
    }

    public void Dismiss(string id)
    {
        lock (_mu)
        {
            if (_jobs.TryGetValue(id, out var j) && j.Error != null) _jobs.Remove(id);
            _launchErr.Remove(id);
            if (id == "launcher" && _selfJob?.Error != null) _selfJob = null;
        }
    }

    // ---------------------------------------------------------------- download queue

    private void Enqueue(string id, Func<Task> task) // caller holds _mu
    {
        _tasks[id] = task;
        _queue.Add(id);
        _queueSignal.Release();
    }

    private async Task QueueWorker()
    {
        while (true)
        {
            await _queueSignal.WaitAsync();
            string id;
            Func<Task>? task;
            lock (_mu)
            {
                if (_queue.Count == 0) continue;
                id = _queue[0];
                _tasks.TryGetValue(id, out task);
            }
            try { if (task != null) await task(); }
            catch (Exception ex) { Log.Error($"queue {id}", ex); }
            lock (_mu)
            {
                if (_queue.Count > 0 && _queue[0] == id) _queue.RemoveAt(0);
                _tasks.Remove(id);
            }
        }
    }

    // ---------------------------------------------------------------- sizes

    private void MeasureSize(string id)
    {
        string? dir;
        lock (_mu) dir = Settings.Installs!.TryGetValue(id, out var rec) ? rec.Dir : null;
        if (dir == null) return;
        var n = GameInstaller.DirSize(dir);
        lock (_mu) _sizes[id] = n;
    }

    private void MeasureAllSizes()
    {
        List<string> ids;
        lock (_mu) ids = Settings.Installs!.Keys.ToList();
        foreach (var id in ids) MeasureSize(id);
    }

    public long DiskTotal() { lock (_mu) return _sizes.Values.Sum(); }

    // ---------------------------------------------------------------- launching and play time

    private GameStats StatsFor(string id) // caller holds _mu
    {
        if (!Settings.Stats.TryGetValue(id, out var s)) Settings.Stats[id] = s = new GameStats();
        return s;
    }

    /// <summary>Starts a game. Throws with a message for the player.</summary>
    public void Launch(string id)
    {
        CatalogGame g;
        InstallRecord? rec;
        CustomGame? cg;
        lock (_mu)
        {
            g = Catalog.Games.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("unknown game");
            if (_running.ContainsKey(id) || _starting.ContainsKey(id)) throw new InvalidOperationException("already running");
            if (_jobs.TryGetValue(id, out var j) && j.Error == null) throw new InvalidOperationException("still installing");
            Settings.Installs!.TryGetValue(id, out rec);
            cg = CustomById(id);
        }

        ProcessStartInfo psi;
        GameMatch? store = null;
        if (g.IsWeb)
        {
            // Its own app window and browser profile, kept in %APPDATA% so the game's saved progress
            // survives updates and uninstalls.
            var profile = Path.Combine(AppPaths.WebGames, g.Id);
            Directory.CreateDirectory(profile);
            var browser = Platform.FindBrowser() ?? throw new InvalidOperationException("Microsoft Edge or Google Chrome is needed to play web games");
            psi = new ProcessStartInfo(browser) { UseShellExecute = false };
            foreach (var a in new[] { "--app=" + g.Url, "--user-data-dir=" + profile, "--window-size=1280,860", "--no-first-run",
                         "--no-default-browser-check", "--disable-background-mode", "--disable-features=Translate,msEdgeSidebarV2,msUndersideButton,msHubApps" })
                psi.ArgumentList.Add(a);
        }
        else if (g.IsLocal)
        {
            if (cg == null) throw new InvalidOperationException("unknown game");
            if (cg.IsStore)
            {
                // Windows starts Store / Xbox apps; the launcher then watches for the game's programs to count play time.
                psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), @"shell:AppsFolder\" + cg.AppId)
                { UseShellExecute = false, CreateNoWindow = true };
                store = GameMatch.ForStore(cg.AppId!, cg.Location ?? "");
            }
            else
            {
                if (!File.Exists(cg.Path)) throw new InvalidOperationException("the game's program wasn't found at " + cg.Path);
                psi = new ProcessStartInfo(cg.Path!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(cg.Path) };
            }
        }
        else
        {
            if (rec == null) throw new InvalidOperationException("install the game first");
            var exe = Path.Combine(rec.Dir, rec.Exe);
            if (!File.Exists(exe))
            {
                // Usually antivirus software removed the program. Forget the install so Install can download it again.
                Log.Info($"launch {id}: {exe} is missing");
                lock (_mu) { Settings.Installs!.Remove(id); SaveSettings(); }
                throw new InvalidOperationException("the game's program is missing (Windows Security may have removed it); click Install to download it again");
            }
            psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) };
        }

        Log.Info($"starting {id}: {psi.FileName}");
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex)
        {
            Log.Error($"launch {id}", ex);
            if (ex.Message.Contains("virus", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows Security blocked the game from starting");
            if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 })
                throw new InvalidOperationException("the game wasn't started (administrator permission was not given)");
            throw new InvalidOperationException($"the game couldn't start ({ex.Message})");
        }
        lock (_mu) _launchErr.Remove(id);

        if (store != null)
        {
            // Windows starts Store / Xbox games, so wait to see the game's own programs before counting.
            lock (_mu) _starting[id] = DateTime.UtcNow;
            proc?.Dispose();
            _ = Task.Run(async () =>
            {
                var seen = await WaitUntilRunning(store, TimeSpan.FromMinutes(2));
                lock (_mu)
                {
                    _starting.Remove(id);
                    if (!seen)
                        _launchErr[id] = $"{g.Title} didn't seem to start (the launcher couldn't see it running), so no play time was counted. If it's open, the launcher will notice it within a few seconds.";
                }
                if (seen) await TrackRunning(id, store, "started from the launcher");
                else Log.Info($"{id}: none of its programs appeared");
            });
            return;
        }

        var start = DateTime.UtcNow;
        BeginSession(id, start);
        var exited = Task.Run(async () =>
        {
            if (proc == null) return; // handed to another process: nothing to wait on
            using (proc)
            {
                await proc.WaitForExitAsync();
                var took = DateTime.UtcNow - start;
                int code;
                try { code = proc.ExitCode; } catch { code = 0; }
                if (took < TimeSpan.FromSeconds(8) && !g.IsWeb && code != 0)
                {
                    Log.Info($"{id} exited after {took.TotalSeconds:0.0}s with code {code}");
                    lock (_mu) _launchErr[id] = $"{g.Title} closed right after it started (exit code {code}). If Windows Security showed a warning, allow the game there, then try again.";
                }
            }
        });
        _ = TrackSession(id, start, exited);
    }

    /// <summary>Counts an open and marks the game as running.</summary>
    private void BeginSession(string id, DateTime start)
    {
        lock (_mu)
        {
            _running[id] = new RunningGame { Start = start, Credited = start };
            var s = StatsFor(id);
            s.PlayCount++;
            s.LastPlayed = new DateTimeOffset(start).ToUnixTimeSeconds();
            SaveSettings();
        }
    }

    /// <summary>Adds up play time until <paramref name="exited"/> completes, saving every minute, then records the session.</summary>
    private async Task TrackSession(string id, DateTime start, Task exited)
    {
        var last = start;
        void Credit(DateTime now)
        {
            lock (_mu)
            {
                if (Settings.Stats.TryGetValue(id, out var s))
                {
                    s.PlaySeconds += (long)Math.Round((now - last).TotalSeconds);
                    s.LastPlayed = new DateTimeOffset(now).ToUnixTimeSeconds();
                }
                last = now;
                if (_running.TryGetValue(id, out var r)) r.Credited = now;
                SaveSettings();
            }
        }
        while (true)
        {
            var done = await Task.WhenAny(exited, Task.Delay(TimeSpan.FromMinutes(1)));
            Credit(DateTime.UtcNow);
            if (done != exited) continue;
            lock (_mu)
            {
                var secs = (long)(DateTime.UtcNow - start).TotalSeconds;
                if (Settings.Stats.TryGetValue(id, out var s) && secs > 0)
                {
                    s.Sessions ??= new();
                    s.Sessions.Add(new PlaySession { Start = new DateTimeOffset(start).ToUnixTimeSeconds(), Secs = secs });
                    if (s.Sessions.Count > MaxSessions) s.Sessions.RemoveRange(0, s.Sessions.Count - MaxSessions);
                    SaveSettings();
                }
                _running.Remove(id);
            }
            Log.Info($"{id} closed after {DateTime.UtcNow - start:hh\\:mm\\:ss}");
            RequestCheck(); // a postponed update can install now
            return;
        }
    }

    private static async Task<bool> WaitUntilRunning(GameMatch m, TimeSpan limit)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < limit)
        {
            await Task.Delay(2000);
            var (n, what) = m.Matches(Platform.Processes());
            if (n > 0) { Log.Info($"found running: {what}"); return true; }
        }
        return false;
    }

    /// <summary>
    /// Counts an open and the play time of a game that's running (a Store / Xbox game Windows started, or any
    /// game started outside the launcher), until its programs have all closed - or the player says it's closed.
    /// </summary>
    private async Task TrackRunning(string id, GameMatch m, string how)
    {
        var start = DateTime.UtcNow;
        BeginSession(id, start);
        var stop = new CancellationTokenSource();
        lock (_mu) _stopWatch[id] = stop;
        Log.Info($"{id}: playing ({how})");
        var exited = Task.Run(async () =>
        {
            var quiet = 0;
            while (true)
            {
                try { await Task.Delay(2000, stop.Token); }
                catch (OperationCanceledException) { Log.Info($"{id}: marked as closed"); return; }
                if (m.Matches(Platform.Processes()).Count > 0) quiet = 0;
                else if (++quiet >= 2) return; // gone for two checks in a row (some games restart themselves)
            }
        });
        _ = exited.ContinueWith(_ => { lock (_mu) if (_stopWatch.TryGetValue(id, out var s) && s == stop) _stopWatch.Remove(id); });
        await TrackSession(id, start, exited);
    }

    /// <summary>"It's closed": stop counting a Store / Xbox game the launcher still thinks is running.</summary>
    public async Task MarkClosedAsync(string id)
    {
        lock (_mu)
            if (_stopWatch.Remove(id, out var cts)) cts.Cancel();
        await Task.Delay(300); // let the play-time tracker finish
    }

    /// <summary>
    /// Notices the player's games (and installed Ironite games) running even when they were started somewhere
    /// else - the Xbox app, the Start menu, a shortcut - so their opens and play time are counted too.
    /// </summary>
    private async Task WatchGames()
    {
        while (true)
        {
            await Task.Delay(10000);
            try
            {
                var cands = new List<(string Id, GameMatch M)>();
                lock (_mu)
                {
                    foreach (var cg in Settings.Custom ?? new())
                        if (!_running.ContainsKey(cg.Id) && !_starting.ContainsKey(cg.Id)) cands.Add((cg.Id, GameMatch.For(cg)));
                    foreach (var (id, rec) in Settings.Installs!)
                        if (!_running.ContainsKey(id) && !_starting.ContainsKey(id) && !_jobs.ContainsKey(id))
                            cands.Add((id, GameMatch.ForPath(Path.Combine(rec.Dir, rec.Exe))));
                }
                if (cands.Count == 0) continue;
                var procs = Platform.Processes();
                foreach (var (id, m) in cands)
                {
                    var (n, what) = m.Matches(procs);
                    if (n == 0) continue;
                    bool busy;
                    lock (_mu) busy = _running.ContainsKey(id) || _starting.ContainsKey(id);
                    if (!busy) _ = Task.Run(() => TrackRunning(id, m, "noticed " + what + " running"));
                }
            }
            catch (Exception ex) { Log.Error("watch games", ex); }
        }
    }

    // ---------------------------------------------------------------- checking GitHub

    public void RequestCheck()
    {
        if (_checkNow.CurrentCount == 0) _checkNow.Release();
    }

    /// <summary>Checks every game's repository (and the launcher's own) at start-up and every 30 minutes.</summary>
    private async Task UpdateLoop()
    {
        while (true)
        {
            try
            {
                await CheckAllAsync();
                AutoInstallUpdates();
            }
            catch (Exception ex) { Log.Error("update loop", ex); }
            await _checkNow.WaitAsync(TimeSpan.FromMinutes(30));
        }
    }

    public async Task CheckAllAsync()
    {
        lock (_mu)
        {
            if (Checking) return;
            Checking = true;
        }
        try
        {
            if (await GitHub.FetchCatalogAsync() && GitHub.CachedCatalogJson() is { } remote)
                lock (_mu)
                    if (remote != _baseJson)
                        Log.Info(TrySetCatalog(remote) ? "using the published game catalog" : "the published catalog couldn't be read");

            List<string> repos;
            lock (_mu)
                repos = new[] { GitHubService.LauncherRepo }
                    .Concat(Catalog.Games.Where(g => !string.IsNullOrEmpty(g.Repo) && !g.IsWeb).Select(g => g.Repo!))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            Exception? first = null;
            using var sem = new SemaphoreSlim(4);
            await Task.WhenAll(repos.Select(async r =>
            {
                await sem.WaitAsync();
                try { await GitHub.FetchLatestAsync(r); }
                catch (Exception ex)
                {
                    Log.Info($"check {r}: {ex.Message}");
                    lock (_mu) first ??= ex;
                }
                finally { sem.Release(); }
            }));

            lock (_mu)
            {
                LastCheck = DateTime.UtcNow;
                CheckError = "";
                CheckBusy = false;
                if (first is RateLimitedException) { CheckError = first.Message; CheckBusy = true; }
                else if (first != null) CheckError = "Couldn't reach GitHub to check for updates. Check your internet connection.";
            }
        }
        finally
        {
            lock (_mu) Checking = false;
        }
    }

    private void AutoInstallUpdates()
    {
        if (!AutoUpdate) return;
        var ids = States().Where(kv => kv.Value.Update && !kv.Value.Running && !kv.Value.Installing && kv.Value.Error == null)
            .Select(kv => kv.Key).ToList();
        foreach (var id in ids)
        {
            Log.Info($"updating {id} automatically");
            try { Install(id); } catch (Exception ex) { Log.Info($"auto-update {id}: {ex.Message}"); }
        }
    }

    public void SetAutoUpdate(bool on)
    {
        lock (_mu)
        {
            Settings.AutoUpdate = on;
            SaveSettings();
        }
        if (on) _ = Task.Run(AutoInstallUpdates);
    }

    // ---------------------------------------------------------------- the launcher updating itself

    public LauncherUpdateInfo? LauncherUpdate()
    {
        lock (_mu)
        {
            var r = GitHub.Get(GitHubService.LauncherRepo);
            if (r?.LauncherAsset() == null || !GitHubService.Newer(r.Version, App.VersionText)) return null;
            var u = new LauncherUpdateInfo { Version = r.Version, Notes = r.Body ?? "", Url = r.HtmlUrl ?? "" };
            if (_selfJob is { } j)
            {
                u.Error = j.Error;
                u.Downloading = j.Error == null;
                u.Installing = j.Error == null && j.Phase == "Installing";
                u.Done = Interlocked.Read(ref j.Done);
                u.Total = Interlocked.Read(ref j.Total);
                u.Progress = j.Progress;
            }
            return u;
        }
    }

    /// <summary>
    /// Like Bytehawk Launcher: downloads the new setup (with progress and Cancel), then runs it silently
    /// (/SILENT: only its progress window, no questions) and closes; setup installs the update and opens the
    /// launcher again.
    /// </summary>
    public void SelfUpdate()
    {
        InstallJob job;
        GhAsset asset;
        lock (_mu)
        {
            var r = GitHub.Get(GitHubService.LauncherRepo);
            if (r?.LauncherAsset() == null || !GitHubService.Newer(r.Version, App.VersionText))
                throw new InvalidOperationException("the launcher is up to date");
            if (_running.Count > 0) throw new InvalidOperationException("close your games before updating the launcher");
            if (_jobs.Values.Any(j => j.Error == null)) throw new InvalidOperationException("wait for downloads to finish before updating the launcher");
            if (_selfJob is { Error: null }) return;
            job = _selfJob = new InstallJob { Phase = "Downloading" };
            asset = r.LauncherAsset()!;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var file = await GameInstaller.DownloadAsync(asset, job);
                job.Phase = "Installing";
                await Task.Delay(800); // let the banner show "Installing"
                Log.Info($"starting launcher update {asset.Name}");
                (StartSetup ?? throw new InvalidOperationException("can't start the update"))(file);
            }
            catch (OperationCanceledException)
            {
                lock (_mu) if (_selfJob == job) _selfJob = null;
                Log.Info("self-update download cancelled");
            }
            catch (Exception ex)
            {
                Log.Error("self-update", ex);
                lock (_mu) job.Error = "Update failed: " + (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                    ? "the update was cancelled (administrator permission was not given)." : ex.Message);
            }
        });
    }

    /// <summary>Set by the app: releases the single-instance lock, starts setup and shuts down.</summary>
    public Action<string>? StartSetup { get; set; }

    public void CancelSelfUpdate()
    {
        lock (_mu)
            if (_selfJob is { Error: null } j && j.Phase != "Installing") j.Cancel.Cancel();
    }

    // ---------------------------------------------------------------- favorites, hidden, sessions, stats

    public void SetPref(string id, bool? favorite = null, bool? hidden = null)
    {
        lock (_mu)
        {
            if (Catalog.Games.All(g => g.Id != id)) throw new InvalidOperationException("unknown game");
            static List<string> Toggle(List<string>? list, string s, bool on)
            {
                var o = (list ?? new()).Where(x => x != s).ToList();
                if (on) o.Add(s);
                return o;
            }
            if (favorite is { } f) Settings.Favorites = Toggle(Settings.Favorites, id, f);
            if (hidden is { } h) Settings.Hidden = Toggle(Settings.Hidden, id, h);
            SaveSettings();
        }
    }

    public void SetSort(string sort)
    {
        lock (_mu) { Settings.Sort = sort; SaveSettings(); }
    }

    /// <summary>A game's recent play sessions, newest first.</summary>
    public List<PlaySession> Sessions(string id)
    {
        lock (_mu)
            return Settings.Stats.TryGetValue(id, out var s) && s.Sessions != null ? Enumerable.Reverse(s.Sessions).ToList() : new();
    }

    /// <summary>Play time per game per week (8 weeks, starting Monday, local time) for the Stats page.</summary>
    public (long[] WeekStarts, List<StatsRow> Rows, long Since) Stats()
    {
        const int weeks = 8;
        var today = DateTime.Today;
        var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var starts = Enumerable.Range(0, weeks).Select(i => new DateTimeOffset(thisWeek.AddDays(-7 * (weeks - 1 - i))).ToUnixTimeSeconds()).ToArray();
        var rows = new List<StatsRow>();
        long since = 0;
        lock (_mu)
        {
            foreach (var (id, s) in Settings.Stats)
            {
                var sessions = s.Sessions ?? new();
                if (s.PlaySeconds == 0 && sessions.Count == 0) continue;
                var row = new StatsRow { Id = id, Weeks = new long[weeks], Total = s.PlaySeconds, Sessions = sessions.Count };
                foreach (var se in sessions)
                {
                    if (since == 0 || se.Start < since) since = se.Start;
                    for (var i = weeks - 1; i >= 0; i--)
                        if (se.Start >= starts[i]) { row.Weeks[i] += se.Secs; break; }
                }
                rows.Add(row);
            }
        }
        return (starts, rows.OrderByDescending(r => r.Total).ToList(), since);
    }

    /// <summary>Makes a desktop shortcut that starts the game through the launcher.</summary>
    public async Task<string> CreateShortcutAsync(string id)
    {
        CatalogGame? g;
        string icon = "";
        lock (_mu)
        {
            g = Catalog.Games.FirstOrDefault(x => x.Id == id);
            if (Settings.Installs!.TryGetValue(id, out var rec)) icon = Path.Combine(rec.Dir, rec.Exe);
            if (CustomById(id) is { Path.Length: > 0 } cg) icon = cg.Path!;
        }
        if (g == null) throw new InvalidOperationException("unknown game");
        var exe = AppPaths.ExePath;
        if (icon == "") icon = exe;
        var path = await Platform.CreateShortcutAsync(Platform.SafeFileName(g.Title), exe, "--play " + id, icon, $"Play {g.Title} with Ironite Launcher");
        Log.Info($"made desktop shortcut {path}");
        return path;
    }

    // ---------------------------------------------------------------- the player's own games

    public static string NewCustomId() => "custom-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private static string Clip(string? s, int n)
    {
        s = (s ?? "").Trim();
        return s.Length > n ? s[..n] : s;
    }

    public sealed record Details(string Title, string Category, string Group, string Developer, string Players, string Description,
        string? AchSource = null, string? SteamAppId = null, string? XboxTitleId = null);

    private static void Apply(Details d, CustomGame cg)
    {
        if (Clip(d.Title, 60) is { Length: > 0 } t) cg.Title = t;
        cg.Category = NullIfEmpty(Clip(d.Category, 40));
        cg.Group = cg.Category == null ? NullIfEmpty(Clip(d.Group, 40)) : null;
        cg.Developer = NullIfEmpty(Clip(d.Developer, 60));
        cg.Players = NullIfEmpty(Clip(d.Players, 40));
        cg.Description = NullIfEmpty(Clip(d.Description, 1200));
        if (d.AchSource is { } src && src.Trim() is "steam" or "xbox" or "") cg.AchSource = NullIfEmpty(src.Trim());
        if (d.SteamAppId != null) cg.SteamAppId = NullIfEmpty(Regex.Match(d.SteamAppId, @"\d+").Value); // accepts a store link too
        if (d.XboxTitleId != null) cg.XboxTitleId = NullIfEmpty(Regex.Match(d.XboxTitleId, @"\d+").Value);
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>Adds a game program to the launcher. Returns its id.</summary>
    public string AddCustom(string title, string path, Details d)
    {
        path = path.Trim().Trim('"').Trim();
        if (path == "") throw new InvalidOperationException("choose the game's program first");
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException(@"use the full path to the game's program (for example C:\Games\MyGame\MyGame.exe)");
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("choose the game's program (an .exe file)");
        if (!File.Exists(path)) throw new InvalidOperationException("that program wasn't found");
        title = Clip(string.IsNullOrWhiteSpace(title) ? GuessTitle(path) : title, 60);
        CustomGame cg;
        lock (_mu)
        {
            if ((Settings.Custom ?? new()).FirstOrDefault(c => !string.IsNullOrEmpty(c.Path) && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)) is { } dup)
                throw new InvalidOperationException(dup.Title + " is already in the launcher");
            cg = new CustomGame { Id = NewCustomId(), Title = title, Path = path, AddedAt = GameInstaller.Now() };
            Apply(d with { Title = "" }, cg);
            (Settings.Custom ??= new()).Add(cg);
            SaveSettings();
            MergeCustom();
        }
        _ = Task.Run(() =>
        {
            // The program's own icon, for its tile.
            if (Platform.ExeIconPng(path) is { } png) SaveIcon(cg.Id, png);
            else Log.Info($"icon for {cg.Id}: the program has no icon");
        });
        Log.Info($"added {cg.Id}: {path}");
        return cg.Id;
    }

    /// <summary>Adds a Microsoft Store / Xbox app game to the launcher. Returns its id.</summary>
    public string AddStoreApp(string title, string appId, string location, Details d)
    {
        if (!appId.Contains('!')) throw new InvalidOperationException("choose a game from the list");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("give the game a name");
        CustomGame cg;
        lock (_mu)
        {
            if ((Settings.Custom ?? new()).FirstOrDefault(c => string.Equals(c.AppId, appId, StringComparison.OrdinalIgnoreCase)) is { } dup)
                throw new InvalidOperationException(dup.Title + " is already in the launcher");
            cg = new CustomGame { Id = NewCustomId(), Title = Clip(title, 60), AppId = appId, Location = NullIfEmpty(location ?? ""), AddedAt = GameInstaller.Now() };
            Apply(d with { Title = "" }, cg);
            (Settings.Custom ??= new()).Add(cg);
            SaveSettings();
            MergeCustom();
        }
        _ = Task.Run(() =>
        {
            if (StoreApps.Art(location) is { } png) SaveIcon(cg.Id, png);
            else Log.Info($"art for {cg.Id}: no logo found");
        });
        Log.Info($"added {cg.Id}: {appId} ({location})");
        return cg.Id;
    }

    private void SaveIcon(string id, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CustomIcons);
            File.WriteAllBytes(Path.Combine(AppPaths.CustomIcons, id + ".png"), png);
            lock (_mu) MergeCustom(); // redraw with the icon
        }
        catch (Exception ex) { Log.Error($"icon for {id}", ex); }
    }

    public void UpdateCustom(string id, Details d)
    {
        if (string.IsNullOrWhiteSpace(d.Title)) throw new InvalidOperationException("give the game a name");
        lock (_mu)
        {
            var cg = CustomById(id) ?? throw new InvalidOperationException("unknown game");
            Apply(d, cg);
            SaveSettings();
            MergeCustom();
        }
    }

    /// <summary>Saves cover art for one of the player's games (null removes it). The picture is scaled to at most 1200 px.</summary>
    public void SetCustomArt(string id, byte[]? data, string ext)
    {
        lock (_mu)
        {
            var cg = CustomById(id) ?? throw new InvalidOperationException("unknown game");
            if (!string.IsNullOrEmpty(cg.Art)) GameInstaller.TryDelete(Path.Combine(AppPaths.CustomIcons, cg.Art));
            cg.Art = null;
            if (data != null)
            {
                if (data.Length > 12 << 20) throw new InvalidOperationException("that picture is too big (12 MB at most)");
                Directory.CreateDirectory(AppPaths.CustomIcons);
                var name = $"{cg.Id}-art-{GameInstaller.Now()}{ext}";
                File.WriteAllBytes(Path.Combine(AppPaths.CustomIcons, name), data);
                cg.Art = name;
            }
            SaveSettings();
            MergeCustom();
        }
    }

    public void RemoveCustom(string id)
    {
        lock (_mu) RemoveCustomLocked(id);
    }

    private void RemoveCustomLocked(string id)
    {
        if (_running.ContainsKey(id)) throw new InvalidOperationException("close the game before removing it");
        var cg = CustomById(id) ?? throw new InvalidOperationException("unknown game");
        Settings.Custom!.Remove(cg);
        Settings.Stats.Remove(id);
        _launchErr.Remove(id);
        SaveSettings();
        MergeCustom();
        GameInstaller.TryDelete(Path.Combine(AppPaths.CustomIcons, id + ".png"));
        if (!string.IsNullOrEmpty(cg.Art)) GameInstaller.TryDelete(Path.Combine(AppPaths.CustomIcons, cg.Art));
    }

    public HashSet<string> AddedStoreAppIds()
    {
        lock (_mu)
            return (Settings.Custom ?? new()).Where(c => c.IsStore).Select(c => c.AppId!.ToLowerInvariant()).ToHashSet();
    }

    public CustomGame? Custom(string id) { lock (_mu) return CustomById(id); }

    /// <summary>Turns "C:\Games\SuperTuxKart\supertuxkart_x64.exe" into a name.</summary>
    public static string GuessTitle(string path)
    {
        var b = Path.GetFileNameWithoutExtension(path.Trim().Trim('"'));
        foreach (var junk in new[] { "_x64", "-x64", "x64", "_win64", "-win64", "_win32", "-Win64-Shipping", "_launcher", "Launcher" })
            if (b.EndsWith(junk, StringComparison.OrdinalIgnoreCase) && b.Length > junk.Length) b = b[..^junk.Length];
        b = Regex.Replace(b, @"[_\-.]+", " ");
        b = Regex.Replace(b, @"(\p{Ll})(\p{Lu})", "$1 $2"); // "MyCoolGame" -> "My Cool Game"
        b = string.Join(" ", b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (b == "") return "My game";
        return char.ToUpper(b[0]) + b[1..];
    }

    // ---------------------------------------------------------------- achievements

    public (string Profile, string? Key) AchievementAccounts()
    {
        lock (_mu) return (Settings.SteamProfile ?? "", Settings.XblKey);
    }

    public void SetSteamProfile(string value)
    {
        value = value.Trim();
        if (value != "" && AchievementService.SteamProfilePath(value) == "")
            throw new InvalidOperationException("that doesn't look like a Steam profile link or ID");
        lock (_mu) { Settings.SteamProfile = NullIfEmpty(value); SaveSettings(); }
        AchievementService.ClearCache();
    }

    public void SetXblKey(string value)
    {
        lock (_mu) { Settings.XblKey = NullIfEmpty(value.Trim()); SaveSettings(); }
        AchievementService.ClearCache();
    }

    public async Task<List<AchievementResult>> AchievementsAsync(bool force)
    {
        List<CustomGame> games;
        string profile;
        string? key;
        lock (_mu)
        {
            games = (Settings.Custom ?? new()).Where(c => !string.IsNullOrEmpty(c.AchSource)).ToList();
            profile = Settings.SteamProfile ?? "";
            key = Settings.XblKey;
        }
        // Ironite games first: read straight from the files the games keep up to date.
        var ironite = IroniteAchievements();
        using var sem = new SemaphoreSlim(4);
        var results = await Task.WhenAll(games.Select(async cg =>
        {
            await sem.WaitAsync();
            try { return await AchievementService.ForGameAsync(cg, profile, key, force); }
            finally { sem.Release(); }
        }));
        return ironite.Concat(results.Where(r => r != null).Select(r => r!)).ToList();
    }

    /// <summary>Achievements of every Ironite game in the catalog that has shared some (played at least once).</summary>
    public List<AchievementResult> IroniteAchievements()
    {
        List<string> ids;
        lock (_mu) ids = Catalog.Games.Where(g => !g.IsLocal).Select(g => g.Id).ToList();
        return ids.Select(AchievementService.Ironite).Where(r => r != null).Select(r => r!).ToList();
    }

    /// <summary>Suggests where a game's achievements come from.</summary>
    public async Task<(string Source, string Id, string Found)> DetectAchievementsAsync(string id)
    {
        CustomGame? cg;
        string? key;
        lock (_mu) { cg = CustomById(id); key = Settings.XblKey; }
        if (cg == null) throw new InvalidOperationException("unknown game");
        if (cg.IsStore)
        {
            var (t, name) = await AchievementService.DetectXboxTitleAsync(cg.Location, cg.Title, key);
            if (t != "") return ("xbox", t, name);
        }
        else
        {
            var (a, name) = AchievementService.DetectSteamApp(cg.Path);
            if (a != "") return ("steam", a, name);
        }
        return ("", "", "");
    }
}
