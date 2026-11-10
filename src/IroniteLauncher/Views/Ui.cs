using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ironite.Launcher.Models;
using Ironite.Launcher.Services;

namespace Ironite.Launcher.Views;

/// <summary>Small helpers for building the launcher's UI in code, in the Ironite look.</summary>
public static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    public static FontFamily Display => (FontFamily)Application.Current.Resources["Display"];
    public static FontFamily Body => (FontFamily)Application.Current.Resources["Body"];
    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new();

    public static SolidColorBrush B(string hex)
    {
        if (Brushes.TryGetValue(hex, out var b)) return b;
        b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return Brushes[hex] = b;
    }

    /// <summary>Letter spacing, which WPF text doesn't have: thin spaces between the letters of short uppercase labels.</summary>
    public static string Track(string s, int level = 1)
    {
        var gap = level switch { 1 => " ", 2 => " ", _ => "  " };
        return string.Join(gap, s.EnumerateRunes().Select(r => r.ToString()));
    }

    public static TextBlock T(string text, double size = 14, Brush? fg = null, FontWeight? weight = null, bool display = false, TextWrapping wrap = TextWrapping.NoWrap)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, Foreground = fg ?? Res("Text"), FontWeight = weight ?? FontWeights.Normal,
            FontFamily = display ? Display : Body, TextWrapping = wrap, TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        return t;
    }

    public static TextBlock Muted(string text, double size = 13, TextWrapping wrap = TextWrapping.Wrap) => T(text, size, Res("Muted"), wrap: wrap);

    public static TextBlock H1(string text) => T(text, 32, weight: FontWeights.Bold, display: true);

    public static Button Btn(object content, string style, Action? click, string? tip = null, bool small = false)
    {
        var b = new Button { Content = content, Style = Style(style), ToolTip = tip };
        if (small) { b.Padding = new Thickness(16, 10, 16, 10); b.FontSize = 14; }
        if (click != null) b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    public static Button Play(string html, string cls, Action? click, bool small = false)
    {
        var b = Btn(html, "IrButton", click, small: small);
        b.Background = cls switch { "inst" => Res("InstBrush"), "upd" => Res("UpdBrush"), _ => Res("PlayBrush") };
        b.Foreground = cls switch { "inst" => B("#06180e"), "upd" => B("#1d1504"), _ => B("#1b0f06") };
        b.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 20, ShadowDepth = 6, Direction = 270, Opacity = 0.35,
            Color = cls switch { "inst" => Color.FromRgb(95, 196, 137), "upd" => Color.FromRgb(231, 195, 90), _ => Color.FromRgb(232, 135, 58) },
        };
        if (click == null) { b.IsEnabled = false; b.Effect = null; }
        return b;
    }

    public static Button Link(string text, Action click, double size = 12.5)
    {
        var b = Btn(text, "LinkButton", click);
        b.FontSize = size;
        return b;
    }

    /// <summary>The small labels on tiles and dialogs ("✓ INSTALLED", "COMING SOON").</summary>
    public static Border Pill(string text, string kind, bool solid = false)
    {
        var (bg, fg, line) = kind switch
        {
            "soon" => ("#D9121419", "#f6b56b", "#80e8873a"),
            "ready" or "inst" => ("#295FC489", "#8fe0b0", "#735FC489"),
            "miss" => ("#26E65A50", "#f0a39b", "#73E65A50"),
            "avail" => ("#D9121419", "#e9e3d6", "#4DFFFFFF"),
            "upd" => ("#29E7C35A", "#f1d78a", "#80E7C35A"),
            "web" => ("#295E9EF0", "#a9cbf7", "#805E9EF0"),
            "mine" => ("#2478A0FF", "#aac4ff", "#7378A0FF"),
            _ => ("#D9121419", "#ece7df", "#4DFFFFFF"),
        };
        // Badges over cover art get a solid dark backing so they read on any colour.
        var inner = new Border
        {
            Background = B(bg), BorderBrush = B(line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 5, 9, 5), HorizontalAlignment = HorizontalAlignment.Left,
            Child = T(Track(text), 10.5, B(fg), FontWeights.Bold, true),
        };
        if (!solid) return inner;
        return new Border { Background = B("#E00E1014"), CornerRadius = new CornerRadius(6), Child = inner, HorizontalAlignment = HorizontalAlignment.Left };
    }

    public static Border DemoTag(bool big = false) => new()
    {
        Background = new LinearGradientBrush(Color.FromRgb(0xff, 0xd3, 0x6b), Color.FromRgb(0xff, 0x9f, 0x43), 90),
        CornerRadius = new CornerRadius(4), Padding = big ? new Thickness(8, 5, 8, 4) : new Thickness(6, 3, 6, 2),
        Margin = new Thickness(big ? 14 : 8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = T(Track("DEMO", 2), big ? 12 : 9.5, B("#1a0b22"), FontWeights.ExtraBold, true),
        ToolTip = "A free demo of the game",
    };

    public static Border Card(UIElement child, double maxWidth = double.PositiveInfinity, Thickness? padding = null, bool dashed = false) => new()
    {
        Background = dashed ? Brushes_.Transparent : Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = padding ?? new Thickness(22, 6, 22, 6), MaxWidth = maxWidth,
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 18), Child = child,
    };

    private static class Brushes_ { public static readonly Brush Transparent = System.Windows.Media.Brushes.Transparent; }

    public static Border Empty(string title, string text) => new()
    {
        BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(34),
        MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left,
        Child = new StackPanel
        {
            Children =
            {
                Center(T(title, 18, weight: FontWeights.Bold, display: true)),
                Center(Muted(text, 14)),
            },
        },
    };

    private static TextBlock Center(TextBlock t) { t.TextAlignment = TextAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 6); return t; }

    public static WrapPanel Row(double gap, params UIElement[] items)
    {
        var w = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items)
        {
            if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 0, gap, gap / 2);
            w.Children.Add(i);
        }
        return w;
    }

    /// <summary>"Label: value" meta text with a bold value.</summary>
    public static TextBlock Meta(string label, string value, Brush? fg = null)
    {
        var t = new TextBlock { FontSize = 12, Foreground = fg ?? B("#b9b1a5"), Margin = new Thickness(0, 0, 14, 4) };
        t.Inlines.Add(new System.Windows.Documents.Run(label + " "));
        t.Inlines.Add(new System.Windows.Documents.Run(value) { FontWeight = FontWeights.SemiBold, Foreground = B("#efe6d6") });
        return t;
    }

    public static void RoundClip(FrameworkElement fe, double radius)
    {
        void Update() => fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), radius, radius);
        fe.SizeChanged += (_, _) => Update();
        if (fe.ActualWidth > 0) Update();
    }

    public static Border Bar(double pct, double width, double height, Brush fill, Brush? back = null) => new()
    {
        Width = width, Height = height, CornerRadius = new CornerRadius(height / 2), Background = back ?? B("#40000000"),
        VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true,
        Child = new Border { Width = Math.Max(0, Math.Min(1, pct / 100)) * width, HorizontalAlignment = HorizontalAlignment.Left, Background = fill, CornerRadius = new CornerRadius(height / 2) },
    };

    public static UIElement Spinner()
    {
        var e = new Ellipse_();
        return e.Element;
    }

    private sealed class Ellipse_
    {
        public readonly FrameworkElement Element;
        public Ellipse_()
        {
            var arc = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, StrokeThickness = 2, Stroke = B("#33FFFFFF") };
            var top = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M5,0 A5,5 0 0 1 10,5"), Stroke = Res("Ember2"), StrokeThickness = 2, Width = 10, Height = 10,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var rot = new RotateTransform();
            top.RenderTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            Element = new Grid { Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { arc, top } };
        }
    }

    // ---------------------------------------------------------------- formatting (as launcher 2.x)

    public static string Ago(long ts)
    {
        if (ts == 0) return "Never";
        var d = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
        var days = (DateTime.Today - d.Date).Days;
        if (days == 0) return "Today";
        if (days == 1) return "Yesterday";
        if (days < 7) return days + " days ago";
        return d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    public static string Since(DateTime utc)
    {
        var s = Math.Max(0, (DateTime.UtcNow - utc).TotalSeconds);
        if (s < 60) return "just now";
        if (s < 3600) return (int)(s / 60) + " min ago";
        return (int)(s / 3600) + " h ago";
    }

    public static string Dur(long sec)
    {
        sec = Math.Max(0, sec);
        if (sec < 60) return sec > 0 ? "< 1m" : "0m";
        var h = sec / 3600;
        var m = sec % 3600 / 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    public static string Size(long b)
    {
        if (b <= 0) return "";
        if (b < 1048576) return Math.Max(1, (int)Math.Round(b / 1024.0)) + " KB";
        if (b < 1073741824) return (b / 1048576.0).ToString(b < 10485760 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB";
        return (b / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    public static string Opens(int n) => $"{n} {(n == 1 ? "time" : "times")}";

    public static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    public static string Sentence(string s)
    {
        s = Cap(s.Trim());
        return s.EndsWith('.') || s.EndsWith('!') || s.EndsWith('?') ? s : s + ".";
    }

    // ---------------------------------------------------------------- pictures

    private static readonly Dictionary<string, ImageSource?> Images = new();

    public static ImageSource? Pack(string path)
    {
        if (Images.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            var uri = new Uri("pack://application:,,,/" + path, UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info != null)
            {
                using var s = info.Stream;
                img = Decode(s);
            }
        }
        catch { /* not built in */ }
        return Images[path] = img;
    }

    public static ImageSource? FromFile(string path, int decodeWidth = 0)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (Images.TryGetValue(key, out var cached)) return cached;
            using var s = File.OpenRead(path);
            return Images[key] = Decode(s, decodeWidth);
        }
        catch { return null; }
    }

    public static BitmapImage? Decode(Stream s, int decodeWidth = 0)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = s;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static readonly HashSet<string> Fetching = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// A game's cover: built in, or (for games added to the catalog after this launcher was built) downloaded
    /// from the launcher's GitHub repository and kept in the cache. Null until one is available.
    /// </summary>
    public static ImageSource? GameCover(string id, bool hero, Action? arrived = null)
    {
        var name = hero ? id + "-hero.jpg" : id + ".jpg";
        if (Pack("Covers/" + name) is { } built) return built;
        var file = Path.Combine(AppPaths.CoverCache, name);
        if (FromFile(file) is { } got) return got;
        lock (Fetching)
        {
            if (!Fetching.Add(name)) return null;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var resp = await Http.GetAsync(GitHubService.CoverUrl + name);
                if (!resp.IsSuccessStatusCode) return;
                Directory.CreateDirectory(AppPaths.CoverCache);
                await File.WriteAllBytesAsync(file, await resp.Content.ReadAsByteArrayAsync());
                if (arrived != null) Application.Current?.Dispatcher.BeginInvoke(arrived);
            }
            catch { /* offline: the generic cover stays */ }
        });
        return null;
    }

    public static Color Hsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            double Hue(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 1 / 2.0) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            }
            r = Hue(h + 1 / 3.0); g = Hue(h); b = Hue(h - 1 / 3.0);
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    /// <summary>
    /// A cover for a game without art: a colour from its name, the program's icon if there is one, and its
    /// initials and name (the look launcher 2.x gave the player's own games).
    /// </summary>
    public static FrameworkElement MadeCover(string title, string? iconPath, bool showName = true, double initialsSize = 64)
    {
        var h = 0;
        foreach (var ch in title) h = (h * 31 + ch) % 360;
        var bg = new RadialGradientBrush
        {
            Center = new Point(0.3, 0.15), GradientOrigin = new Point(0.3, 0.15), RadiusX = 1.2, RadiusY = 0.9,
            GradientStops =
            {
                new GradientStop(Hsl(h, 0.55, 0.42), 0),
                new GradientStop(Hsl(h + 35, 0.45, 0.16), 0.7),
                new GradientStop(Color.FromRgb(0x0d, 0x0f, 0x14), 1),
            },
        };
        var g = new Grid { Background = bg, ClipToBounds = true };
        var initials = string.Concat(title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpper(w[0])));
        var icon = iconPath != null ? FromFile(iconPath) : null;
        if (icon != null)
        {
            var img = new Image
            {
                Source = icon, Stretch = Stretch.Uniform, Width = 82, Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 8, Opacity = 0.45, Direction = 270 },
            };
            g.SizeChanged += (_, e) => img.Width = Math.Max(1, Math.Min(128, e.NewSize.Width * 0.46));
            g.Children.Add(img);
        }
        else
        {
            var t = T(initials.Length > 0 ? initials : "?", initialsSize, B("#E0FFFFFF"), FontWeights.ExtraBold, true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Opacity = 0.4, Direction = 270 };
            g.Children.Add(new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(12) });
        }
        if (showName)
        {
            var n = T(Track(title.ToUpperInvariant()), 12, B("#B8FFFFFF"), FontWeights.Bold, true);
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.VerticalAlignment = VerticalAlignment.Bottom;
            n.Margin = new Thickness(10, 0, 10, 12);
            g.Children.Add(n);
        }
        return g;
    }

    public static Image Fill(ImageSource src) => new() { Source = src, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

}

