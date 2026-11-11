using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ironite.Launcher.Models;
using Ironite.Launcher.Services;
using Ironite.Launcher.Views;
using static Ironite.Launcher.Views.Ui;

namespace Ironite.Launcher;

/// <summary>The Library: search and sorting, the featured carousel, and the categories of game tiles.</summary>
public partial class MainWindow
{
    private sealed record Featured(string Id, string Kicker, string Title, string Small);

    private static readonly Featured[] FeaturedGames =
    {
        new("klondike", "FEATURED · CARD GAMES", "KLONDIKE", "GOLD RUSH SOLITAIRE"),
        new("rewindlife", "FREE DEMO · SIMULATION", "REWIND LIFE", "MAPLE GROVE, 1986"),
        new("euchre", "FEATURED · CARD GAMES", "EUCHRE", "A KITCHEN-TABLE CLASSIC"),
        new("oldmaid", "FEATURED · CARD GAMES", "OLD MAID", "A VICTORIAN PARLOR GAME"),
        new("spades", "NEW · CARD GAMES", "SPADES", "A BROOKLYN ROOFTOP CARD GAME"),
        new("cribbage", "NEW · CARD GAMES", "CRIBBAGE", "A LIGHTHOUSE CARD GAME"),
        new("ginrummy", "FEATURED · CARD GAMES", "GIN RUMMY", "A CLUB CAR CARD GAME"),
        new("hearts", "FEATURED · CARD GAMES", "HEARTS", "A PARIS CAFÉ CARD GAME"),
        new("mahjong", "NEW · BOARD GAMES", "MAHJONG", "JADE MOON GARDEN"),
        new("solarsiege", "FEATURED · ARCADE", "SOLAR SIEGE", "DEFEND THE SOLAR SYSTEM"),
    };

    internal readonly TextBox _search = new() { MinWidth = 220, Height = 40, Padding = new Thickness(38, 0, 34, 0), FontSize = 14 };
    private readonly ComboBox _sort = new() { Width = 170, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed, ToolTip = "Sort" };
    private readonly CheckBox _showHid = new() { Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Grid _hero = new() { MinHeight = 320, Margin = new Thickness(0, 0, 0, 30) };
    private readonly StackPanel _cats = new();
    private Button? _clear;
    private string _query = "";
    private bool _showHidden;
    private string _sortBy = "recent";

    private int _slide;
    private bool _heroHover;
    private readonly DispatcherTimer _slideTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly List<Grid> _slides = new();
    private readonly List<Border> _dots = new();

    private void BuildLibraryShell()
    {
        _sortBy = Core.Settings.Sort is "recent" or "most" or "az" ? Core.Settings.Sort : "recent";

        // Search box with its magnifier, placeholder and clear button.
        var hint = T("Search games", 14, Res("Muted"));
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(41, 0, 0, 0); // where typed text starts
        hint.VerticalAlignment = VerticalAlignment.Center;
        var glass = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M7,1 A6,6 0 1 1 6.99,1 Z M11.5,11.5 L15,15"), Stroke = B("#8b919c"), StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 16, Height = 16, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(13, 0, 0, 0), IsHitTestVisible = false,
        };
        _clear = Btn(T("✕", 13, Res("Muted")), "BareButton", () => { _search.Text = ""; _search.Focus(); }, "Clear");
        _clear.HorizontalAlignment = HorizontalAlignment.Right;
        _clear.Margin = new Thickness(0, 0, 10, 0);
        _clear.VerticalAlignment = VerticalAlignment.Center;
        _clear.Visibility = Visibility.Collapsed;
        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _query = _search.Text.Trim().ToLowerInvariant();
            _clear.Visibility = _query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderLibrary();
        };
        var search = new Grid { MaxWidth = 420, MinWidth = 220, Width = 420 };
        search.Children.Add(_search);
        search.Children.Add(glass);
        search.Children.Add(hint);
        search.Children.Add(_clear);

