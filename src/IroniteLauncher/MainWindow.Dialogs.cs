using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ironite.Launcher.Models;
using Ironite.Launcher.Services;
using Ironite.Launcher.Views;
using static Ironite.Launcher.Views.Ui;

namespace Ironite.Launcher;

/// <summary>The dialog (cover on the left, details on the right) used for game details and every form.</summary>
public partial class MainWindow
{
    private string? _modalId;

    private sealed class Modal
    {
        public FrameworkElement? Art;
        public UIElement? Pill;
        public UIElement? Title;
        public string? Sub;
        public UIElement? Desc;
        public UIElement? Notes;
        public UIElement? Meta;
        public UIElement? Actions;
        public UIElement? Tools;
    }

    private void OpenModal(Modal m)
    {
        DlgArt.Child = m.Art;
        DlgBody.Children.Clear();
        if (m.Pill != null) DlgBody.Children.Add(m.Pill);
        if (m.Title != null) DlgBody.Children.Add(m.Title);
        if (!string.IsNullOrEmpty(m.Sub)) DlgBody.Children.Add(T(m.Sub, 14, Res("Muted"), wrap: TextWrapping.Wrap));
        if (m.Desc != null) DlgBody.Children.Add(m.Desc);
        else DlgBody.Children.Add(new Border { Height = 14 });
        if (m.Notes != null) DlgBody.Children.Add(m.Notes);
        if (m.Meta != null) DlgBody.Children.Add(m.Meta);
        if (m.Actions != null) DlgBody.Children.Add(m.Actions);
        if (m.Tools != null) DlgBody.Children.Add(m.Tools);
        Veil.Visibility = Visibility.Visible;
    }

    public void CloseModal()
    {
        Veil.Visibility = Visibility.Collapsed;
        _modalId = null;
        DlgArt.AllowDrop = false;
    }

