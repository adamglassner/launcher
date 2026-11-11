using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Ironite.Launcher.Services;

namespace Ironite.Launcher;

public partial class App : Application
{
    /// <summary>The app version (from the assembly, e.g. "3.0.0"). Set at build time by build.ps1 / CI.</summary>
    public static string VersionText { get; } =
        (typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "3.0.0").Split('+')[0];

    /// <summary>When this build was made (the exe's time stamp), shown on the About page.</summary>
    public static string BuildText { get; } = File.Exists(AppPaths.ExePath) ? File.GetLastWriteTimeUtc(AppPaths.ExePath).ToString("yyyyMMdd.HHmm") : "dev";

    /// <summary>The same lock launcher 2.x took, so a 2.x helper (and the installer) can see the launcher is running.</summary>
    public const string MutexName = @"Local\IroniteLauncher";

    public static LauncherCore Core { get; } = new();
    private static Services.Friends.FriendsService? _friends;
    /// <summary>The player's Ironite account and friends (made on first use, so the tour can pick a test server first).</summary>
    public static Services.Friends.FriendsService Friends => _friends ??= new();

    /// <summary>Starts Friends: tells it what the player is playing, and signs back in.</summary>
    public static void StartFriends()
    {
        Friends.NowPlaying = () =>
        {
            var id = Core.States().FirstOrDefault(kv => kv.Value.Running).Key;
            return (id, id == null ? null : Core.Find(id)?.Title);
        };
        Friends.GamesSnapshot = () => Core.SharedGamesAsync();
        _ = Friends.StartAsync();
    }
    public static string PipeName => $"IroniteLauncher.{Process.GetCurrentProcess().SessionId}.{Environment.UserName}";

    private Mutex? _singleInstance;
    private CancellationTokenSource? _pipeStop;
    private DispatcherTimer? _idle;
    private DateTime _started = DateTime.UtcNow;
    private bool _exiting;

    public Ironite.Launcher.MainWindow? LauncherWindow => MainWindow as Ironite.Launcher.MainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled (domain)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Unobserved task", args.Exception); args.SetObserved(); };

        // Headless self-test used by CI: IroniteLauncher.exe --self-test <result-file>
        var selfTest = Array.IndexOf(e.Args, "--self-test");
        if (selfTest >= 0)
        {
            var output = selfTest + 1 < e.Args.Length ? e.Args[selfTest + 1] : Path.Combine(Path.GetTempPath(), "ironite-selftest.txt");
            _ = Dispatcher.InvokeAsync(async () => Quit(await SelfTest.RunAsync(output)));
            return;
        }
        // Screenshot tour used by CI: IroniteLauncher.exe --tour <folder>
        var tour = Array.IndexOf(e.Args, "--tour");
        if (tour >= 0)
        {
            var dir = tour + 1 < e.Args.Length ? e.Args[tour + 1] : Path.Combine(Path.GetTempPath(), "ironite-tour");
            _ = Dispatcher.InvokeAsync(async () => Quit(await Tour.RunAsync(dir)));
            return;
        }

        var p = Array.IndexOf(e.Args, "--play");
        var playId = p >= 0 && p + 1 < e.Args.Length ? e.Args[p + 1] : null; // started from a game's desktop shortcut

        _singleInstance = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Log.Start(append: true);
            Log.Info($"Ironite Launcher {VersionText}: second start");
            if (HandOff(playId)) { Shutdown(); return; }
            // The other launcher is closing: wait for it to let go.
            var got = false;
            for (var i = 0; i < 40 && !got; i++)
            {
                try { got = _singleInstance.WaitOne(200); }
                catch (AbandonedMutexException) { got = true; }
            }
            if (!got)
            {
                MessageBox.Show("Ironite Launcher is already running but not responding. Restart your PC if it doesn't open.", "Ironite Launcher",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }
        }
        else Log.Start(append: false);