        foreach (var (v, label) in new[] { ("recent", "Recently played"), ("most", "Most played"), ("az", "A–Z") })
            _sort.Items.Add(new ComboBoxItem { Content = label, Tag = v });
        _sort.SelectedIndex = _sortBy switch { "most" => 1, "az" => 2, _ => 0 };
        _sort.SelectionChanged += (_, _) =>
        {
            _sortBy = (string)((ComboBoxItem)_sort.SelectedItem).Tag;
            Core.SetSort(_sortBy);
            RenderLibrary();
        };
        _showHid.Checked += (_, _) => { _showHidden = true; RenderLibrary(); };
        _showHid.Unchecked += (_, _) => { _showHidden = false; RenderLibrary(); };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        bar.Children.Add(search);
        bar.Children.Add(_sort);
        bar.Children.Add(_showHid);
        _library.Children.Add(bar);
        _library.Children.Add(_hero);
        _library.Children.Add(_cats);

        _hero.MouseEnter += (_, _) => _heroHover = true;
        _hero.MouseLeave += (_, _) => _heroHover = false;
        RoundClip(_hero, 18);
    }

    private void StartCarousel()
    {
        _slideTimer.Tick += (_, _) =>
        {
            if (!_heroHover && IsActive && _view == "all" && _slides.Count > 1) ShowSlide(_slide + 1);
        };
        _slideTimer.Start();
    }

    // ---------------------------------------------------------------- game state -> buttons and labels

    private bool Avail(CatalogGame g) => St(g.Id).Available;
    private bool Visible(CatalogGame g) => _showHidden || !St(g.Id).Hidden;
    private bool Matches(CatalogGame g) => _query.Length == 0 ||
        string.Join(" ", new[] { g.Title, g.Subtitle, g.Group }.Where(x => !string.IsNullOrEmpty(x))).ToLowerInvariant().Contains(_query);

    private List<Featured> FeaturedNow() =>
        FeaturedGames.Where(f => Game(f.Id) is { } g && Avail(g) && !St(f.Id).Hidden).ToList();

    /// <summary>Web games need no install, so they're always in Installed.</summary>
    private List<CatalogGame> InstalledGames() =>
        Games.Where(g => g.IsWeb ? Avail(g) : St(g.Id).Installed || St(g.Id).Installing).ToList();

    private List<CatalogGame> Updatable() =>
        InstalledGames().Where(g => !g.IsWeb && St(g.Id).Update && !St(g.Id).Installing && !St(g.Id).Running).ToList();

    private List<CatalogGame> SortGames(List<CatalogGame> list) => _sortBy switch
    {
        "most" => list.OrderByDescending(g => St(g.Id).PlaySeconds).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "az" => list.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => list.OrderByDescending(g => St(g.Id).LastPlayed).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
    };

    /// <summary>What the main button should do and say for a game: (action, style, text).</summary>
    private (string? Act, string Cls, string Text) Primary(string id)
    {
        var s = St(id);
        var g = Game(id);
        var d = g?.Demo == true ? " DEMO" : "";
        if (g?.IsLocal == true || g?.IsWeb == true)
        {
            if (_busy.Contains(id)) return (null, "", "OPENING…");
            if (s.Running) return (null, "", "PLAYING…");
            return ("play", "", g.IsWeb ? "▶  PLAY ONLINE" : "▶  PLAY");
        }
        if (s.Installing && s.Phase == "Queued") return (null, "inst", s.QueueLen > 1 ? $"QUEUED · {s.QueuePos} OF {s.QueueLen}" : "QUEUED…");
        if (s.Installing)
        {
            var word = s.Phase == "Downloading" ? (s.Updating ? "UPDATING" : "DOWNLOADING") : "INSTALLING";
            return (null, "inst", word + (s.Phase == "Downloading" || s.Progress > 0 ? $"  {s.Progress}%" : "…"));
        }
        if (_busy.Contains(id)) return (null, "", "WORKING…");
        if (s.Running) return (null, "", "PLAYING…");
        if (s.Installed && s.Update) return ("install", "upd", $"⟳  UPDATE TO {s.LatestVersion}");
        if (!s.Installed) return ("install", "inst", "⬇  INSTALL" + d);
        return ("play", "", "▶  PLAY" + d);
    }

    /// <summary>The main button, with a progress bar while downloading.</summary>
    private Button PrimaryButton(string id, bool small = false)
    {
        var (act, cls, text) = Primary(id);
        var s = St(id);
        Button b;
        if (s.Installing && s.Phase != "Queued" && (s.Phase == "Downloading" || s.Progress > 0))
        {
            var word = text[..text.IndexOf("  ", StringComparison.Ordinal)];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = word + "  " });
            row.Children.Add(Bar(s.Progress, 70, 6, Brushes.White));
            row.Children.Add(new TextBlock { Text = $"  {s.Progress}%" });
            b = Ui.Play("", cls, null, small);
            b.Content = row;
        }
        else b = Ui.Play(text, cls, act == null ? null : () => Act(id, act), small);
        if (act == null) b.IsEnabled = false;
        return b;
    }

    private Border PillFor(CatalogGame g, bool shortText = false)
    {
        var s = St(g.Id);
        if (!s.Available) return Pill("COMING SOON", "soon");
        if (g.IsWeb) return s.Running ? Pill("PLAYING", "ready") : Pill("🌐 WEB GAME", "web");
        if (g.IsLocal)
            return s.Running ? Pill("PLAYING", "ready") : s.LaunchError != null ? Pill("NOT FOUND", "miss")
                : g.IsStore ? Pill("XBOX / STORE", "mine") : Pill("YOUR GAME", "mine");
        if (s.Error != null) return Pill("INSTALL FAILED", "miss");
        var word = s.Phase == "Downloading" ? (s.Updating ? "UPDATING" : "DOWNLOADING") : "INSTALLING";
        if (s.Installing && s.Phase == "Queued") return Pill("QUEUED" + (s.QueueLen > 1 ? $" · {s.QueuePos} OF {s.QueueLen}" : ""), "inst");
        if (s.Installing && s.QueueLen > 1) return Pill($"{word} {s.Progress}% · 1 OF {s.QueueLen}", "inst");
        if (s.Installing) return Pill($"{word} {s.Progress}%", "inst");
        if (s.Running) return Pill("PLAYING", "ready");
        if (s.Installed && s.Update) return Pill("UPDATE" + (shortText ? "" : " AVAILABLE"), "upd");
        if (s.Installed) return Pill("✓ INSTALLED", "inst");
        return Pill("NOT INSTALLED", "avail");
    }

    private void Act(string id, string? what)
    {
        if (what == null || _busy.Contains(id)) return;
        var g = Game(id);
        if (g == null) return;
        if (what == "uninstall") { ConfirmUninstall(g); return; }
        _busy.Add(id);
        Refresh(force: true);
        try
        {
            var wasUpdate = St(id).Update;
            if (what == "play")
            {
                Core.Launch(id);
                ShowToast($"Starting {g.Title}{(g.Demo ? " (demo)" : "")}…");
            }
            else if (what == "install")
            {
                Core.Install(id);
                ShowToast($"{(wasUpdate ? "Updating" : "Downloading")} {g.Title} from GitHub…");
            }
        }
        catch (Exception ex) { Fail(ex); }
        // Opening a game takes a moment; keep "OPENING…" up briefly so a double click doesn't start it twice.
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(what == "play" ? 1500 : 200) };
        t.Tick += (_, _) => { t.Stop(); _busy.Remove(id); Refresh(force: true); };
        t.Start();
    }

    // ---------------------------------------------------------------- covers

    /// <summary>A game's cover: its art, or for the player's own games their cover art or a made cover with the program's icon.</summary>
    private FrameworkElement Cover(CatalogGame g, bool showName = true, double initials = 64)
    {
        if (g.IsLocal && g.Custom is { } cg)
        {
            if (!string.IsNullOrEmpty(cg.Art) && FromFile(System.IO.Path.Combine(AppPaths.CustomIcons, cg.Art), 600) is { } art)
                return new Grid { Background = B("#0d0f14"), ClipToBounds = true, Children = { Fill(art) } };
            return MadeCover(g.Title, System.IO.Path.Combine(AppPaths.CustomIcons, cg.Id + ".png"), showName, initials);
        }
        if (GameCover(g.Id, false, () => Refresh(force: true)) is { } img)
            return new Grid { ClipToBounds = true, Children = { Fill(img) } };
        return MadeCover(g.Title, null, showName, initials);
    }

    // ---------------------------------------------------------------- featured carousel

    private void RenderHero()
    {
        var f = FeaturedNow();
        var show = _view == "all" && f.Count > 0 && _query.Length == 0;
        _hero.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _hero.Children.Clear();
        _slides.Clear();
        _dots.Clear();
        if (!show) return;
        if (_slide >= f.Count) _slide = 0;
        _hero.Background = B("#20140d");

        for (var i = 0; i < f.Count; i++)
        {
            var ft = f[i];
            var g = Game(ft.Id)!;
            var s = St(ft.Id);
            var slide = new Grid { Opacity = i == _slide ? 1 : 0, IsHitTestVisible = i == _slide };
            if (GameCover(ft.Id, true, () => Refresh(force: true)) is { } art)
                slide.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            slide.Children.Add(new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(235, 14, 10, 8), 0), new GradientStop(Color.FromArgb(184, 14, 10, 8), 0.38),
                        new GradientStop(Color.FromArgb(0, 14, 10, 8), 0.70),
                    },
                },
            });

            var info = new StackPanel { Margin = new Thickness(38, 34, 38, 34), MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            info.Children.Add(Spaced(T(Track(ft.Kicker, 2), 11, Res("Gold"), FontWeights.SemiBold, true), 0, 8));
            var h1 = new StackPanel { Orientation = Orientation.Horizontal };
            var title = T(ft.Title, 52, B("#fff2d6"), FontWeights.Bold, true);
            title.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Direction = 270, Opacity = 0.5 };
            title.LineHeight = 50;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            h1.Children.Add(title);
            if (g.Demo) h1.Children.Add(DemoTag(true));
            info.Children.Add(h1);
            info.Children.Add(Spaced(T(Track(ft.Small, 2), 16, Res("Gold"), FontWeights.SemiBold, true), 10, 0));
            var desc = T(g.Description ?? "", 14, B("#d7cfc2"), wrap: TextWrapping.Wrap);
            desc.MaxWidth = 430;
            desc.MaxHeight = 62;
            desc.TextTrimming = TextTrimming.WordEllipsis;
            desc.HorizontalAlignment = HorizontalAlignment.Left;
            info.Children.Add(Spaced(desc, 14, 20));

            var actions = new WrapPanel();
            var p = PrimaryButton(ft.Id);
            p.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(p);
            var det = Btn("Details", "GhostButton", () => Details(ft.Id), small: true);
            det.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(det);
            if (s.Running) actions.Children.Add(RunningNote());
            info.Children.Add(actions);

            var meta = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            if (s.Installed || s.PlayCount > 0)
            {
                meta.Children.Add(Meta("Opened", Opens(s.PlayCount)));
                meta.Children.Add(Meta("Played", Dur(s.PlaySeconds)));
                meta.Children.Add(Meta("Last played", Ago(s.LastPlayed)));
            }
            else
            {
                meta.Children.Add(Meta("Version", s.InstalledVersion ?? s.LatestVersion ?? ""));
                meta.Children.Add(MetaText(g.Players ?? ""));
                meta.Children.Add(MetaText(g.Demo ? "Free demo" : "Free"));
                if (s.Size > 0) meta.Children.Add(MetaText(Size(s.Size)));
            }
            foreach (var chip in Plats(g, true).Children.Cast<UIElement>().ToList())
            {
                ((Panel)((FrameworkElement)chip).Parent).Children.Remove(chip);
                ((FrameworkElement)chip).VerticalAlignment = VerticalAlignment.Top;
                meta.Children.Add(chip);
            }
            info.Children.Add(meta);
            slide.Children.Add(info);
            _slides.Add(slide);
            _hero.Children.Add(slide);
        }

        // Arrows and dots, bottom right.
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 12) };
        nav.Children.Add(Arrow("‹", () => ShowSlide(_slide - 1)));
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < f.Count; i++)
        {
            var k = i;
            var d = new Border { Width = 30, Height = 5, CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 0, 4, 0), Background = i == _slide ? Res("Ember2") : B("#47FFFFFF") };
            var db = new Button { Style = Style("BareButton"), Content = new Border { Padding = new Thickness(0, 8, 0, 8), Background = Brushes.Transparent, Child = d }, ToolTip = f[i].Title };
            db.Click += (_, _) => ShowSlide(k);
            _dots.Add(d);
            dots.Children.Add(db);
        }
        nav.Children.Add(dots);
        nav.Children.Add(Arrow("›", () => ShowSlide(_slide + 1)));
        _hero.Children.Add(nav);
        _hero.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), IsHitTestVisible = false });
    }

    private static FrameworkElement Spaced(FrameworkElement e, double top, double bottom)
    {
        e.Margin = new Thickness(0, top, 0, bottom);
        return e;
    }

    private static TextBlock MetaText(string s) => T(s, 12, B("#b9b1a5")).Also(t => t.Margin = new Thickness(0, 0, 14, 4));

    private static UIElement RunningNote()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = B("#335FC489"), Margin = new Thickness(0, 0, 8, 0),
            Child = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Res("Ok") },
        });
        p.Children.Add(T("Running in its own window", 12));
        return p;
    }

    private Button Arrow(string glyph, Action click)
    {
        var b = Btn(new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = B("#2EFFFFFF"), BorderThickness = new Thickness(1), Background = B("#800A0A0E"),
            Child = new TextBlock { Text = glyph, Foreground = Brushes.White, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) },
        }, "BareButton", click);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    private void ShowSlide(int i)
    {
        if (_slides.Count == 0) return;
        _slide = ((i % _slides.Count) + _slides.Count) % _slides.Count;
        for (var k = 0; k < _slides.Count; k++)
        {
            var on = k == _slide;
            _slides[k].IsHitTestVisible = on;
            _slides[k].BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SineEase() });
            _dots[k].Background = on ? Res("Ember2") : B("#47FFFFFF");
        }
        _slideTimer.Stop();
        _slideTimer.Start();
    }

    private static readonly Dictionary<string, (string Name, string Glyph)> Plat = new()
    {
        ["windows"] = ("Windows", "⊞"), ["macos"] = ("macOS", "⌘"), ["linux"] = ("Linux", "♦"), ["web"] = ("Web", "◎"),
    };

    /// <summary>Which platforms a game plays on (only the versions that actually exist are listed).</summary>
    private static WrapPanel Plats(CatalogGame g, bool dark = false)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in g.Platforms ?? new())
        {
            if (!Plat.TryGetValue(k, out var p)) continue;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(T(p.Glyph + " ", 12, Res("Ember2")));
            row.Children.Add(T(p.Name, 11, B("#cfd6e2"), FontWeights.SemiBold));
            w.Children.Add(new Border
            {
                Background = B(dark ? "#59000000" : "#0FFFFFFF"), BorderBrush = B(dark ? "#38FFFFFF" : "#24FFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 5, 3), Child = row, ToolTip = "Plays on " + p.Name,
            });
        }
        return w;
    }

    // ---------------------------------------------------------------- tiles and sections

    private UIElement Tile(CatalogGame g, string where)
    {
        var s = St(g.Id);
        var soon = !s.Available;
        var (act, _, _) = soon ? (null, "", "") : Primary(g.Id);

        var cv = new Grid();
        var coverEl = Cover(g);
        if (soon) coverEl.Opacity = 0.62; // greyed: coming soon
        cv.Children.Add(coverEl);
        if (soon) cv.Children.Add(new Border { Background = B("#4D2A2F3A") });
        if (g.Demo) cv.Children.Add(DemoBand());
        if (s.Hidden)
            cv.Children.Add(new Border
            {
                Background = B("#B30A0C10"), VerticalAlignment = VerticalAlignment.Center, Height = 24,
                Child = new TextBlock { Text = Track("HIDDEN", 3), FontSize = 11, FontWeight = FontWeights.ExtraBold, FontFamily = Display, Foreground = B("#dddddd"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });

        // Favorite star (bottom right of the cover): shows on hover, always when on.
        var star = new Button
        {
            Style = Style("BareButton"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10),
            ToolTip = s.Favorite ? "Remove from Favorites" : "Add to Favorites",
            Content = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = B("#9E0A0C10"),
                Child = new TextBlock { Text = s.Favorite ? "★" : "☆", FontSize = 16, Foreground = B(s.Favorite ? "#f3d27a" : "#e8e2d4"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
            Opacity = s.Favorite ? 1 : 0,
        };
        star.Click += (_, e) => { e.Handled = true; SetPref(g.Id, favorite: !s.Favorite); };
        cv.Children.Add(star);

        var cover = new RatioBox { Ratio = 1.25, Child = cv };
        cover.Clip = null;

        var sub = soon ? "Coming soon · " + g.Players : $"{g.Subtitle} · {g.Players}";
        if (where == "installed")
            sub = (g.IsWeb ? "Web game · " : g.IsLocal ? "Your game · " : s.InstalledVersion != null ? $"v{s.InstalledVersion} · " : "") + $"Played {Dur(s.PlaySeconds)}";
        var t1 = new StackPanel { Orientation = Orientation.Horizontal };
        t1.Children.Add(T(g.Title, 15, weight: FontWeights.Bold, display: true));
        if (g.Demo) t1.Children.Add(DemoTag());
        var tt = new StackPanel { Margin = new Thickness(13, 11, 13, 13) };
        tt.Children.Add(t1);
        tt.Children.Add(T(sub, 12, Res("Muted")).Also(x => x.Margin = new Thickness(0, 1, 0, 0)));
        if (g.Platforms is { Count: > 0 }) tt.Children.Add(Plats(g));

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(cover);
        Grid.SetRow(tt, 1);
        body.Children.Add(tt);

        // Badges over cover art get a solid dark backing so they read on any colour.
        body.Children.Add(new Border
        {
            Background = B("#E00E1014"), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 12, 0, 0), Child = PillFor(g, true),
        });

        // Play / install chip, top right.
        if (act != null)
        {
            var icon = act == "play" ? "▶" : s.Update ? "⟳" : "⬇";
            Brush bg = act == "play" ? Res("PlayBrush") : s.Update ? Res("UpdBrush") : B("#D9121419");
            var chip = new Button
            {
                Style = Style("BareButton"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 12, 0),
                ToolTip = act == "play" ? "Play" : s.Update ? "Update to " + s.LatestVersion : "Install",
                Content = new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = bg,
                    BorderBrush = act == "play" || s.Update ? null : B("#40FFFFFF"), BorderThickness = new Thickness(act == "play" || s.Update ? 0 : 1),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 4, Direction = 270, Opacity = 0.4 },
                    Child = new TextBlock
                    {
                        Text = icon, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = act == "play" ? B("#1b0f06") : s.Update ? B("#1d1504") : Res("Text"),
                    },
                },
            };
            chip.Click += (_, e) => { e.Handled = true; Act(g.Id, act); };
            body.Children.Add(chip);
        }

        if (s.Installing)
        {
            var bar = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Background = B("#14FFFFFF") };
            Grid.SetRow(bar, 1);
            bar.Child = new Border { Background = Res("Ok"), HorizontalAlignment = HorizontalAlignment.Left };
            bar.SizeChanged += (_, e) => ((Border)bar.Child).Width = e.NewSize.Width * s.Progress / 100.0;
            body.Children.Add(bar);
        }

        var tile = new Button
        {
            Style = Style("TileButton"), Content = body, ClipToBounds = false, Opacity = s.Hidden ? 0.5 : 1,
            BorderBrush = g.Demo ? B("#73FFAA50") : Res("Line"), HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        RoundClip(body, 13);
        tile.MouseEnter += (_, _) => { if (!s.Favorite) star.Opacity = 1; };
        tile.MouseLeave += (_, _) => { if (!s.Favorite) star.Opacity = 0; };
        tile.Click += (_, _) => Details(g.Id);
        return tile;
    }

    private static Border DemoBand() => new()
    {
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), Height = 24, IsHitTestVisible = false,
        Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 255, 180, 80), 0), new GradientStop(Color.FromArgb(245, 255, 190, 90), 0.16),
                new GradientStop(Color.FromArgb(245, 255, 190, 90), 0.84), new GradientStop(Color.FromArgb(0, 255, 180, 80), 1),
            },
        },
        Child = new TextBlock
        {
            Text = Track("FREE DEMO", 3), FontSize = 11, FontWeight = FontWeights.ExtraBold, FontFamily = Display, Foreground = B("#1a0b22"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private UIElement AddTile()
    {
        var plus = T("+", 72, Res("Ember2"), FontWeights.Light, true);
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        plus.VerticalAlignment = VerticalAlignment.Center;
        plus.Opacity = 0.85;
        var body = new StackPanel();
        body.Children.Add(new RatioBox { Ratio = 1.25, Child = new Grid { Background = B("#05FFFFFF"), Children = { plus } } });
        var tt = new StackPanel { Margin = new Thickness(13, 11, 13, 13) };
        tt.Children.Add(T("Add a game", 15, weight: FontWeights.Bold, display: true));
        tt.Children.Add(T("Any game on this PC", 12, Res("Muted")));
        body.Children.Add(tt);
        RoundClip(body, 13);
        // A dashed outline that turns ember on hover.
        var outline = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 14, RadiusY = 14, Stroke = Res("Line"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false,
        };
        var b = new Button { Style = Style("BareButton"), Content = new Grid { Background = Brushes.Transparent, Children = { body, outline } } };
        b.MouseEnter += (_, _) => outline.Stroke = Res("Ember2");
        b.MouseLeave += (_, _) => outline.Stroke = Res("Line");
        b.Click += (_, _) => AddGameDialog();
        return b;
    }

    private FrameworkElement SectionHead(string title, string count, string? blurb, UIElement? extra = null)
    {
        var p = new StackPanel();
        var h = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = false };
        var t = T(title, 21, weight: FontWeights.Bold, display: true);
        h.Children.Add(t);
        var c = T(count, 12, Res("Muted"));
        c.Margin = new Thickness(14, 0, 0, 0);
        c.VerticalAlignment = VerticalAlignment.Bottom;
        c.Padding = new Thickness(0, 0, 0, 3);
        h.Children.Add(c);
        if (extra != null)
        {
            DockPanel.SetDock(extra, Dock.Right);
            h.Children.Add(extra);
        }
        p.Children.Add(h);
        if (!string.IsNullOrEmpty(blurb)) p.Children.Add(T(blurb, 13, Res("Muted"), wrap: TextWrapping.Wrap).Also(x => x.Margin = new Thickness(0, 0, 0, 16)));
        else p.Children.Add(new Border { Height = 12 });
        return p;
    }

    private AutoGrid Grid_(IEnumerable<CatalogGame> list, string where, UIElement? tail = null)
    {
        var grid = new AutoGrid();
        foreach (var g in list) grid.Children.Add(Tile(g, where));
        if (tail != null) grid.Children.Add(tail);
        return grid;
    }

    private UIElement Section(string title, string count, string? blurb, List<CatalogGame> list, string where, UIElement? extra = null, UIElement? tail = null)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 34) };
        p.Children.Add(SectionHead(title, count, blurb, extra));
        p.Children.Add(Grid_(list, where, tail));
        return p;
    }

    private static string Count(int n, string one = "game", string many = "games") => $"{n} {(n == 1 ? one : many)}";

    /// <summary>Non-Ironite Games: all of the player's own games, grouped by the category they gave them.</summary>
    private UIElement CustomSection(CatalogCategory c)
    {
        var mine = Games.Where(g => g.IsLocal && Visible(g)).ToList();
        var count = mine.Count > 0 ? Count(mine.Count) : "Add your first game";
        var groups = mine.Select(g => g.Group ?? "").Distinct().OrderBy(x => x.Length == 0 ? 1 : 0).ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_view != "custom" || groups.Count < 2) return Section(c.Name, count, c.Blurb, mine, "cat", tail: AddTile());
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 34) };
        p.Children.Add(SectionHead(c.Name, count, c.Blurb));
        for (var i = 0; i < groups.Count; i++)
        {
            var grp = groups[i];
            var list = mine.Where(g => (g.Group ?? "") == grp).ToList();
            var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 12) };
            h.Children.Add(T(Track((grp.Length > 0 ? grp : "No category").ToUpperInvariant()), 14, B("#d9cdb6"), FontWeights.SemiBold, true));
            h.Children.Add(T("   " + list.Count, 12, Res("Muted")));
            p.Children.Add(h);
            p.Children.Add(Grid_(list, "cat", i == groups.Count - 1 ? AddTile() : null));
        }
        return p;
    }

    private void RenderLibrary()
    {
        if (Pages.Contains(_view)) return;
        // Library tools: Show hidden appears once something is hidden; sorting is for Installed.
        var nHidden = Games.Count(g => St(g.Id).Hidden);
        _showHid.Visibility = nHidden > 0 || _showHidden ? Visibility.Visible : Visibility.Collapsed;
        _showHid.Content = $"Show hidden ({nHidden})";
        _sort.Visibility = _view == "installed" ? Visibility.Visible : Visibility.Collapsed;

        RenderHero();
        _cats.Children.Clear();
        var inst = SortGames(InstalledGames());
        var ups = Updatable();

        if (_query.Length > 0)
        {
            var pool = _view switch
            {
                "all" => Games,
                "installed" => inst,
                "custom" => Games.Where(g => g.IsLocal).ToList(),
                _ => Games.Where(g => g.Category == _view).ToList(),
            };
            var found = pool.Where(g => !g.IsPlaceholder && Matches(g) && Visible(g))
                .OrderByDescending(g => Avail(g)).ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            var q = _search.Text.Trim();
            if (found.Count > 0) _cats.Children.Add(Section("Search results", $"{Count(found.Count)} matching “{q}”", null, found, "cat"));
            else
            {
                var p = new StackPanel();
                p.Children.Add(SectionHead("Search results", "", null));
                p.Children.Add(Empty($"No games match “{q}”", "Try a shorter name" + (nHidden > 0 && !_showHidden ? ", or tick Show hidden." : ".")));
                _cats.Children.Add(p);
            }
            return;
        }

        UIElement? upBtn = null;
        if (ups.Count > 0)
        {
            var b = Ui.Play($"⟳  Update all ({ups.Count})", "upd", () => { foreach (var g in Updatable()) Act(g.Id, "install"); }, small: true);
            b.Padding = new Thickness(14, 7, 14, 7);
            b.FontSize = 12.5;
            upBtn = b;
        }
        if (_view == "all")
        {
            var favs = Games.Where(g => St(g.Id).Favorite && Visible(g)).ToList();
            if (favs.Count > 0) _cats.Children.Add(Section("Favorites", Count(favs.Count), null, favs, "cat"));
        }
        if (_view == "installed")
        {
            if (inst.Count > 0)
            {
                var total = inst.Sum(g => St(g.Id).PlaySeconds);
                _cats.Children.Add(Section("Installed", $"{Count(inst.Count)} · {Dur(total)} played in total",
                    Core.AutoUpdate ? "Games you have installed. They update automatically from GitHub when a new version comes out."
                        : "Games you have installed. Automatic updates are off; update them here.", inst, "installed", upBtn));
            }
            else
            {
                var p = new StackPanel();
                p.Children.Add(SectionHead("Installed", "", null));
                p.Children.Add(Empty("No games installed yet", "Open the Library and click Install on a game. It downloads from GitHub and shows up here, ready to play."));
                _cats.Children.Add(p);
            }
            return;
        }
        if (_view == "all" && inst.Count > 0) _cats.Children.Add(Section("Installed", Count(inst.Count), null, inst, "installed", upBtn));
        foreach (var c in Core.Catalog.Categories.Where(c => _view == "all" || _view == c.Id))
        {
            if (c.Id == LauncherCore.CustomCategoryId) { _cats.Children.Add(CustomSection(c)); continue; }
            var list = Games.Where(g => g.Category == c.Id && Visible(g)).OrderByDescending(g => Avail(g)).ToList();
            var ready = list.Count(Avail);
            var count = ready == 0 ? "Coming soon" : $"{ready} available" + (list.Count > ready ? $" · {list.Count - ready} coming soon" : "");
            _cats.Children.Add(Section(c.Name, count, c.Blurb, list, "cat"));
        }
    }

    private void SetPref(string id, bool? favorite = null, bool? hidden = null)
    {
        try
        {
            Core.SetPref(id, favorite, hidden);
            Refresh(force: true);
        }
        catch (Exception ex) { Fail(ex); }
    }
}

internal static class FluentExtensions
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }
}