/// <summary>A box whose height is its width times <see cref="Ratio"/> (covers are 4:5).</summary>
public sealed class RatioBox : Decorator
{
    public double Ratio { get; set; } = 1.25;

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 178 : available.Width;
        var size = new Size(w, w * Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var size = new Size(final.Width, final.Width * Ratio);
        Child?.Arrange(new Rect(size));
        return size;
    }
}

/// <summary>Lays tiles out like CSS grid "repeat(auto-fill, minmax(min, 1fr))": as many columns as fit, stretched to fill.</summary>
public sealed class AutoGrid : Panel
{
    public double MinItemWidth { get; set; } = 178;
    public double Gap { get; set; } = 18;
    public int MaxColumns { get; set; } = int.MaxValue;

    private (int Cols, double W) Layout(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 900;
        var cols = Math.Max(1, Math.Min(MaxColumns, (int)((width + Gap) / (MinItemWidth + Gap))));
        return (cols, (width - Gap * (cols - 1)) / cols);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, w) = Layout(available.Width);
        double total = 0, rowH = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(w, double.PositiveInfinity));
            rowH = Math.Max(rowH, c.DesiredSize.Height);
            if (i % cols == cols - 1 || i == InternalChildren.Count - 1)
            {
                total += rowH + (total > 0 ? Gap : 0);
                rowH = 0;
            }
        }
        return new Size(double.IsInfinity(available.Width) ? cols * w + (cols - 1) * Gap : available.Width, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, w) = Layout(final.Width);
        double y = 0;
        for (var start = 0; start < InternalChildren.Count; start += cols)
        {
            var rowH = 0.0;
            for (var i = start; i < Math.Min(start + cols, InternalChildren.Count); i++) rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);
            for (var i = start; i < Math.Min(start + cols, InternalChildren.Count); i++)
                InternalChildren[i].Arrange(new Rect((i - start) * (w + Gap), y, w, rowH));
            y += rowH + Gap;
        }
        return final;
    }
}
