using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ironite.Launcher.Models;
using Ironite.Launcher.Services;
using Ironite.Launcher.Services.Friends;
using Ironite.Launcher.Views;
using static Ironite.Launcher.Views.Ui;

namespace Ironite.Launcher;

/// <summary>
/// Player profiles: your own profile card (picture, theme, favourite game), editing it, and a friend's profile
/// with their games, play time and achievements.
/// </summary>
public partial class MainWindow
{
    private static readonly (string Id, string Name, string Light, string Dark)[] Themes =
    {
        ("ember", "Ember", "#e8873a", "#5c2a0c"), ("ocean", "Ocean", "#3a8fe8", "#0f2c55"), ("forest", "Forest", "#4caf6e", "#123d24"),
        ("violet", "Violet", "#9b6ae8", "#2e1758"), ("rose", "Rose", "#e85a8a", "#561630"), ("gold", "Gold", "#e7c35a", "#55400e"),
        ("slate", "Slate", "#8a96a8", "#262d39"),
    };

    private static Color ThemeColor(string theme, bool dark = false)
    {
        var t = Themes.FirstOrDefault(x => x.Id == theme);
        if (t.Id == null) t = Themes[0];
        return (Color)ColorConverter.ConvertFromString(dark ? t.Dark : t.Light);
    }

