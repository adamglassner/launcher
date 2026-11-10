using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ironite.Launcher.Models;
using Ironite.Launcher.Services;
using Ironite.Launcher.Views;
using static Ironite.Launcher.Views.Ui;

namespace Ironite.Launcher;

/// <summary>
/// The launcher window, in Ironite's look: a sidebar with the library's categories and pages, and the main
/// area with the launcher-update banner, the Library (search, featured carousel, tiles) or a page. It redraws
/// from <see cref="LauncherCore.States"/> when something visible changes. The parts are in the partial files
/// MainWindow.Library.cs, MainWindow.Dialogs.cs and MainWindow.Pages.cs.
/// </summary>
public partial class MainWindow : Window
{
    private static LauncherCore Core => App.Core;

    private string _view = "all";
    private Dictionary<string, GameState> _states = new();
    private string _sig = "";
    private string _bannerSig = "";
    private string _checkSig = "";
    private int _catVersion = -1;
    private readonly HashSet<string> _busy = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(3800) };

    private readonly Border _banner = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _library = new();
    private readonly StackPanel _page = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, Button> _nav = new();
    private Border? _instBadge;

    private static readonly (string Id, string Icon, string Label)[] NavItems =
    {
        ("all", "▦", "Library"), ("installed", "✓", "Installed"), ("card", "♠", "Card Games"), ("hidden", "⌕", "Hidden Object"),
        ("arcade", "◆", "Arcade"), ("puzzle", "▣", "Puzzle"), ("word", "A", "Word/Trivia"), ("sim", "⌂", "Simulation"),
        ("idle", "↻", "Idle"), ("edu", "✎", "Education"), ("board", "♞", "Board Games"), ("custom", "⊕", "Non-Ironite Games"),
        ("-", "", ""),
        ("friends", "☺", "Friends"), ("achievements", "★", "Achievements"), ("stats", "▤", "Stats"), ("news", "✦", "What's New"), ("about", "i", "About"),
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Ironite Launcher {App.VersionText}";
        VerSideText.Text = "v" + App.VersionText;
        BrandSub.Text = Track("LAUNCHER");
        VerSide.Click += (_, _) => Go("news");

        Main.Children.Add(_banner);
        Main.Children.Add(_library);
        Main.Children.Add(_page);
        BuildNav();
        BuildLibraryShell();

        Veil.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == Veil) CloseModal(); };
        SizeChanged += (_, _) => Dlg.MaxHeight = Math.Max(300, ActualHeight * 0.9);
        PreviewKeyDown += OnKey;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh(force: true);
            _timer.Start();
            StartCarousel();
            if (JsonStore.LastLoadError is { } err) ShowToast(err);
        };
        SourceInitialized += (_, _) => DarkTitleBar();
    }

    // ---------------------------------------------------------------- window

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void DarkTitleBar()
    {
        try
        {
            var on = 1;
            var h = new WindowInteropHelper(this).Handle;
            if (DwmSetWindowAttribute(h, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(h, 19, ref on, sizeof(int));
            var caption = 0x00191714; // the sidebar's colour, as COLORREF (0x00BBGGRR)
            DwmSetWindowAttribute(h, 35, ref caption, sizeof(int));
        }
        catch { /* older Windows */ }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Games that are still open and downloads in progress keep the launcher running in the background
        // (so play time and installs are recorded); it quits after. The window just hides.
        if (!App.IsExiting && !Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            CloseModal();
            Hide();
            Log.Info(Core.GamesRunning() || Core.Busy() ? "window closed; staying in the background for a game or download" : "window closed");
        }
        base.OnClosing(e);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Veil.Visibility == Visibility.Visible) { CloseModal(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _library.Visibility == Visibility.Visible)
        {
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
        }
    }

    public void ShowToast(string msg)
    {
        ToastText.Text = msg;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void Fail(Exception ex) => ShowToast(Sentence(ex.Message));

    // ---------------------------------------------------------------- navigation

    private void BuildNav()
    {
        foreach (var (id, icon, label) in NavItems)
        {
            if (id == "-")
            {
                Nav.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(8, 10, 8, 10) });
                continue;
            }
            var row = new DockPanel { LastChildFill = true };
            var ic = T(icon, 15, Res("Ember2"));
            ic.Width = 18;
            ic.TextAlignment = TextAlignment.Center;
            ic.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(ic, Dock.Left);
            row.Children.Add(ic);
            if (id == "installed")
            {
                _instBadge = NavBadge("0", "#2E5FC489", "#8fe0b0", "#665FC489");
                _instBadge.Visibility = Visibility.Collapsed;
                DockPanel.SetDock(_instBadge, Dock.Right);
                row.Children.Add(_instBadge);
            }
            if (id == "friends")
            {
                var soon = NavBadge(Track("SOON"), "#0FFFFFFF", "#9a9fab", "#2f3542", 9);
                DockPanel.SetDock(soon, Dock.Right);
                row.Children.Add(soon);
            }
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            row.Children.Add(text);
            var b = new Button { Style = Style("NavButton"), Content = row, Tag = id == _view ? "on" : null };
            b.Click += (_, _) => Go(id);
            _nav[id] = b;
            Nav.Children.Add(b);
        }
    }

    private static Border NavBadge(string text, string bg, string fg, string line, double size = 11) => new()
    {
        Background = B(bg), BorderBrush = B(line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999),
        Padding = new Thickness(8, 1, 8, 1), MinWidth = 22, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, FontFamily = Display, Foreground = B(fg), TextAlignment = TextAlignment.Center },
    };

    private static readonly HashSet<string> Pages = new() { "friends", "achievements", "stats", "news", "about" };

    public void Go(string view)
    {
        _view = view;
        foreach (var (id, b) in _nav) b.Tag = id == view ? "on" : null;
        var page = Pages.Contains(view);
        _library.Visibility = page ? Visibility.Collapsed : Visibility.Visible;
        _page.Visibility = page ? Visibility.Visible : Visibility.Collapsed;
        MainScroll.ScrollToTop();
        switch (view)
        {
            case "friends": RenderFriends(); break;
            case "achievements": _ = RenderAchievementsAsync(false); break;
            case "stats": RenderStats(); break;
            case "news": RenderNews(); break;
            case "about": RenderAbout(); break;
            default: RenderAll(); break;
        }
    }

    // ---------------------------------------------------------------- refresh

    /// <summary>Re-reads the launcher's state and redraws what changed (play time counts in whole minutes, so a running game doesn't redraw every tick).</summary>
    public void Refresh(bool force = false)
    {
        var old = _states;
        _states = Core.States();
        foreach (var (id, s) in _states)
            if (s.LaunchError is { } le && (!old.TryGetValue(id, out var o) || o.LaunchError != le) && !force && old.Count > 0)
                ShowToast(le);

        var sig = string.Join(";", _states.Select(kv => kv.Key + "=" + kv.Value.Signature())) + "|" + Core.AutoUpdate + "|" + Core.CatalogVersion + "|" + string.Join(",", _busy);
        var changed = force || sig != _sig;
        _sig = sig;
        if (Core.CatalogVersion != _catVersion) { _catVersion = Core.CatalogVersion; changed = true; }

        var lu = Core.LauncherUpdate();
        var bsig = lu?.Signature() + "|" + _bannerHidden;
        if (force || bsig != _bannerSig) { _bannerSig = bsig; RenderBanner(lu); }

        var csig = $"{Core.Checking}|{Core.LastCheck}|{Core.CheckError}|{(Core.LastCheck is { } t ? Since(t) : "")}";
        if (force || csig != _checkSig) { _checkSig = csig; RenderCheck(); }

        if (changed) RenderAll();
        else if (_view == "about") RenderAbout();
    }

    private void RenderAll()
    {
        UpdateNavBadge();
        if (Pages.Contains(_view))
        {
            if (_view == "about") RenderAbout();
            return;
        }
        RenderLibrary();
        if (_modalId != null && Veil.Visibility == Visibility.Visible) Details(_modalId);
    }

    private GameState St(string id) => _states.TryGetValue(id, out var s) ? s : new GameState();
    private CatalogGame? Game(string id) => Core.Catalog.Games.FirstOrDefault(g => g.Id == id);
    private List<CatalogGame> Games => Core.Catalog.Games;

    // ---------------------------------------------------------------- launcher update banner

    // Launcher updates work like Bytehawk Launcher's: a banner offers the new version; Update now downloads its
    // setup (with progress, and Cancel), then runs it showing only its progress window. The launcher closes,
    // setup installs the update and opens the launcher again.
    private string _bannerHidden = "";

    private void RenderBanner(LauncherUpdateInfo? u)
    {
        if (u == null || (_bannerHidden == u.Version && !u.Downloading && u.Error == null))
        {
            _banner.Visibility = Visibility.Collapsed;
            return;
        }
        _banner.Visibility = Visibility.Visible;
        _banner.Background = new LinearGradientBrush(Color.FromArgb(0x29, 0xe7, 0xc3, 0x5a), Color.FromArgb(0x14, 0xe8, 0x87, 0x3a), 0);
        _banner.BorderBrush = B("#73e7c35a");
        _banner.BorderThickness = new Thickness(1);
        _banner.CornerRadius = new CornerRadius(14);
        _banner.Padding = new Thickness(18, 14, 18, 14);
        _banner.Margin = new Thickness(0, 0, 0, 22);

        var title = $"Ironite Launcher {u.Version} is available";
        UIElement line = T($"You have {App.VersionText}.", 12.5, B("#cfc6b4"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (u.Installing)
        {
            title = $"Installing Ironite Launcher {u.Version}…";
            line = T("The launcher will close, and the installer will open it again when it’s done. If Windows asks for permission, click Yes.", 12.5, B("#cfc6b4"), wrap: TextWrapping.Wrap);
        }
        else if (u.Downloading)
        {
            title = $"Downloading Ironite Launcher {u.Version}…";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Bar(u.Progress, 180, 6, new LinearGradientBrush(Color.FromRgb(0xf6, 0xb5, 0x6b), Color.FromRgb(0xf3, 0xd2, 0x7a), 0), B("#1AFFFFFF")));
            row.Children.Add(T($"  {u.Progress}%" + (u.Total > 0 ? $" · {Size(u.Done)} of {Size(u.Total)}" : ""), 12.5, B("#cfc6b4")));
            line = row;
            actions.Children.Add(Btn("Cancel", "GhostButton", () => Core.CancelSelfUpdate(), small: true));
        }
        else
        {
            if (u.Error != null) line = T(u.Error, 12.5, B("#cfc6b4"), wrap: TextWrapping.Wrap);
            var up = Play("⟳  Update now", "", () =>
            {
                try
                {
                    if (u.Error != null) Core.Dismiss("launcher");
                    Core.SelfUpdate();
                    Refresh(force: true);
                }
                catch (Exception ex) { Fail(ex); }
            }, small: true);
            up.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(up);
            var notes = Btn("Release notes", "GhostButton", () => ReleaseDialog($"Ironite Launcher {u.Version}", u.Notes), small: true);
            notes.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(notes);
            var x = Btn(T("✕", 15, Res("Muted")), "BareButton", () => { _bannerHidden = u.Version; Refresh(force: true); }, "Hide until next time");
            x.Padding = new Thickness(6, 4, 6, 4);
            actions.Children.Add(x);
        }
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        txt.Children.Add(T(title, 16, B("#f6e3ad"), FontWeights.Bold, true));
        txt.Children.Add(line);
        var grid = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        grid.Children.Add(actions);
        grid.Children.Add(txt);
        _banner.Child = grid;
    }

    // ---------------------------------------------------------------- "Checked GitHub 3 min ago · Check"

    private void RenderCheck()
    {
        CheckLine.Children.Clear();
        void Text(string s) => CheckLine.Children.Add(T(s, 12, Res("Muted")));
        void Warn(string tip) { var w = T("⚠ ", 12, B("#f1d78a")); w.ToolTip = tip; CheckLine.Children.Add(w); }
        void Check(string label) => CheckLine.Children.Add(Link(label, () => { Core.RequestCheck(); RenderCheckNow(); }, 12));
        if (Core.Checking || Core.LastCheck == null)
        {
            CheckLine.Children.Add(Spinner());
            Text("Checking GitHub…");
        }
        else if (Core.CheckError.Length > 0 && Core.CheckBusy)
        {
            Warn(Core.CheckError);
            Text("GitHub is busy · will retry");
        }
        else if (Core.CheckError.Length > 0)
        {
            Warn(Core.CheckError);
            Text("Offline · ");
            Check("Retry");
        }
        else
        {
            Text($"Checked GitHub {Since(Core.LastCheck.Value)} · ");
            Check("Check");
        }
    }

    private void RenderCheckNow()
    {
        CheckLine.Children.Clear();
        CheckLine.Children.Add(Spinner());
        CheckLine.Children.Add(T("Checking GitHub…", 12, Res("Muted")));
    }

    private void UpdateNavBadge()
    {
        if (_instBadge == null) return;
        var n = InstalledGames().Count(g => g.IsWeb || St(g.Id).Installed);
        var ups = Updatable().Count;
        _instBadge.Child = new TextBlock
        {
            Text = n.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, FontFamily = Display, TextAlignment = TextAlignment.Center,
            Foreground = B(ups > 0 ? "#f1d78a" : "#8fe0b0"),
        };
        _instBadge.Background = B(ups > 0 ? "#2EE7C35A" : "#2E5FC489");
        _instBadge.BorderBrush = B(ups > 0 ? "#80E7C35A" : "#665FC489");
        _instBadge.ToolTip = ups > 0 ? $"{ups} update{(ups > 1 ? "s" : "")} available" : null;
        _instBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
