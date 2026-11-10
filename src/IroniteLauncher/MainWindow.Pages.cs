using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Ironite.Launcher.Models;
using Ironite.Launcher.Services;
using Ironite.Launcher.Views;
using static Ironite.Launcher.Views.Ui;

namespace Ironite.Launcher;

/// <summary>The pages: Friends (coming soon), Achievements, Stats, What's New and About.</summary>
public partial class MainWindow
{
    private void PageStart(string title, string? sub, UIElement? pill = null)
    {
        _page.Children.Clear();
        var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        h.Children.Add(H1(title));
        if (pill != null)
        {
            if (pill is FrameworkElement fe) { fe.Margin = new Thickness(12, 0, 0, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
            h.Children.Add(pill);
        }
        _page.Children.Add(h);
        if (sub != null) _page.Children.Add(Muted(sub, 14));
    }

    // ---------------------------------------------------------------- Friends

    private void RenderFriends()
    {
        PageStart("Friends", "Play Ironite games with the people you know.", Pill("COMING SOON", "soon"));
        var grid = new AutoGrid { MinItemWidth = 210, Gap = 14, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 22) };
        foreach (var (t, d) in new[] { ("Friends list", "Add friends and see who's online."), ("Now playing", "See which games your friends are playing right now."), ("Invites", "Invite a friend to a game in one click.") })
            grid.Children.Add(new Border
            {
                BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 16, 18, 16),
                Background = B("#06FFFFFF"), Opacity = 0.75,
                Child = new StackPanel { Children = { T(t, 15, weight: FontWeights.SemiBold, display: true), Muted(d, 13).Also(x => x.Margin = new Thickness(0, 4, 0, 0)) } },
            });
        _page.Children.Add(grid);
        _page.Children.Add(Empty("Coming soon", "Friends isn't available yet. It will arrive in a future launcher update, and the launcher updates itself, so there's nothing you need to do."));
    }

    // ---------------------------------------------------------------- Achievements

    private List<AchievementResult>? _ach;
    private readonly HashSet<string> _achOpen = new();
    private readonly StackPanel _achBody = new();