        Log.Info($"Ironite Launcher {VersionText} ({BuildText}) starting");
        try
        {
            AppPaths.MigrateOldFolders();
            AppPaths.EnsureCreated();
            Core.Load();
        }
        catch (Exception ex)
        {
            Log.Error("start-up", ex);
            MessageBox.Show("Ironite Launcher couldn't start:\n" + ex.Message, "Ironite Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Core.StartSetup = file => Dispatcher.Invoke(() => RunSetupAndExit(file));
        Core.StartBackground();
        StartFriends();
        StartPipeServer();

        if (playId != null)
        {
            // Started from a desktop shortcut: start the game and stay in the background (no window) to count
            // its play time, then quit.
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(500);
                Play(playId);
            });
        }
        else ShowWindow();

        // The launcher keeps running in the background while a game is open or a download is running (so play
        // time and installs are recorded), and quits once the window is closed and nothing is left to do.
        _idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idle.Tick += (_, _) =>
        {
            if (_exiting || DateTime.UtcNow - _started < TimeSpan.FromSeconds(5)) return;
            if (LauncherWindow is { IsVisible: true }) return;
            if (Core.GamesRunning() || Core.Busy()) return;
            Log.Info("window closed and nothing running, exiting");
            Quit();
        };
        _idle.Start();
    }

    public void ShowWindow()
    {
        if (LauncherWindow == null)
        {
            var w = new Ironite.Launcher.MainWindow();
            MainWindow = w;
            w.Show();
        }
        else
        {
            var w = LauncherWindow;
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.Topmost = true;
            w.Topmost = false;
            w.Focus();
            w.Refresh(force: true);
        }
    }

    private void Play(string id)
    {
        try
        {
            Core.Launch(id);
            LauncherWindow?.Refresh(force: true);
        }
        catch (Exception ex)
        {
            Log.Info($"shortcut: {id}: {ex.Message}");
            MessageBox.Show("Couldn't start the game: " + ex.Message, "Ironite Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------------- second start

    /// <summary>Asks the running launcher to show its window (or start a game). False if it isn't answering.</summary>
    private static bool HandOff(string? playId)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe) { AutoFlush = true };
            using var r = new StreamReader(pipe);
            w.WriteLine(playId != null ? "play " + playId : "show");
            var answer = r.ReadLine();
            Log.Info($"handed off to the running launcher ({answer})");
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"the running launcher isn't answering ({ex.Message})");
            return false;
        }
    }

    private void StartPipeServer()
    {
        _pipeStop = new CancellationTokenSource();
        var ct = _pipeStop.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    using var r = new StreamReader(server);
                    await using var w = new StreamWriter(server) { AutoFlush = true };
                    var line = (await r.ReadLineAsync(ct)) ?? "";
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (line.StartsWith("play ", StringComparison.Ordinal)) Play(line[5..].Trim());
                        else ShowWindow();
                    });
                    await w.WriteLineAsync("ok");
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log.Error("pipe", ex); await Task.Delay(500); }
            }
        });
    }

    // ---------------------------------------------------------------- self-update

    /// <summary>
    /// Bytehawk-style update: run the downloaded setup silently (only its progress window), and close so it can
    /// replace the launcher; setup opens the launcher again (/RELAUNCH=1).
    /// </summary>
    private void RunSetupAndExit(string setup)
    {
        ReleaseSingleInstance();
        _pipeStop?.Cancel();
        try
        {
            Process.Start(new ProcessStartInfo(setup)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1",
                WorkingDirectory = Path.GetDirectoryName(setup),
            });
        }
        catch
        {
            // Cancelled at the Windows prompt (or couldn't start): keep running.
            _singleInstance = new Mutex(true, MutexName, out _);
            StartPipeServer();
            throw;
        }
        Log.Info($"started launcher update {setup}; exiting");
        Quit();
    }

    /// <summary>Set while the launcher is quitting, so closing the window really closes it.</summary>
    public static bool IsExiting { get; private set; }

    public void Quit(int code = 0)
    {
        _exiting = true;
        IsExiting = true;
        Shutdown(code);
    }

    public void ReleaseSingleInstance()
    {
        try { _singleInstance?.ReleaseMutex(); } catch { /* not owned */ }
        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled (UI)", e.Exception);
        if (!_exiting)
            MessageBox.Show($"Something went wrong:\n{e.Exception.Message}\n\nDetails were written to {AppPaths.Log}.",
                "Ironite Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        IsExiting = true;
        _pipeStop?.Cancel();
        _friends?.GoOffline();
        ReleaseSingleInstance();
        base.OnExit(e);
    }
}