    /// <summary>
    /// The dialog's picture column: the cover as a card (its own 4:5 shape, rounded, with a shadow) at the top
    /// of a plain dark column, like a game box on a store page. The column is taller than a cover, so the cover
    /// isn't stretched to fill it.
    /// </summary>
    private static FrameworkElement ArtPanel(Func<FrameworkElement> make)
    {
        var art = make();
        var cover = new RatioBox { Ratio = 1.25, Child = art };
        RoundClip(art, 10);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10), BorderBrush = B("#1FFFFFFF"), BorderThickness = new Thickness(1),
            Margin = new Thickness(20, 22, 20, 22), VerticalAlignment = VerticalAlignment.Top, Child = cover,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Direction = 270, Opacity = 0.5 },
        };
        return new Grid { Background = Res("Bg2"), Children = { card } };
    }

    private static TextBlock DlgTitle(string text, bool demo = false)
    {
        var t = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, FontFamily = Display, Foreground = Res("Text"), Margin = new Thickness(0, 12, 0, 2), TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(new Run(text));
        if (demo) t.Inlines.Add(new InlineUIContainer(DemoTag()) { BaselineAlignment = BaselineAlignment.Center });
        return t;
    }

    private static TextBlock DlgDesc(string text) =>
        T(text, 14, B("#d0ccc5"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 14, 0, 20));

    private static WrapPanel Actions(params UIElement?[] items)
    {
        var w = new WrapPanel();
        foreach (var i in items.Where(x => x != null))
        {
            if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
            w.Children.Add(i!);
        }
        return w;
    }

    private static UIElement Tools(params UIElement?[] links)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var l in links.Where(x => x != null))
        {
            if (l is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 18, 0);
            w.Children.Add(l!);
        }
        return new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Margin = new Thickness(0, 4, 0, 0), Child = w };
    }

    private static Border Box(UIElement child, bool error = false) => new()
    {
        Background = B(error ? "#1AE65A50" : "#2E000000"), BorderBrush = error ? B("#66E65A50") : Res("Line"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, -6, 0, 18), Child = child,
    };

    private static TextBlock NotesHead(string text, string? small = null)
    {
        var t = new TextBlock { FontSize = 11, FontFamily = Display, FontWeight = FontWeights.SemiBold, Foreground = Res("Ember2"), Margin = new Thickness(0, 0, 0, 4) };
        t.Inlines.Add(new Run(Track(text.ToUpperInvariant())));
        if (small != null) t.Inlines.Add(new Run(" · " + small) { Foreground = Res("Muted"), FontWeight = FontWeights.Normal });
        return t;
    }

    /// <summary>Release notes (Markdown from GitHub) as simple text: headings, paragraphs and bullet points.</summary>
    private static StackPanel NotesView(string? md)
    {
        var p = new StackPanel();
        foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
        {
            if (Regex.IsMatch(raw, @"^---+\s*$")) break; // the footer that links back to the launcher
            if (raw.Trim().Length == 0) continue;
            var l = Regex.Replace(raw, @"\[([^\]]+)\]\([^)]+\)", "$1");
            Match m;
            if ((m = Regex.Match(l, @"^\s*[-*]\s+(.*)$")).Success)
            {
                var row = new DockPanel { Margin = new Thickness(4, 2, 0, 2) };
                var dot = T("•", 13, B("#d0ccc5"));
                dot.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(Inline(m.Groups[1].Value));
                p.Children.Add(row);
            }
            else if ((m = Regex.Match(l, @"^#{1,6}\s*(.*)$")).Success)
                p.Children.Add(T(Track(m.Groups[1].Value), 12, B("#e9dcc4"), FontWeights.SemiBold, true).Also(t => t.Margin = new Thickness(0, 8, 0, 2)));
            else p.Children.Add(Inline(l).Also(t => t.Margin = new Thickness(0, 4, 0, 4)));
        }
        return p;
    }

    /// <summary>**bold** and `code` in a line of release notes.</summary>
    private static TextBlock Inline(string text)
    {
        var t = new TextBlock { FontSize = 13, Foreground = B("#d0ccc5"), TextWrapping = TextWrapping.Wrap };
        foreach (var part in Regex.Split(text, @"(\*\*.+?\*\*|`[^`]+`)"))
        {
            if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4) t.Inlines.Add(new Bold(new Run(part[2..^2])));
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2) t.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Consolas") });
            else t.Inlines.Add(new Run(part));
        }
        return t;
    }

    // ---------------------------------------------------------------- game details

    internal void Details(string id)
    {
        var g = Game(id);
        if (g == null) return;
        _modalId = id;
        var s = St(id);
        var m = new Modal { Art = ArtPanel(() => Cover(g, true, 84)), Pill = PillFor(g), Title = DlgTitle(g.Title, g.Demo) };
        if (!s.Available)
        {
            m.Sub = g.IsPlaceholder ? "New category" : $"{g.Subtitle} · {g.Players}";
            m.Desc = DlgDesc((g.Description ?? "") + (!string.IsNullOrEmpty(g.Repo) ? " It will be ready to install here as soon as its first release is published." : " Coming soon to Ironite Launcher."));
            m.Actions = Actions(Btn("Got it", "GhostButton", CloseModal, small: true));
            m.Tools = Tools(FavLink(g, s), HideLink(g, s));
            OpenModal(m);
            return;
        }

        var v = g.IsWeb ? "Web game"
            : g.IsLocal ? (g.IsStore ? "Xbox / Microsoft Store" : "Not an Ironite game") + (string.IsNullOrEmpty(g.Group) ? "" : " · " + g.Group)
            : s.Installed ? $"v{s.InstalledVersion} installed" + (s.Update ? $" · v{s.LatestVersion} available" : "")
            : $"v{s.LatestVersion}" + (s.Size > 0 ? $" · {Size(s.Size)} download" : "");
        m.Sub = $"{g.Subtitle} · {g.Players} · {v}";

        if (g.IsLocal)
        {
            var d = new TextBlock { FontSize = 14, Foreground = B("#d0ccc5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 20) };
            if (!string.IsNullOrEmpty(g.Description)) { d.Inlines.Add(new Run(g.Description)); d.Inlines.Add(new LineBreak()); d.Inlines.Add(new LineBreak()); }
            if (g.IsStore) d.Inlines.Add(new Run("Added from the Xbox app / Microsoft Store. Windows starts it, and the launcher tracks your play time while it’s open; updates come from the Xbox app or Store."));
            else
            {
                d.Inlines.Add(new Run("Added from "));
                d.Inlines.Add(new Run(g.Exe) { FontFamily = new FontFamily("Consolas"), FontSize = 12, Background = B("#0FFFFFFF") });
                d.Inlines.Add(new Run(". The launcher starts it and tracks your play time; updates come from the game itself."));
            }
            m.Desc = d;
        }
        else
            m.Desc = DlgDesc((g.Description ?? "") + (g.IsWeb ? " Needs an internet connection; your progress is saved on this PC." : "")
                             + (g.Demo ? " New demo versions arrive automatically through the launcher." : ""));

        var notes = new StackPanel();
        if (s.Error != null) notes.Children.Add(Box(T(s.Error, 13, B("#f0b1aa"), wrap: TextWrapping.Wrap), true));
        else if (s.LaunchError != null) notes.Children.Add(Box(T(s.LaunchError, 13, B("#f0b1aa"), wrap: TextWrapping.Wrap), true));
        else if (!string.IsNullOrWhiteSpace(s.Notes))
        {
            var n = new StackPanel();
            n.Children.Add(NotesHead((s.Update ? "New in" : "What’s new in") + " version " + s.LatestVersion, s.PublishedAt > 0 ? Ago(s.PublishedAt) : null));
            n.Children.Add(NotesView(s.Notes));
            notes.Children.Add(Box(new ScrollViewer { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = n }));
        }
        m.Notes = notes;
        if (!g.IsLocal && AchievementService.Ironite(id) is { Error: null, Total: > 0 } ach) notes.Children.Add(Box(AchSummary(ach)));
        if (s.PlayCount > 0) AddSessions(notes, id);

        var meta = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        if (!g.IsLocal && g.Platforms is { Count: > 0 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(T("Plays on ", 12, Res("Muted")).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
            row.Children.Add(Plats(g).Also(p => p.Margin = new Thickness(0)));
            meta.Children.Add(new Border { Width = 9999, MaxWidth = 420, Child = row });
        }
        meta.Children.Add(Meta("Opened", Opens(s.PlayCount), Res("Muted")));
        meta.Children.Add(Meta("Played", Dur(s.PlaySeconds), Res("Muted")));
        meta.Children.Add(Meta("Last played", Ago(s.LastPlayed), Res("Muted")));
        if (s.Installed && s.InstalledAt > 0) meta.Children.Add(Meta(g.IsLocal ? "Added" : "Installed", Ago(s.InstalledAt), Res("Muted")));
        if (s.DiskSize > 0) meta.Children.Add(Meta("Size on disk", Size(s.DiskSize), Res("Muted")));
        m.Meta = meta;

        var primary = PrimaryButton(id, small: true);
        primary.Click += (_, _) => CloseModal();
        m.Actions = Actions(
            primary,
            s.Error != null ? Btn("Try again", "GhostButton", () => { Core.Dismiss(id); Act(id, "install"); }, small: true) : null,
            s.Installed && s.Update && !s.Running && !s.Installing ? Btn("▶  Play " + s.InstalledVersion, "GhostButton", () => { CloseModal(); Act(id, "play"); }, small: true) : null,
            g.IsLocal && g.IsStore && s.Running ? Btn("■  It’s closed", "GhostButton", async () => { await Core.MarkClosedAsync(id); Refresh(force: true); }, small: true) : null,
            g.IsLocal ? Btn("✎  Edit details", "GhostButton", () => EditDialog(g), small: true) : null,
            s.Installed && !s.Running && !s.Installing ? Btn(g.IsLocal ? "Remove from launcher" : "Uninstall", "DangerButton", () => ConfirmUninstall(g)) : null,
            Btn("Close", "GhostButton", CloseModal, small: true));
        m.Tools = Tools(
            FavLink(g, s), HideLink(g, s),
            s.Installed || g.IsWeb ? Link("Desktop shortcut", async () =>
            {
                try
                {
                    await Core.CreateShortcutAsync(id);
                    ShowToast($"Added a {g.Title} shortcut to your desktop. It starts the game through the launcher, so play time is still counted.");
                }
                catch (Exception ex) { Fail(ex); }
            }) : null,
            s.Installed && !g.IsLocal && !g.IsWeb && Core.Settings.Installs!.TryGetValue(id, out var rec) ? Link("Open game folder", () => Platform.OpenFolder(rec.Dir)) : null);
        OpenModal(m);
    }

    private Button FavLink(CatalogGame g, GameState s) => Link(s.Favorite ? "★ In Favorites" : "☆ Add to Favorites", () => SetPref(g.Id, favorite: !s.Favorite));

    private Button HideLink(CatalogGame g, GameState s) => Link(s.Hidden ? "Show in the Library again" : "Hide this game", () =>
    {
        SetPref(g.Id, hidden: !s.Hidden);
        ShowToast(s.Hidden ? $"{g.Title} is back in the Library." : $"{g.Title} is hidden. Tick Show hidden to see it again.");
    });

    /// <summary>An Ironite game's achievements in its details: how many, the latest ones, and a link to the full list.</summary>
    private UIElement AchSummary(AchievementResult ach)
    {
        var p = new StackPanel();
        var pct = (int)Math.Round(ach.Unlocked * 100.0 / ach.Total);
        p.Children.Add(NotesHead("Achievements", $"{ach.Unlocked} of {ach.Total} unlocked"));
        p.Children.Add(new Border { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Width = 420, Margin = new Thickness(0, 2, 0, 10), Child = StretchBar(pct) });
        var recent = ach.Items.Where(i => i.Unlocked).OrderByDescending(i => i.UnlockedAt).Take(8).ToList();
        if (recent.Count > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var it in recent)
            {
                FrameworkElement icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Res("InputBg") };
                if (!string.IsNullOrEmpty(it.Icon) && Uri.TryCreate(it.Icon, UriKind.Absolute, out var uri))
                {
                    try
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = uri;
                        bmp.DecodePixelWidth = 80;
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        icon = new Image { Width = 40, Height = 40, Source = bmp };
                    }
                    catch { /* missing icon: keep the plain circle */ }
                }
                icon.Margin = new Thickness(0, 0, 8, 0);
                icon.ToolTip = it.Name + (string.IsNullOrEmpty(it.Description) ? "" : "\n" + it.Description);
                row.Children.Add(icon);
            }
            p.Children.Add(row);
        }
        else p.Children.Add(Muted("None unlocked yet.", 12.5).Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
        var id = ach.Id;
        p.Children.Add(Link("See all achievements", () => { CloseModal(); ShowAchievements(id); }));
        return p;
    }

    /// <summary>A game's recent play sessions, under its details.</summary>
    private void AddSessions(StackPanel notes, string id)
    {
        var list = Core.Sessions(id);
        if (list.Count == 0) return;
        var p = new StackPanel();
        p.Children.Add(NotesHead("Recent sessions"));
        foreach (var x in list.Take(6))
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var d = T(Dur(x.Secs), 13, weight: FontWeights.SemiBold);
            DockPanel.SetDock(d, Dock.Right);
            row.Children.Add(d);
            row.Children.Add(T(DateTimeOffset.FromUnixTimeSeconds(x.Start).LocalDateTime.ToString("ddd, MMM d, h:mm tt"), 13, B("#d0ccc5")));
            p.Children.Add(row);
        }
        if (list.Count > 6) p.Children.Add(T($"{list.Count - 6} more on the Stats page", 12, Res("Muted")));
        notes.Children.Add(Box(p));
    }

    private static FrameworkElement AppIconArt()
    {
        var img = new Image { Source = Pack("Assets/icon-1024.png"), Stretch = Stretch.Uniform, Margin = new Thickness(40) };
        return new Grid { Background = B("#141821"), Children = { img } };
    }

    private void ReleaseDialog(string title, string notes)
    {
        _modalId = null;
        OpenModal(new Modal
        {
            Art = AppIconArt(), Pill = Pill("LAUNCHER UPDATE", "upd"), Title = DlgTitle(title), Sub = $"You have version {App.VersionText}",
            Notes = Box(new ScrollViewer
            {
                MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = string.IsNullOrWhiteSpace(notes) ? Inline("Improvements and fixes.") : NotesView(notes),
            }),
            Actions = Actions(Btn("Close", "GhostButton", CloseModal, small: true)),
        });
    }

    private void ConfirmUninstall(CatalogGame g)
    {
        _modalId = null;
        if (g.IsLocal)
        {
            OpenModal(new Modal
            {
                Art = ArtPanel(() => Cover(g, true, 84)), Pill = Pill("REMOVE", "miss"), Title = DlgTitle($"Remove {g.Title} from the launcher?"),
                Desc = DlgDesc($"{g.Title} disappears from the launcher, along with its play stats here. The game itself isn't touched: its files stay on this PC and you can add it again any time."),
                Actions = Actions(Btn("Remove", "DangerButton", () =>
                {
                    CloseModal();
                    try { Core.RemoveCustom(g.Id); ShowToast($"{g.Title} was removed from the launcher."); }
                    catch (Exception ex) { Fail(ex); }
                    Refresh(force: true);
                }), Btn("Cancel", "GhostButton", CloseModal, small: true)),
            });
            return;
        }
        OpenModal(new Modal
        {
            Art = ArtPanel(() => Cover(g, true, 84)), Pill = Pill("UNINSTALL", "miss"), Title = DlgTitle($"Uninstall {g.Title}{(g.Demo ? " Demo" : "")}?"),
            Desc = DlgDesc($"This removes {g.Title}'s game files from this PC. Your saves, settings and play stats are kept, and you can install it again any time from the Library."),
            Actions = Actions(Btn("Uninstall", "DangerButton", async () =>
            {
                CloseModal();
                _busy.Add(g.Id);
                Refresh(force: true);
                try { await Core.UninstallAsync(g.Id); ShowToast($"{g.Title} was uninstalled."); }
                catch (Exception ex) { Fail(ex); }
                _busy.Remove(g.Id);
                Refresh(force: true);
            }), Btn("Cancel", "GhostButton", CloseModal, small: true)),
        });
    }

    // ---------------------------------------------------------------- forms

    private static FrameworkElement Field(string label, UIElement control, string? help = null)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        p.Children.Add(T(Track(label.ToUpperInvariant()), 11, Res("Muted"), FontWeights.SemiBold, true).Also(t => t.Margin = new Thickness(0, 0, 0, 5)));
        p.Children.Add(control);
        if (help != null) p.Children.Add(T(help, 12, Res("Muted"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 5, 0, 0)));
        return p;
    }

    private static Border FormError(out TextBlock text)
    {
        text = T("", 13, B("#f0b1aa"), wrap: TextWrapping.Wrap);
        return new Border
        {
            Background = B("#1AE65A50"), BorderBrush = B("#66E65A50"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed, Child = text,
        };
    }

    private static Action<string> ErrorSetter(Border box, TextBlock text) => msg =>
    {
        text.Text = msg.Length > 0 ? Sentence(msg) : "";
        box.Visibility = msg.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    };

    /// <summary>Category choices: the launcher's categories, plus the player's own, plus "New category…".</summary>
    private ComboBox CategoryBox(CatalogGame? g, TextBox newCat)
    {
        var box = new ComboBox();
        void Add(string value, string label, bool header = false)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            if (header) { item.IsEnabled = false; item.FontSize = 11; item.Foreground = Res("Muted"); }
            box.Items.Add(item);
        }
        var sel = g == null ? "" : !string.IsNullOrEmpty(g.Custom?.Category) && g.Custom!.Category != LauncherCore.CustomCategoryId ? "cat:" + g.Custom.Category
            : !string.IsNullOrEmpty(g.Group) ? "grp:" + g.Group : "";
        Add("", "No category");
        var launcher = Core.Catalog.Categories.Where(c => c.Id != LauncherCore.CustomCategoryId).ToList();
        Add("-", "Launcher categories (the game shows there too)", true);
        foreach (var c in launcher) Add("cat:" + c.Id, "   " + c.Name);
        var names = launcher.Select(c => c.Name).ToHashSet();
        var own = Games.Where(x => x.IsLocal && x.Custom?.Group is { Length: > 0 }).Select(x => x.Custom!.Group!).Distinct().Where(n => !names.Contains(n)).ToList();
        if (own.Count > 0)
        {
            Add("-", "Your categories", true);
            foreach (var n in own) Add("grp:" + n, "   " + n);
        }
        Add("new", "New category…");
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == sel) ?? box.Items[0];
        newCat.Visibility = Visibility.Collapsed;
        newCat.Margin = new Thickness(0, 6, 0, 0);
        newCat.MaxLength = 40;
        box.SelectionChanged += (_, _) =>
        {
            var isNew = (string)((ComboBoxItem)box.SelectedItem).Tag == "new";
            newCat.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            if (isNew) newCat.Focus();
        };
        return box;
    }

    private static (string Category, string Group, bool MissingName) ReadCategory(ComboBox box, TextBox newCat)
    {
        var v = (string)((ComboBoxItem)box.SelectedItem).Tag;
        if (v == "new") return ("", newCat.Text.Trim(), newCat.Text.Trim().Length == 0);
        if (v.StartsWith("cat:")) return (v[4..], "", false);
        if (v.StartsWith("grp:")) return ("", v[4..], false);
        return ("", "", false);
    }

    private static StackPanel Stack(params UIElement[] items)
    {
        var p = new StackPanel();
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    private static FrameworkElement Placeholder(TextBox box, string text)
    {
        var hint = T(text, 13.5, B("#6b7280"));
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.VerticalAlignment = box.AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (box.AcceptsReturn) hint.Margin = new Thickness(11, 9, 0, 0);
        void Update() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Update();
        Update();
        return new Grid { Children = { box, hint } };
    }

    // ---------------------------------------------------------------- adding your own games

    internal void AddGameDialog()
    {
        _modalId = null;
        var path = new TextBox();
        var title = new TextBox { MaxLength = 60 };
        var newCat = new TextBox();
        var cat = CategoryBox(null, newCat);
        var errBox = FormError(out var errText);
        var err = ErrorSetter(errBox, errText);
        var titleTouched = false;
        title.TextChanged += (_, _) => { if (title.IsKeyboardFocused) titleTouched = true; };
        void SetTitle(string t) { if (!titleTouched || title.Text.Length == 0) title.Text = t; }

        var browse = Btn("Browse…", "GhostButton", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a game to add to Ironite Launcher", Filter = "Games (*.exe)|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            };
            if (dlg.ShowDialog(this) == true)
            {
                path.Text = dlg.FileName;
                SetTitle(LauncherCore.GuessTitle(dlg.FileName));
            }
        }, small: true);
        browse.Margin = new Thickness(8, 0, 0, 0);
        path.LostFocus += (_, _) => { if (path.Text.Trim().Length > 0) SetTitle(LauncherCore.GuessTitle(path.Text)); };
        var pathRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(Placeholder(path, @"C:\Games\My Game\MyGame.exe"));
        var exePane = Field("Program", pathRow);

        // Xbox / Microsoft Store tab.
        var search = new TextBox();
        var all = new CheckBox { Content = "Show apps that aren’t games too", FontSize = 12.5, Margin = new Thickness(0, 8, 0, 12) };
        var list = new StackPanel();
        var listBox = new Border
        {
            BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = Res("InputBg"), Padding = new Thickness(4), Height = 190,
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list },
        };
        List<StoreApp>? apps = null;
        StoreApp? picked = null;
        void DrawApps()
        {
            list.Children.Clear();
            if (apps == null) return;
            var q = search.Text.Trim().ToLowerInvariant();
            var games = apps.Where(a => a.Game).ToList();
            var shown = apps.Where(a => (all.IsChecked == true || a.Game || games.Count == 0) && (q.Length == 0 || a.Name.ToLowerInvariant().Contains(q))).ToList();
            if (shown.Count == 0)
            {
                list.Children.Add(T(q.Length > 0 ? "Nothing matches that search." : "No Xbox or Microsoft Store games were found on this PC. Tick the box below to see other apps.", 12, Res("Muted"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(6)));
                return;
            }
            foreach (var a in shown)
            {
                var row = new DockPanel();
                if (a.Added) row.Children.Add(AppTag("ADDED", true).Also(t => DockPanel.SetDock(t, Dock.Right)));
                if (a.Game) row.Children.Add(AppTag("XBOX GAME", false).Also(t => DockPanel.SetDock(t, Dock.Right)));
                row.Children.Add(T(a.Name, 13.5));
                var on = picked?.AppId == a.AppId;
                var b = new Button
                {
                    Style = Style("BareButton"), IsEnabled = !a.Added, Opacity = a.Added ? 0.45 : 1,
                    Content = new Border
                    {
                        Padding = new Thickness(9, 7, 9, 7), CornerRadius = new CornerRadius(6), Child = row,
                        Background = on ? B("#2Ee8873a") : Brushes.Transparent, BorderBrush = on ? B("#80e8873a") : Brushes.Transparent, BorderThickness = new Thickness(1),
                    },
                };
                b.Click += (_, _) => { picked = a; SetTitle(a.Name); DrawApps(); };
                list.Children.Add(b);
            }
        }
        async Task LoadApps()
        {
            list.Children.Clear();
            list.Children.Add(T("Loading your Xbox and Microsoft Store apps…", 12, Res("Muted")).Also(t => t.Margin = new Thickness(6)));
            try
            {
                apps = await StoreApps.ListAsync();
                var added = Core.AddedStoreAppIds();
                foreach (var a in apps) a.Added = added.Contains(a.AppId.ToLowerInvariant());
                DrawApps();
            }
            catch (Exception ex)
            {
                list.Children.Clear();
                list.Children.Add(T(Sentence(ex.Message), 13, B("#f0b1aa"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(6)));
            }
        }
        search.TextChanged += (_, _) => DrawApps();
        all.Checked += (_, _) => DrawApps();
        all.Unchecked += (_, _) => DrawApps();
        var storePane = Stack(Field("Game", Placeholder(search, "Search your Xbox and Store apps")), listBox, all);
        storePane.Visibility = Visibility.Collapsed;

        var tab = "exe";
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        Button? exeTab = null, storeTab = null;
        void SetTab(string t)
        {
            tab = t;
            exePane.Visibility = t == "exe" ? Visibility.Visible : Visibility.Collapsed;
            storePane.Visibility = t == "store" ? Visibility.Visible : Visibility.Collapsed;
            TabLook(exeTab!, t == "exe");
            TabLook(storeTab!, t == "store");
            err("");
            if (t == "store" && apps == null) _ = LoadApps();
        }
        exeTab = TabButton("Program (.exe)", () => SetTab("exe"));
        storeTab = TabButton("Xbox / Microsoft Store", () => SetTab("store"));
        tabs.Children.Add(exeTab);
        tabs.Children.Add(storeTab);
        TabLook(exeTab, true);
        TabLook(storeTab, false);
        var tabBar = new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, -4, 0, 14), Child = tabs };

        var form = Stack(tabBar, exePane, storePane, Field("Name", Placeholder(title, "My Game")), Field("Category", Stack(cat, newCat)), errBox,
            T("You can add details and your own cover art after adding it, with Edit details.", 12, Res("Muted"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 0, 0, 18)));

        var plus = T("+", 96, Res("Ember2"), FontWeights.Light, true);
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        plus.VerticalAlignment = VerticalAlignment.Center;
        OpenModal(new Modal
        {
            Art = new Grid { Background = B("#141821"), Children = { plus } },
            Pill = Pill("NON-IRONITE GAME", "mine"), Title = DlgTitle("Add a game"), Sub = "Play any game on this PC from Ironite Launcher.",
            Notes = form,
            Actions = Actions(Ui.Play("+  Add game", "", () =>
            {
                err("");
                var (category, group, missing) = ReadCategory(cat, newCat);
                if (missing) { err("type a name for the new category"); return; }
                var d = new LauncherCore.Details(title.Text, category, group, "", "", "");
                try
                {
                    if (tab == "store")
                    {
                        if (picked == null) { err("choose a game from the list"); return; }
                        Core.AddStoreApp(title.Text, picked.AppId, picked.Location, d);
                    }
                    else Core.AddCustom(title.Text, path.Text, d);
                    CloseModal();
                    ShowToast($"{(title.Text.Trim().Length > 0 ? title.Text.Trim() : "Your game")} was added to the launcher.");
                    if (_view != "all" && _view != "installed") Go("custom");
                    else Refresh(force: true);
                }
                catch (Exception ex) { err(ex.Message); }
            }, small: true), Btn("Cancel", "GhostButton", CloseModal, small: true)),
        });
        Dispatcher.BeginInvoke(() => path.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static Border AppTag(string text, bool dim) => new()
    {
        BorderBrush = dim ? Res("Line") : B("#735FC489"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2),
        Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = T(Track(text), 9.5, dim ? Res("Muted") : B("#8fe0b0"), FontWeights.Bold, true),
    };

    private static Button TabButton(string text, Action click)
    {
        var b = Btn(new Border { Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0, 0, 0, 2), Child = T(text, 12.5, weight: FontWeights.SemiBold, display: true) }, "BareButton", click);
        b.Margin = new Thickness(0, 0, 6, -1);
        return b;
    }

    private static void TabLook(Button b, bool on)
    {
        var border = (Border)b.Content;
        border.BorderBrush = on ? Res("Ember2") : Brushes.Transparent;
        ((TextBlock)border.Child).Foreground = on ? Res("Text") : Res("Muted");
    }

    /// <summary>Reads a picture and shrinks it to cover size (at most 1200 px), so the saved art stays small.</summary>
    private static (byte[] Data, string Ext) LoadArt(string file)
    {
        BitmapSource src;
        try
        {
            using var s = File.OpenRead(file);
            var dec = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            src = dec.Frames[0];
        }
        catch { throw new InvalidOperationException("that picture couldn’t be opened (use a PNG, JPEG, WebP or GIF)"); }
        var k = Math.Min(1.0, 1200.0 / Math.Max(src.PixelWidth, src.PixelHeight));
        if (k < 1) src = new TransformedBitmap(src, new ScaleTransform(k, k));
        var png = Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase);
        BitmapEncoder enc = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return (ms.ToArray(), png ? ".png" : ".jpg");
    }

    internal void EditDialog(CatalogGame g)
    {
        _modalId = null;
        var cg = g.Custom!;
        (byte[] Data, string Ext)? art = null; // null = unchanged
        var removeArt = false;
        var title = new TextBox { Text = g.Title, MaxLength = 60 };
        var newCat = new TextBox();
        var cat = CategoryBox(g, newCat);
        var dev = new TextBox { Text = cg.Developer ?? "", MaxLength = 60 };
        var players = new TextBox { Text = cg.Players ?? "", MaxLength = 40 };
        var desc = new TextBox { Text = cg.Description ?? "", MaxLength = 1200, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 62, VerticalContentAlignment = VerticalAlignment.Top };
        var errBox = FormError(out var errText);
        var err = ErrorSetter(errBox, errText);

        var achSel = new ComboBox { Width = 130 };
        foreach (var (v, l) in new[] { ("", "None"), ("steam", "Steam"), ("xbox", "Xbox") }) achSel.Items.Add(new ComboBoxItem { Content = l, Tag = v });
        achSel.SelectedIndex = cg.AchSource switch { "steam" => 1, "xbox" => 2, _ => 0 };
        var achId = new TextBox { Text = cg.AchSource == "xbox" ? cg.XboxTitleId ?? "" : cg.SteamAppId ?? "", Margin = new Thickness(8, 0, 0, 0) };
        var achHelp = T("", 12, Res("Muted"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 5, 0, 0));
        string AchSource() => (string)((ComboBoxItem)achSel.SelectedItem).Tag;
        Button? find = null;
        void AchUi()
        {
            var v = AchSource();
            achId.Visibility = find!.Visibility = v.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            achHelp.Text = v == "steam" ? "The number in the game’s Steam store link (store.steampowered.com/app/NUMBER). Find fills it in for games installed by Steam."
                : v == "xbox" ? "Find looks it up from the game, or your Xbox game history (needs your OpenXBL key in Settings)." : "";
        }
        find = Btn("Find", "GhostButton", async () =>
        {
            err("");
            try
            {
                var (source, id, found) = await Core.DetectAchievementsAsync(g.Id);
                if (source.Length == 0)
                {
                    err(AchSource() == "xbox" ? "couldn’t find it automatically; type the title ID" : "couldn’t find it automatically; paste the game’s Steam store link");
                    return;
                }
                achSel.SelectedIndex = source == "xbox" ? 2 : 1;
                achId.Text = id;
                if (found.Length > 0) achHelp.Text = "Found: " + found;
            }
            catch (Exception ex) { err(ex.Message); }
        }, small: true);
        find.Margin = new Thickness(8, 0, 0, 0);
        achSel.SelectionChanged += (_, _) => { achId.Text = ""; AchUi(); };
        var achRow = new DockPanel();
        DockPanel.SetDock(achSel, Dock.Left);
        DockPanel.SetDock(find, Dock.Right);
        achRow.Children.Add(achSel);
        achRow.Children.Add(find);
        achRow.Children.Add(achId);
        AchUi();

        void Preview()
        {
            Func<FrameworkElement> make;
            if (art is { } a)
            {
                using var ms = new MemoryStream(a.Data);
                var img = Decode(ms)!;
                make = () => new Grid { Background = B("#0d0f14"), Children = { Fill(img) } };
            }
            else if (removeArt) make = () => MadeCover(g.Title, Path.Combine(AppPaths.CustomIcons, cg.Id + ".png"), true, 84);
            else make = () => Cover(g, true, 84);
            DlgArt.Child = ArtPanel(make);
        }
        Button? useIcon = null;
        void UseFile(string file)
        {
            err("");
            try { art = LoadArt(file); removeArt = false; Preview(); useIcon!.Visibility = Visibility.Visible; }
            catch (Exception ex) { err(ex.Message); }
        }
        var pick = Btn("🖼  Choose picture…", "GhostButton", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose cover art", Filter = "Pictures (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp" };
            if (dlg.ShowDialog(this) == true) UseFile(dlg.FileName);
        }, small: true);
        useIcon = Btn("Use the game’s icon", "GhostButton", () => { art = null; removeArt = true; Preview(); useIcon!.Visibility = Visibility.Collapsed; }, small: true);
        useIcon.Visibility = string.IsNullOrEmpty(cg.Art) ? Visibility.Collapsed : Visibility.Visible;
        var artRow = Actions(pick, useIcon);

        var two = new Grid();
        two.ColumnDefinitions.Add(new ColumnDefinition());
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        two.ColumnDefinitions.Add(new ColumnDefinition());
        var f1 = Field("Developer", Placeholder(dev, "Who made it"));
        var f2 = Field("Players", Placeholder(players, "1 player"));
        Grid.SetColumn(f2, 2);
        two.Children.Add(f1);
        two.Children.Add(f2);

        var form = Stack(Field("Name", title), Field("Category", Stack(cat, newCat)), two, Field("Description", Placeholder(desc, "What the game is about")),
            Field("Achievements from", Stack(achRow, achHelp)),
            Field("Cover art", artRow, "A tall picture works best (4:5, like 800 × 1000). You can also drop a picture onto the cover."), errBox);

        OpenModal(new Modal
        {
            Art = ArtPanel(() => Cover(g, true, 84)), Pill = Pill("EDIT DETAILS", "mine"), Title = DlgTitle(g.Title), Sub = g.IsStore ? "Xbox / Microsoft Store game" : "Your game",
            Notes = form,
            Actions = Actions(Ui.Play("Save", "", () =>
            {
                err("");
                var (category, group, missing) = ReadCategory(cat, newCat);
                if (missing) { err("type a name for the new category"); return; }
                var src = AchSource();
                var aid = achId.Text.Trim();
                if (src.Length > 0 && !Regex.IsMatch(aid, @"\d")) { err(src == "xbox" ? "type the game’s Xbox title ID, or click Find" : "type the game’s Steam app ID, or click Find"); return; }
                try
                {
                    Core.UpdateCustom(g.Id, new LauncherCore.Details(title.Text, category, group, dev.Text, players.Text, desc.Text, src,
                        src == "steam" ? aid : cg.SteamAppId ?? "", src == "xbox" ? aid : cg.XboxTitleId ?? ""));
                    if (art is { } a) Core.SetCustomArt(g.Id, a.Data, a.Ext);
                    else if (removeArt) Core.SetCustomArt(g.Id, null, "");
                    Refresh(force: true);
                    Details(g.Id);
                    ShowToast("Saved.");
                }
                catch (Exception ex) { err(ex.Message); }
            }, small: true), Btn("Cancel", "GhostButton", () => Details(g.Id), small: true)),
        });
        // Drop a picture onto the cover.
        DlgArt.AllowDrop = true;
        DlgArt.Drop -= OnArtDrop;
        _artDrop = UseFile;
        DlgArt.Drop += OnArtDrop;
    }

    private Action<string>? _artDrop;

    private void OnArtDrop(object sender, DragEventArgs e)
    {
        if (!DlgArt.AllowDrop) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) _artDrop?.Invoke(files[0]);
    }

    private void AccountDialog(string kind)
    {
        _modalId = null;
        var steam = kind == "steam";
        var (profile, key) = Core.AchievementAccounts();
        Control input = steam ? new TextBox { Text = profile } : new PasswordBox();
        var errBox = FormError(out var errText);
        var err = ErrorSetter(errBox, errText);
        var glyph = T(steam ? "♨" : "X", 96, Res("Ember2"), FontWeights.Light, true);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        OpenModal(new Modal
        {
            Art = new Grid { Background = B("#141821"), Children = { glyph } },
            Pill = Pill("ACHIEVEMENTS", "mine"), Title = DlgTitle(steam ? "Steam profile" : "Xbox (OpenXBL key)"),
            Desc = DlgDesc(steam
                ? "Paste your Steam profile link (for example steamcommunity.com/id/yourname), or leave it empty to use the account signed in to Steam on this PC. In Steam, set Profile > Privacy Settings > Game details to Public."
                : "Xbox achievements need a sign-in, so the launcher reads them through OpenXBL. Go to xbl.io, sign in with your Xbox account, copy your API key, and paste it here. The key is stored only on this PC."),
            Notes = Stack(Field(steam ? "Profile link or ID" : "OpenXBL API key", input,
                steam ? null : string.IsNullOrEmpty(key) ? null : "Leave it empty to remove the key."), errBox),
            Actions = Actions(Ui.Play("Save", "", async () =>
            {
                err("");
                try
                {
                    if (steam) Core.SetSteamProfile(((TextBox)input).Text);
                    else Core.SetXblKey(((PasswordBox)input).Password);
                    CloseModal();
                    AchievementService.ClearCache();
                    _ach = null; // the Achievements page reloads with the new account
                    if (_view == "settings") RenderSettings();
                    else if (_view == "achievements") await RenderAchievementsAsync(true);
                }
                catch (Exception ex) { err(ex.Message); }
            }, small: true), Btn("Cancel", "GhostButton", CloseModal, small: true)),
        });
    }
}