    private async Task RenderAchievementsAsync(bool refresh)
    {
        PageStart("Achievements", "Achievements from Steam and Xbox for your non-Ironite games, all in one place.");
        var soon = new Border
        {
            BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 16, 0, 18), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var sp = new DockPanel();
        var pill = Pill("COMING SOON", "soon");
        pill.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(pill, Dock.Left);
        sp.Children.Add(pill);
        sp.Children.Add(Muted("Achievements for Ironite games are on the way. They'll arrive in a launcher update, so there's nothing you need to do.").Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        soon.Child = sp;
        _page.Children.Add(soon);
        _page.Children.Add(_achBody);
        if (_ach == null || refresh)
        {
            _achBody.Children.Clear();
            _achBody.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 20), Children = { Spinner(), Muted("Loading achievements from Steam and Xbox…") } });
        }
        else DrawAchievements();
        try { _ach = await Core.AchievementsAsync(refresh); }
        catch (Exception ex)
        {
            _achBody.Children.Clear();
            _achBody.Children.Add(T(Sentence(ex.Message), 13, B("#f0b1aa")));
            return;
        }
        if (_view == "achievements") DrawAchievements();
    }

    private void DrawAchievements()
    {
        _achBody.Children.Clear();
        var linked = _ach ?? new();
        var (profile, key) = Core.AchievementAccounts();
        var (sid, sname) = AchievementService.DetectSteamUser();

        var accounts = new AutoGrid { MinItemWidth = 300, Gap = 12, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        UIElement steamWho = profile.Length > 0 ? T(Regex.Replace(profile, @"^https?://(www\.)?steamcommunity\.com/", "", RegexOptions.IgnoreCase), 13).Also(t => t.ToolTip = profile)
            : sname.Length > 0 ? new TextBlock { FontSize = 13, Foreground = Res("Text"), TextTrimming = TextTrimming.CharacterEllipsis, Inlines = { new System.Windows.Documents.Run(sname + " "), new System.Windows.Documents.Run("(signed in to Steam on this PC)") { Foreground = Res("Muted") } } }
            : T("Not found. Paste your profile link.", 13, Res("Muted"));
        accounts.Children.Add(Account("STEAM", steamWho, profile.Length > 0 ? "Change" : "Use another profile", () => AccountDialog("steam"),
            "Your Steam profile's Game details must be set to Public."));
        accounts.Children.Add(Account("XBOX", string.IsNullOrEmpty(key) ? T("Not connected", 13, Res("Muted")) : T("Connected with your OpenXBL key", 13),
            string.IsNullOrEmpty(key) ? "Add OpenXBL key" : "Change key", () => AccountDialog("xbox"),
            "Xbox achievements need a free key from xbl.io (sign in there with your Xbox account)."));
        _achBody.Children.Add(accounts);

        if (linked.Count == 0)
        {
            _achBody.Children.Add(Empty("No games linked yet", "Open one of your non-Ironite games, choose Edit details, and pick where its achievements come from (Steam or Xbox). Then they'll show up here."));
            return;
        }
        var tot = linked.Sum(r => r.Total);
        var got = linked.Sum(r => r.Unlocked);
        var sum = new DockPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 12) };
        var refresh = Btn("⟳  Refresh", "GhostButton", () => _ = RenderAchievementsAsync(true), small: true);
        DockPanel.SetDock(refresh, Dock.Right);
        sum.Children.Add(refresh);
        var st = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Muted") };
        st.Inlines.Add(new System.Windows.Documents.Run(got.ToString()) { FontSize = 18, FontWeight = FontWeights.Bold, FontFamily = Display, Foreground = Res("Text") });
        st.Inlines.Add(new System.Windows.Documents.Run($"  of {tot} achievements unlocked across {Count(linked.Count)}"));
        sum.Children.Add(st);
        _achBody.Children.Add(sum);

        foreach (var r in linked)
        {
            var g = Game(r.Id);
            if (g == null) continue;
            var pct = r.Total > 0 ? (int)Math.Round(r.Unlocked * 100.0 / r.Total) : 0;
            var open = _achOpen.Contains(r.Id);
            var src = r.Source == "xbox" ? "Xbox" : "Steam";
            var head = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
            var cv = new Border { Width = 44, Height = 55, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 0), Child = Cover(g, false, 18) };
            RoundClip(cv, 6);
            DockPanel.SetDock(cv, Dock.Left);
            head.Children.Add(cv);
            if (r.Error == null)
            {
                var chev = T(open ? "▾" : "▸", 14, Res("Muted")).Also(t => { t.Width = 14; t.VerticalAlignment = VerticalAlignment.Center; });
                DockPanel.SetDock(chev, Dock.Right);
                head.Children.Add(chev);
                var p = T(pct + "%", 16, B("#f3d27a"), FontWeights.Bold, true).Also(t => { t.Margin = new Thickness(14, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center; });
                DockPanel.SetDock(p, Dock.Right);
                head.Children.Add(p);
            }
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(T(g.Title, 15, weight: FontWeights.SemiBold, display: true));
            info.Children.Add(T(src + (r.Error != null ? "" : $" · {r.Unlocked} of {r.Total}" + (r.ScoreTotal > 0 ? $" · {r.Score} / {r.ScoreTotal} G" : "")), 12, Res("Muted")).Also(t => t.Margin = new Thickness(0, 4, 0, 4)));
            if (r.Error != null) info.Children.Add(T(Sentence(r.Error), 12.5, B("#f0b1aa"), wrap: TextWrapping.Wrap));
            else info.Children.Add(new Border { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch, Child = StretchBar(pct) });
            head.Children.Add(info);
            var headBtn = new Button { Style = Style("BareButton"), Content = new Border { Background = Brushes.Transparent, Child = head } };
            var id = r.Id;
            headBtn.Click += (_, _) => { if (!_achOpen.Remove(id)) _achOpen.Add(id); DrawAchievements(); };
            var card = new StackPanel();
            card.Children.Add(headBtn);
            if (open && r.Error == null)
            {
                var items = new AutoGrid { MinItemWidth = 360, Gap = 4, Margin = new Thickness(10, 6, 10, 10) };
                foreach (var it in r.Items) items.Children.Add(AchievementItem(it));
                card.Children.Add(new Border { BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = items });
            }
            _achBody.Children.Add(new Border
            {
                BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Res("Panel"), Child = card,
            });
        }
    }

    private static FrameworkElement StretchBar(int pct)
    {
        var fill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4),
            Background = new LinearGradientBrush(Color.FromRgb(0xf6, 0xb5, 0x6b), Color.FromRgb(0xf3, 0xd2, 0x7a), 0),
        };
        var back = new Border { Height = 6, CornerRadius = new CornerRadius(4), Background = B("#14FFFFFF"), Child = fill };
        back.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * pct / 100.0;
        return back;
    }

    private static UIElement Account(string name, UIElement who, string action, Action click, string note)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition());
        var n = T(Track(name, 2), 13, weight: FontWeights.Bold, display: true).Also(t => { t.Margin = new Thickness(0, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center; });
        g.Children.Add(n);
        if (who is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(who, 1);
        g.Children.Add(who);
        var link = Link(action, click);
        Grid.SetColumn(link, 2);
        g.Children.Add(link);
        var small = T(note, 11.5, B("#7f8592"), wrap: TextWrapping.Wrap).Also(t => t.Margin = new Thickness(0, 4, 0, 0));
        Grid.SetRow(small, 1);
        Grid.SetColumnSpan(small, 3);
        g.Children.Add(small);
        return new Border
        {
            BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12),
            Background = B("#05FFFFFF"), Child = g,
        };
    }

    private static UIElement AchievementItem(Achievement it)
    {
        var g = new Grid { Margin = new Thickness(0), Opacity = it.Unlocked ? 1 : 0.55 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement icon;
        if (!string.IsNullOrEmpty(it.Icon) && Uri.TryCreate(it.Icon, UriKind.Absolute, out var uri))
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            try
            {
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.DecodePixelWidth = 80;
                bmp.EndInit();
            }
            catch { /* bad link */ }
            ImageSource src = bmp;
            if (!it.Unlocked)
            {
                // Grey for locked achievements (once the picture has downloaded).
                var img0 = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill, Source = bmp };
                bmp.DownloadCompleted += (_, _) => { try { img0.Source = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0); } catch { } };
                icon = img0;
            }
            else icon = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill, Source = src };
            RoundClip(icon, 6);
        }
        else icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(6), Background = Res("InputBg"), Child = T("★", 16, Res("Muted")).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }) };
        g.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(T(it.Name, 13.5, weight: FontWeights.SemiBold, wrap: TextWrapping.Wrap));
        if (!string.IsNullOrEmpty(it.Description)) text.Children.Add(T(it.Description, 12, Res("Muted"), wrap: TextWrapping.Wrap));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var when = T((it.Unlocked ? it.UnlockedAt > 0 ? Ago(it.UnlockedAt) : "Unlocked" : "Locked") + (it.Score > 0 ? $" · {it.Score} G" : ""), 11.5, it.Unlocked ? B("#8fe0b0") : Res("Muted"));
        when.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(when, 2);
        g.Children.Add(when);
        return new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(8), Child = g };
    }

    // ---------------------------------------------------------------- Stats

    private static readonly string[] Palette = { "#e8873a", "#5fc489", "#6aa8f0", "#e7c35a", "#c07cf0", "#f07c8c", "#4fd1c5" };

    private void RenderStats()
    {
        var (weekStarts, all, since) = Core.Stats();
        var rows = all.Where(r => Game(r.Id) != null).ToList();
        PageStart("Stats", since > 0
            ? $"Play time is broken down by week from {DateTimeOffset.FromUnixTimeSeconds(since).LocalDateTime:MMMM d, yyyy}, when the launcher started keeping play sessions."
            : "Play time is broken down by week once you’ve played a game with this version of the launcher.");

        var weekly = rows.Where(r => r.Weeks.Any(w => w > 0)).ToList();
        var top = weekly.OrderByDescending(r => r.Weeks.Sum()).Take(Palette.Length - 1).ToList();
        var series = top.Select((r, i) => (Name: Game(r.Id)!.Title, Color: Palette[i], Weeks: r.Weeks)).ToList();
        var rest = weekly.Where(r => !top.Contains(r)).ToList();
        if (rest.Count > 0) series.Add(($"Other games ({rest.Count})", "#6b7280", weekStarts.Select((_, i) => rest.Sum(r => r.Weeks[i])).ToArray()));
        var totals = weekStarts.Select((_, i) => series.Sum(x => x.Weeks[i])).ToArray();

        var thisWeek = totals.LastOrDefault();
        var allTime = rows.Sum(r => r.Total);
        var tiles = new UniformGrid { Columns = 4, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 18) };
        var disk = Size(Core.DiskTotal());
        if (disk.Length == 0) disk = "0 MB";
        foreach (var (v, l) in new[] { (Dur(thisWeek), "This week"), (Dur(allTime), "All time"), (rows.Sum(r => St(r.Id).PlayCount).ToString(), "Games opened"), (disk, "Space used by games") })
            tiles.Children.Add(new Border
            {
                BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 12, 0), Background = B("#05FFFFFF"),
                Child = new StackPanel { Children = { T(v, 24, weight: FontWeights.Bold, display: true), T(l, 12.5, Res("Muted")).Also(t => t.Margin = new Thickness(0, 4, 0, 0)) } },
            });
        _page.Children.Add(tiles);

        var chartCard = new StackPanel();
        chartCard.Children.Add(NotesHead("Play time per week").Also(t => t.Margin = new Thickness(0, 0, 0, 8)));
        if (weekly.Count > 0)
        {
            chartCard.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = Chart(weekStarts, series, totals) });
            var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var x in series)
                legend.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 6),
                    Children = { new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = B(x.Color), Margin = new Thickness(0, 0, 6, 0) }, T(x.Name, 12.5, B("#cfc9bc")) },
                });
            chartCard.Children.Add(legend);
        }
        else chartCard.Children.Add(T("No sessions yet. Play a game and it shows up here.", 12, Res("Muted")).Also(t => t.Margin = new Thickness(0, 16, 0, 16)));
        _page.Children.Add(ChartCard(chartCard));

        // Every game played, as a table.
        var table = new Grid();
        string[] heads = { "Game", "Played", "Opened", "Last played", "Size on disk" };
        foreach (var w in new[] { 2.2, 1, 0.8, 1.2, 1 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition());
        for (var c = 0; c < heads.Length; c++)
        {
            var h = Cell(heads[c], Res("Muted"), true);
            Grid.SetColumn(h, c);
            table.Children.Add(h);
        }
        var rowIx = 1;
        foreach (var r in rows)
        {
            var g = Game(r.Id)!;
            var s = St(r.Id);
            table.RowDefinitions.Add(new RowDefinition());
            var hit = new Button { Style = Style("BareButton"), Content = new Border { Background = Brushes.Transparent } };
            var id = r.Id;
            hit.Click += (_, _) => Details(id);
            hit.MouseEnter += (_, _) => ((Border)hit.Content).Background = B("#08FFFFFF");
            hit.MouseLeave += (_, _) => ((Border)hit.Content).Background = Brushes.Transparent;
            Grid.SetRow(hit, rowIx);
            Grid.SetColumnSpan(hit, heads.Length);
            table.Children.Add(hit);
            string[] vals = { g.Title, Dur(r.Total), s.PlayCount.ToString(), Ago(s.LastPlayed), s.DiskSize > 0 ? Size(s.DiskSize) : "—" };
            for (var c = 0; c < vals.Length; c++)
            {
                var cell = Cell(vals[c], Res("Text"), false);
                cell.IsHitTestVisible = false;
                Grid.SetRow(cell, rowIx);
                Grid.SetColumn(cell, c);
                table.Children.Add(cell);
            }
            rowIx++;
        }
        _page.Children.Add(ChartCard(Stack(NotesHead("All games").Also(t => t.Margin = new Thickness(0, 0, 0, 8)), table)));
    }

    private static Border Cell(string text, Brush fg, bool head) => new()
    {
        BorderBrush = head ? Res("Line") : B("#0AFFFFFF"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, head ? 6 : 7, 8, head ? 6 : 7),
        Child = T(text, 13, fg, head ? FontWeights.Medium : FontWeights.Normal),
    };

    private static Border ChartCard(UIElement child) => new()
    {
        BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14),
        MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 14), Background = Res("Panel"), Child = child,
    };

    /// <summary>Stacked bars of play time per week (720 × 220, scaled to fit).</summary>
    private static Canvas Chart(long[] weekStarts, List<(string Name, string Color, long[] Weeks)> series, long[] totals)
    {
        const double W = 720, H = 220, padL = 44, padB = 26;
        var c = new Canvas { Width = W, Height = H };
        var max = Math.Max(3600, totals.DefaultIfEmpty(0).Max());
        var hours = (int)Math.Ceiling(max / 3600.0);
        var stepH = hours <= 4 ? 1 : hours <= 10 ? 2 : (int)Math.Ceiling(hours / 5.0);
        var bw = (W - padL) / weekStarts.Length;
        TextBlock Ax(string s, double size = 11) => new() { Text = s, FontSize = size, Foreground = B("#8b919c") };
        for (var h = 0; h <= hours; h += stepH)
        {
            var y = H - padB - (h * 3600.0 / (hours * 3600.0)) * (H - padB - 10);
            c.Children.Add(new Line { X1 = padL, X2 = W, Y1 = y, Y2 = y, Stroke = B("#12FFFFFF"), StrokeThickness = 1 });
            var t = Ax(h + "h");
            t.Width = 36;
            t.TextAlignment = TextAlignment.Right;
            Canvas.SetLeft(t, padL - 44);
            Canvas.SetTop(t, y - 8);
            c.Children.Add(t);
        }
        for (var i = 0; i < weekStarts.Length; i++)
        {
            var y = H - padB;
            var x = padL + i * bw + bw * 0.18;
            var w = bw * 0.64;
            foreach (var sr in series)
            {
                var v = sr.Weeks[i];
                if (v == 0) continue;
                var h = v / (hours * 3600.0) * (H - padB - 10);
                y -= h;
                var rect = new Rectangle { Width = w, Height = Math.Max(h, 1), Fill = B(sr.Color), RadiusX = 2, RadiusY = 2, ToolTip = $"{sr.Name}: {Dur(v)}" };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                c.Children.Add(rect);
            }
            var label = Ax(i == weekStarts.Length - 1 ? "This week" : DateTimeOffset.FromUnixTimeSeconds(weekStarts[i]).LocalDateTime.ToString("MMM d", CultureInfo.CurrentCulture));
            label.Width = bw;
            label.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(label, padL + i * bw);
            Canvas.SetTop(label, H - 20);
            c.Children.Add(label);
            if (totals[i] > 0)
            {
                var tot = Ax(Dur(totals[i]));
                tot.Foreground = B("#d9d3c5");
                tot.FontWeight = FontWeights.SemiBold;
                tot.Width = bw;
                tot.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(tot, padL + i * bw);
                Canvas.SetTop(tot, y - 19);
                c.Children.Add(tot);
            }
        }
        return c;
    }

    // ---------------------------------------------------------------- What's New

    private sealed record ChangelogEntry(string Version, string Date, List<(string Name, List<string> Items)> Sections);

    private static List<ChangelogEntry> ParseChangelog(string md)
    {
        var rels = new List<ChangelogEntry>();
        ChangelogEntry? cur = null;
        (string Name, List<string> Items)? sec = null;
        foreach (var line in md.Replace("\r", "").Split('\n'))
        {
            Match m;
            if ((m = Regex.Match(line, @"^##(?!#)\s*\[?([^\]\s]+)\]?\s*-?\s*(.*)$")).Success)
            {
                cur = new ChangelogEntry(m.Groups[1].Value, m.Groups[2].Value, new());
                rels.Add(cur);
                sec = null;
            }
            else if (cur != null && (m = Regex.Match(line, @"^###\s*(.+)$")).Success)
            {
                sec = (m.Groups[1].Value, new List<string>());
                cur.Sections.Add(sec.Value);
            }
            else if (cur != null && (m = Regex.Match(line, @"^\s*[-*]\s+(.+)$")).Success)
            {
                if (sec == null) { sec = ("Changes", new List<string>()); cur.Sections.Add(sec.Value); }
                sec.Value.Items.Add(m.Groups[1].Value);
            }
        }
        return rels;
    }

    private void RenderNews()
    {
        PageStart("What's New", $"You're on version {App.VersionText}. Launcher releases are listed newest first.");
        // The games' own versions, from GitHub.
        var gp = new StackPanel();
        gp.Children.Add(T("Games on GitHub", 20, weight: FontWeights.Bold, display: true).Also(t => t.Margin = new Thickness(0, 0, 0, 6)));
        gp.Children.Add(Muted("Each game installs and updates from its own GitHub repository. Latest versions:"));
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var g in Games.Where(g => Avail(g) && !g.IsWeb && !g.IsLocal))
        {
            var s = St(g.Id);
            var t = new TextBlock { FontSize = 14, Foreground = B("#d9d5ce"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new System.Windows.Documents.Run($"•  {g.Title}{(g.Demo ? " (demo)" : "")} — version {s.LatestVersion ?? s.InstalledVersion}"));
            if (s.Installed)
                t.Inlines.Add(new System.Windows.Documents.Run(s.Update ? $" (you have {s.InstalledVersion})" : " (installed)") { Foreground = Res("Muted") });
            list.Children.Add(t);
        }
        gp.Children.Add(list);
        _page.Children.Add(Rel(gp, dashed: true));

        var rels = ParseChangelog(LauncherCore.EmbeddedText("CHANGELOG.md"));
        for (var i = 0; i < rels.Count; i++)
        {
            var r = rels[i];
            var p = new StackPanel();
            var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            h.Children.Add(T("Version " + r.Version, 20, weight: FontWeights.Bold, display: true));
            if (i == 0) h.Children.Add(Pill("CURRENT", "ready").Also(x => { x.Margin = new Thickness(12, 0, 0, 0); x.VerticalAlignment = VerticalAlignment.Center; }));
            h.Children.Add(T(FmtDate(r.Date), 12.5, Res("Muted")).Also(t => { t.Margin = new Thickness(12, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
            p.Children.Add(h);
            foreach (var (name, items) in r.Sections)
            {
                p.Children.Add(T(Track(name.ToUpperInvariant(), 2), 12, Res("Ember2"), FontWeights.SemiBold, true).Also(t => t.Margin = new Thickness(0, 14, 0, 6)));
                foreach (var it in items)
                {
                    var row = new DockPanel { Margin = new Thickness(4, 4, 0, 0) };
                    var dot = T("•", 14, B("#d9d5ce")).Also(t => t.Margin = new Thickness(0, 0, 10, 0));
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(T(it, 14, B("#d9d5ce"), wrap: TextWrapping.Wrap));
                    p.Children.Add(row);
                }
            }
            _page.Children.Add(Rel(p));
        }
    }

    private static string FmtDate(string d) =>
        DateTime.TryParseExact(d.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture) : d;

    private static Border Rel(UIElement child, bool dashed = false)
    {
        var b = new Border
        {
            Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(dashed ? 0 : 1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 20, 24, 20), Margin = new Thickness(0, 18, 0, 0), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left, Child = child,
        };
        if (!dashed) return b;
        var g = new Grid { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left, Margin = b.Margin };
        b.Margin = new Thickness(0);
        g.Children.Add(b);
        g.Children.Add(new Rectangle { RadiusX = 14, RadiusY = 14, Stroke = Res("Line"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false });
        return new Border { Child = g };
    }

    // ---------------------------------------------------------------- About

    private void RenderAbout()
    {
        var titles = Games.Where(g => !g.IsPlaceholder).ToList();
        var av = Games.Where(Avail).ToList();
        var inst = av.Count(g => St(g.Id).Installed && !g.IsLocal);
        var web = av.Count(g => g.IsWeb);
        var own = av.Count(g => g.IsLocal);
        var offset = MainScroll.VerticalOffset;
        PageStart("About", null);

        var info = AboutCard();
        AboutRow(info, "Product", "Ironite Launcher");
        AboutRow(info, "Launcher version", App.VersionText);
        AboutRow(info, "Launcher platforms", "⊞ Windows · ⌘ macOS (coming soon) · ♦ Linux (coming soon)");
        AboutRow(info, "Build", App.BuildText);
        AboutRow(info, "Games", $"{inst} installed · {av.Count - web - own} to download · {web} web · {titles.Count - av.Count} coming soon" + (own > 0 ? $" · {own} of your own" : ""));
        AboutRow(info, "Total time played", Dur(av.Sum(g => St(g.Id).PlaySeconds)));
        var used = Size(Core.DiskTotal());
        AboutRow(info, "Space used by games", used.Length > 0 ? used : "—");
        AboutRow(info, "Install folder", AppPaths.InstallDir, mono: true, open: () => Platform.OpenFolder(AppPaths.InstallDir));
        AboutRow(info, "Games folder", AppPaths.Games, mono: true, open: () => Platform.OpenFolder(AppPaths.Games));
        AboutRow(info, "Log", AppPaths.Log, mono: true, open: () => Platform.OpenUrl(AppPaths.Log));
        _page.Children.Add(CardOf(info));

        _page.Children.Add(T("Updates", 16, weight: FontWeights.Bold, display: true).Also(t => t.Margin = new Thickness(0, 26, 0, 0)));
        var up = AboutCard();
        var sw = new ToggleButton { Style = Style("SwitchButton"), IsChecked = Core.AutoUpdate, VerticalAlignment = VerticalAlignment.Center };
        sw.Click += (_, _) =>
        {
            Core.SetAutoUpdate(sw.IsChecked == true);
            ShowToast($"Automatic game updates are {(sw.IsChecked == true ? "on" : "off")}.");
            Refresh(force: true);
        };
        var swRow = new StackPanel { Orientation = Orientation.Horizontal };
        swRow.Children.Add(sw);
        swRow.Children.Add(T(Core.AutoUpdate ? "On" : "Off", 14, Res("Muted")).Also(t => { t.Margin = new Thickness(8, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
        AboutRow(up, "Update games automatically", swRow, "Installed games download new versions from GitHub as soon as they're released (never while you're playing).");
        AboutRow(up, "Last checked", Core.Checking ? "Checking now…" : Core.LastCheck is { } lc ? Since(lc) + (Core.CheckError.Length > 0 ? " (couldn’t reach GitHub)" : "") : "—");
        AboutRow(up, "Where updates come from", "Each game's own GitHub repository · launcher: " + GitHubService.LauncherRepo);
        _page.Children.Add(CardOf(up));
        _page.Children.Add(Muted("All games, art and music in Ironite Launcher are original works by Ironite. Copyright © 2026 Ironite. All rights reserved.", 14));
        Dispatcher.BeginInvoke(() => MainScroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static StackPanel AboutCard() => new();

    private void AboutRow(StackPanel card, string label, object value, string? small = null, bool mono = false, Action? open = null)
    {
        var row = new DockPanel { Margin = new Thickness(0) };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        left.Children.Add(T(label, 14, Res("Muted")));
        if (small != null) left.Children.Add(T(small, 11.5, B("#7f8592"), wrap: TextWrapping.Wrap).Also(t => { t.MaxWidth = 330; t.Margin = new Thickness(0, 3, 0, 0); }));
        FrameworkElement right = value switch
        {
            UIElement u when u is FrameworkElement fe => fe,
            _ => mono ? T(value.ToString() ?? "", 12, wrap: TextWrapping.Wrap).Also(t => { t.FontFamily = new FontFamily("Consolas"); t.TextAlignment = TextAlignment.Right; t.MaxWidth = 380; })
                      : T(value.ToString() ?? "", 14, weight: FontWeights.SemiBold, wrap: TextWrapping.Wrap).Also(t => { t.TextAlignment = TextAlignment.Right; t.MaxWidth = 380; }),
        };
        right.HorizontalAlignment = HorizontalAlignment.Right;
        right.VerticalAlignment = VerticalAlignment.Center;
        if (open != null)
        {
            var b = Btn(right, "BareButton", open, "Open");
            b.HorizontalAlignment = HorizontalAlignment.Right;
            right = b;
        }
        DockPanel.SetDock(left, Dock.Left);
        row.Children.Add(left);
        row.Children.Add(right);
        var line = new Border { Padding = new Thickness(0, 13, 0, 13), BorderBrush = Res("Line"), BorderThickness = new Thickness(0, card.Children.Count > 0 ? 1 : 0, 0, 0), Child = row };
        card.Children.Add(line);
    }

    private static Border CardOf(StackPanel rows) => new()
    {
        Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
        Padding = new Thickness(22, 6, 22, 6), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 18), Child = rows,
    };

}