    /// <summary>A profile banner in the player's theme colour.</summary>
    private static Brush ThemeBanner(string theme) => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0xCC, ThemeColor(theme).R, ThemeColor(theme).G, ThemeColor(theme).B), 0),
            new GradientStop(ThemeColor(theme, dark: true), 0.65),
            new GradientStop(Color.FromRgb(0x1e, 0x22, 0x2b), 1),
        },
    };

    // ---------------------------------------------------------------- your profile card (top of Friends)

    private UIElement MyProfileCard()
    {
        var me = Fr.MyProfile;
        var theme = me?.Theme ?? "ember";
        var grid = new DockPanel();
        var av = Avatar(Fr.Username, null, 64, Fr.MyId, theme);
        av.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(av, Dock.Left);
        grid.Children.Add(av);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var edit = Btn("✎  Edit profile", "GhostButton", EditProfileDialog, small: true);
        edit.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(edit);
        actions.Children.Add(Btn("View", "GhostButton", () => OpenProfile(Fr.MyId), small: true));
        DockPanel.SetDock(actions, Dock.Right);
        grid.Children.Add(actions);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(T(Fr.Username, 22, weight: FontWeights.Bold, display: true));
        var line = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (me?.FavoriteTitle is { } fav)
        {
            line.Children.Add(T("★ Favorite: ", 13, B("#f3d27a")));
            line.Children.Add(T(fav + "   ", 13, weight: FontWeights.SemiBold));
        }
        line.Children.Add(Link("Sign out", async () =>
        {
            await Fr.SignOutAsync();
            _fMode = "signin";
            Refresh(force: true);
        }, 12.5));
        text.Children.Add(line);
        grid.Children.Add(text);

        return new Border
        {
            Background = ThemeBanner(theme), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 4, 0, 16), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Child = grid,
        };
    }

    // ---------------------------------------------------------------- a profile (a friend's, or your own)

    private string? _profileId;
    private Profile? _prof;
    private FriendLibrary? _profLib;
    private string? _profError;
    private readonly HashSet<string> _profOpen = new();

    private async void OpenProfile(string id)
    {
        _profileId = id;
        _prof = null;
        _profLib = null;
        _profError = null;
        _profOpen.Clear();
        RenderFriends();
        MainScroll.ScrollToTop();
        try { (_prof, _profLib) = await Fr.LoadProfileAsync(id); }
        catch (Exception ex) { _profError = ex.Message; }
        if (_profileId == id && _view == "friends") RenderFriends();
    }

    private void RenderProfile()
    {
        _page.Children.Clear();
        _page.Children.Add(Link("‹  Friends", () => { _profileId = null; RenderFriends(); }, 13).Also(b => b.Margin = new Thickness(0, 0, 0, 10)));
        var id = _profileId!;
        var mine = id == Fr.MyId;
        var friend = Fr.Friends.FirstOrDefault(f => f.Id == id);
        var name = _prof?.Username ?? friend?.Username ?? (mine ? Fr.Username : "");
        var theme = _prof?.Theme ?? friend?.Theme ?? "ember";

        // Banner: picture, name, status, favourite game.
        var head = new DockPanel();
        var av = Avatar(name, mine ? null : friend?.Online, 92, id, theme);
        av.Margin = new Thickness(0, 0, 20, 0);
        DockPanel.SetDock(av, Dock.Left);
        head.Children.Add(av);
        if (_prof?.FavoriteGame is { } favId)
        {
            var fg = Game(favId);
            var favCard = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 92 };
            var cover = new Border { Width = 92, CornerRadius = new CornerRadius(8), BorderBrush = B("#33FFFFFF"), BorderThickness = new Thickness(1),
                Child = new RatioBox { Ratio = 1.25, Child = fg != null ? Cover(fg, false, 28) : MadeCover(_prof.FavoriteTitle ?? favId, null, false, 28) } };
            RoundClip((FrameworkElement)cover.Child, 7);
            favCard.Children.Add(T(Track("FAVORITE", 2), 10, B("#f3d27a"), FontWeights.Bold, true).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 5); }));
            favCard.Children.Add(cover);
            favCard.Children.Add(T(_prof.FavoriteTitle ?? fg?.Title ?? "", 11.5, wrap: TextWrapping.Wrap).Also(t => { t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(0, 5, 0, 0); }));
            DockPanel.SetDock(favCard, Dock.Right);
            head.Children.Add(favCard);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(T(name, 30, weight: FontWeights.Bold, display: true));
        if (mine) text.Children.Add(T("This is how your friends see you.", 13.5, B("#d0ccc5")));
        else if (friend != null)
            text.Children.Add(friend.Online && friend.PlayingTitle != null ? T("Playing " + friend.PlayingTitle, 14, B("#8fe0b0"), FontWeights.SemiBold)
                : friend.Online ? T("Online", 14, B("#8fe0b0"))
                : T(friend.LastSeen is { } ls ? "Last online " + LastOnline(ls) : "Offline", 14, B("#d0ccc5")));
        if (friend?.Since is { } since) text.Children.Add(T($"Friends since {since.ToLocalTime():MMMM d, yyyy}", 12.5, B("#b9b1a5")).Also(t => t.Margin = new Thickness(0, 4, 0, 0)));
        if (mine)
        {
            var edit = Btn("✎  Edit profile", "GhostButton", EditProfileDialog, small: true);
            edit.HorizontalAlignment = HorizontalAlignment.Left;
            edit.Margin = new Thickness(0, 12, 0, 0);
            text.Children.Add(edit);
        }
        head.Children.Add(text);
        _page.Children.Add(new Border
        {
            Background = ThemeBanner(theme), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24, 22, 24, 22), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch, Child = head,
        });

        if (_profError != null)
        {
            _page.Children.Add(T(_profError, 13, B("#f0b1aa"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 18, 0, 0)));
            return;
        }
        if (_profLib == null)
        {
            _page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0), Children = { Spinner(), Muted($"Loading {name}'s games…") } });
            return;
        }
        if (!_profLib.Shared)
        {
            _page.Children.Add(Empty(mine ? "You're not sharing your games" : $"{name} doesn't share their games",
                mine ? "Turn on sharing in Settings to let friends see your games, play time and achievements." : "They've chosen to keep their games, play time and achievements private."));
            return;
        }

        var games = _profLib.Games;
        var tiles = new UniformGrid { Columns = 3, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 6) };
        var got = games.Sum(g => g.AchUnlocked);
        var total = games.Sum(g => g.AchTotal);
        foreach (var (v, l) in new[] { (Dur(games.Sum(g => g.PlaySeconds)), "Time played"), (games.Count.ToString(), games.Count == 1 ? "Game" : "Games"),
                     (total > 0 ? $"{got} / {total}" : got.ToString(), "Achievements") })
            tiles.Children.Add(new Border
            {
                BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 12, 0), Background = Res("Panel"),
                Child = new StackPanel { Children = { T(v, 22, weight: FontWeights.Bold, display: true), T(l, 12.5, Res("Muted")).Also(t => t.Margin = new Thickness(0, 3, 0, 0)) } },
            });
        _page.Children.Add(tiles);

        _page.Children.Add(FriendsHead("GAMES", games.Count));
        if (games.Count == 0)
            _page.Children.Add(Muted(mine ? "Play a game and it shows up here." : $"{name} hasn't played anything yet.", 13.5));
        foreach (var g in games) _page.Children.Add(SharedGameRow(g, mine));
    }

    private UIElement SharedGameRow(SharedGame sg, bool mine)
    {
        var local = Game(sg.Id);
        var open = _profOpen.Contains(sg.Id);
        var row = new DockPanel();
        var cover = new Border { Width = 48, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new RatioBox { Ratio = 1.25, Child = local != null ? Cover(local, false, 18) : MadeCover(sg.Title, null, false, 18) } };
        RoundClip((FrameworkElement)cover.Child, 6);
        DockPanel.SetDock(cover, Dock.Left);
        row.Children.Add(cover);

        if (sg.AchTotal > 0)
        {
            var pct = (int)Math.Round(sg.AchUnlocked * 100.0 / sg.AchTotal);
            var ach = new StackPanel { Width = 170, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            var top = new DockPanel();
            var p = T(pct + "%", 13, B("#f3d27a"), FontWeights.Bold, true);
            DockPanel.SetDock(p, Dock.Right);
            top.Children.Add(p);
            top.Children.Add(T($"★ {sg.AchUnlocked} of {sg.AchTotal}", 12.5, B("#d0ccc5")));
            ach.Children.Add(top);
            ach.Children.Add(StretchBar(pct).Also(b => b.Margin = new Thickness(0, 5, 0, 0)));
            DockPanel.SetDock(ach, Dock.Right);
            row.Children.Add(ach);
        }

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(T(sg.Title, 15, weight: FontWeights.SemiBold, display: true));
        if (sg.Kind == "own") title.Children.Add(Pill("NON-IRONITE", "mine").Also(x => { x.Margin = new Thickness(10, 0, 0, 0); x.VerticalAlignment = VerticalAlignment.Center; }));
        info.Children.Add(title);
        var meta = $"Played {Dur(sg.PlaySeconds)} · Last played {Ago(sg.LastPlayed).ToLowerInvariant()}";
        if (!mine && local != null && St(local.Id).PlaySeconds > 0) meta += $" · You: {Dur(St(local.Id).PlaySeconds)}";
        info.Children.Add(T(meta, 12.5, Res("Muted")).Also(t => t.Margin = new Thickness(0, 3, 0, 0)));
        row.Children.Add(info);

        var body = new StackPanel();
        body.Children.Add(row);
        if (open && sg.Achievements is { Count: > 0 } list)
        {
            var items = new AutoGrid { MinItemWidth = 340, Gap = 4, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var a in list.OrderByDescending(x => x.Unlocked).ThenByDescending(x => x.At))
                items.Children.Add(AchievementItem(new Achievement { Name = a.Name, Description = a.Description, Icon = a.Icon, Unlocked = a.Unlocked, UnlockedAt = a.At }));
            body.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0), Child = items });
        }
        var card = new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 16, 10), Margin = new Thickness(0, 0, 0, 8), Child = body,
        };
        if (sg.Achievements is not { Count: > 0 }) return card.Also(c => c.MaxWidth = 860);
        var b = new Button { Style = Style("BareButton"), Content = card, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = open ? null : "Show achievements" };
        b.MouseEnter += (_, _) => card.BorderBrush = B("#4a5163");
        b.MouseLeave += (_, _) => card.BorderBrush = Res("Line");
        b.Click += (_, _) => { if (!_profOpen.Remove(sg.Id)) _profOpen.Add(sg.Id); RenderFriends(); };
        return b;
    }

    internal void OpenProfileForTour(string id) { _view = "friends"; OpenProfile(id); }
    internal void EditProfileForTour() => EditProfileDialog();

    // ---------------------------------------------------------------- editing your profile

    private void EditProfileDialog()
    {
        _modalId = null;
        var me = Fr.MyProfile;
        var theme = me?.Theme ?? "ember";
        byte[]? newPicture = null; // null = keep, empty = remove
        var errBox = FormError(out var errText);
        var err = ErrorSetter(errBox, errText);

        var preview = new Border();
        void Draw()
        {
            ImageSource? pic = null;
            if (newPicture is { Length: > 0 }) { using var ms = new MemoryStream(newPicture); pic = Decode(ms); }
            preview.Child = newPicture is { Length: 0 } ? Avatar(Fr.Username, null, 84, null, theme) : Avatar(Fr.Username, null, 84, Fr.MyId, theme, pic);
        }
        Draw();
        var choose = Btn("🖼  Choose picture…", "GhostButton", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose a profile picture", Filter = "Pictures (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp" };
            if (dlg.ShowDialog(this) != true) return;
            try { newPicture = AvatarJpeg(dlg.FileName); err(""); Draw(); }
            catch (Exception ex) { err(ex.Message); }
        }, small: true);
        var remove = Btn("Remove", "GhostButton", () => { newPicture = Array.Empty<byte>(); Draw(); }, small: true);
        var picRow = new DockPanel();
        preview.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(preview, Dock.Left);
        picRow.Children.Add(preview);
        picRow.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Actions(choose, remove), Muted("A square picture works best. It's shrunk to a small size.", 12) } });

        // Theme swatches.
        var swatches = new WrapPanel();
        void DrawSwatches()
        {
            swatches.Children.Clear();
            foreach (var t in Themes)
            {
                var on = t.Id == theme;
                var dot = new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = ThemeBanner(t.Id),
                    BorderBrush = on ? Brushes.White : B("#33FFFFFF"), BorderThickness = new Thickness(on ? 3 : 1),
                };
                var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 6), Children = { dot, T(t.Name, 11, on ? Res("Text") : Res("Muted")).Also(x => { x.HorizontalAlignment = HorizontalAlignment.Center; x.Margin = new Thickness(0, 4, 0, 0); }) } };
                var id = t.Id;
                swatches.Children.Add(Btn(cell, "BareButton", () => { theme = id; DrawSwatches(); Draw(); }, t.Name));
            }
        }
        DrawSwatches();

        // Favourite game: any game in the library that's available, installed, or one of your own.
        var fav = new ComboBox();
        fav.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });
        foreach (var g in Games.Where(g => !g.IsPlaceholder && (St(g.Id).Available || St(g.Id).Installed)).OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase))
            fav.Items.Add(new ComboBoxItem { Content = g.Title + (g.IsLocal ? "  (non-Ironite)" : ""), Tag = g.Id });
        fav.SelectedItem = fav.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (me?.FavoriteGame ?? "")) ?? fav.Items[0];

        OpenModal(new Modal
        {
            Pill = Pill("YOUR PROFILE", "mine"), Title = DlgTitle("Edit profile"), Sub = "Your friends see your picture, theme and favorite game.",
            Notes = Stack(Field("Picture", picRow), Field("Theme", swatches), Field("Favorite game", fav), errBox),
            Actions = Actions(Ui.Play("Save", "", async () =>
            {
                err("");
                var favId = (string)((ComboBoxItem)fav.SelectedItem).Tag;
                try
                {
                    await Fr.SaveProfileAsync(theme, favId.Length > 0 ? favId : null, favId.Length > 0 ? Game(favId)?.Title : null, newPicture);
                    CloseModal();
                    ShowToast("Your profile was saved.");
                    if (_profileId == Fr.MyId) OpenProfile(Fr.MyId);
                    else RenderFriends();
                }
                catch (Exception ex) { err(ex.Message); }
            }, small: true), Btn("Cancel", "GhostButton", CloseModal, small: true)),
        });
    }

    /// <summary>A picture cut to a centred square and shrunk to 160 × 160 JPEG (about 10 KB) for a profile picture.</summary>
    private static byte[] AvatarJpeg(string file)
    {
        BitmapSource src;
        try
        {
            using var s = File.OpenRead(file);
            src = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch { throw new InvalidOperationException("That picture couldn’t be opened. Use a PNG, JPEG, WebP or GIF."); }
        var side = Math.Min(src.PixelWidth, src.PixelHeight);
        src = new CroppedBitmap(src, new Int32Rect((src.PixelWidth - side) / 2, (src.PixelHeight - side) / 2, side, side));
        var k = 160.0 / side;
        src = new TransformedBitmap(src, new ScaleTransform(k, k));
        // JPEG has no transparency: put the picture on the launcher's dark colour first.
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(B("#1e222b"), null, new Rect(0, 0, 160, 160));
            dc.DrawImage(src, new Rect(0, 0, 160, 160));
        }
        var rtb = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new JpegBitmapEncoder { QualityLevel = 85 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
