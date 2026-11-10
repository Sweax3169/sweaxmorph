using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SweaxMorph.Core;
using SweaxMorph.UI;

namespace SweaxMorph.Dev;

/// <summary>
/// README pictures and recordings. The well runs its real code on a hand-stepped clock: the file is "carried"
/// along a path, Morph eats it, a petal is picked and a real conversion runs. Frames go to FFmpeg.
/// </summary>
public static class Shots
{
    const double W = 1040, H = 760, K = 1.25;       // stage size and render scale
    static readonly Point WellAt = new(400, 40);     // where the well sits on the stage
    static bool Dark => Theme.Dark;

    public static async Task Render(App app, string dir, string[] args)
    {
        Directory.CreateDirectory(dir);
        Engines.Detect();
        var settings = new Settings { Output = OutputPlace.Custom, Quality = Quality.Balanced, Language = Loc.Turkish ? "tr" : "en" };
        string outDir = Path.Combine(dir, "~out");
        Directory.CreateDirectory(outDir);
        settings.OutputFolder = outDir;
        app.UseSettings(settings);
        Planner.Build(App.AllConverters());

        string t = Dark ? "dark" : "light";
        string lang = Loc.Turkish ? "tr" : "en";
        string samples = Path.Combine(dir, "~samples");
        Directory.CreateDirectory(samples);
        string photo = Path.Combine(samples, Loc.Turkish ? "gün-batımı.jpg" : "sunset.jpg");
        if (!File.Exists(photo)) Sunset(photo);
        string video = Path.Combine(samples, Loc.Turkish ? "tatil.mov" : "trip.mov");
        if (!File.Exists(video)) File.WriteAllBytes(video, []);

        Springs.ManualClock = true;
        var desk = Desktop();

        // 1) the recording: carry a photo in, Morph eats it, pick WEBP, done
        var frames = Path.Combine(dir, $"~frames-{t}-{lang}");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        var well = new Well();
        well.DemoShow();
        int n = 0;
        var files = new List<string> { photo };
        Point cursor = new(150, 300);
        bool carrying = false, recording = true;
        string ghost = Path.GetFileName(photo);

        async Task Frame(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Springs.Step(1 / 30.0);
                await Dispatcher.Yield(DispatcherPriority.Background);
                well.UpdateLayout();
                if (recording) Save(Compose(desk, well, cursor, carrying ? ghost : null), Path.Combine(frames, $"f{n++:D4}.png"));
            }
        }
        Point Stage(Point p) => new(p.X - WellAt.X, p.Y - WellAt.Y);
        async Task Move(Point to, int count, Action<Point>? each = null)
        {
            var from = cursor;
            for (int i = 1; i <= count; i++)
            {
                double u = i / (double)count, e = u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
                var mid = new Point((from.X + to.X) / 2, Math.Min(from.Y, to.Y) - 60);
                double a = 1 - e;
                cursor = new Point(a * a * from.X + 2 * a * e * mid.X + e * e * to.X, a * a * from.Y + 2 * a * e * mid.Y + e * e * to.Y);
                each?.Invoke(cursor);
                await Frame();
            }
        }

        var heart = new Point(WellAt.X + Well.Center.X, WellAt.Y + Well.Center.Y);
        await Frame(14);
        carrying = true;
        await Move(new Point(heart.X - 30, heart.Y + 20), 40, p =>
        {
            var s = Stage(p);
            if (s.X > 0) well.DemoCarry(files, s);
        });
        await Frame(10);
        well.DemoLand(files, Stage(cursor));
        carrying = false;
        await Frame(22);
        var webp = well.PetalPoint("avif");
        await Move(new Point(WellAt.X + webp.X + 6, WellAt.Y + webp.Y + 8), 24, p => well.DemoPoint(Stage(p)));
        await Frame(16);
        well.DemoChoose("avif");
        await Frame(4);
        while (well.Working) await Frame();
        await Move(new Point(heart.X + 120, heart.Y + 330), 30, p => well.DemoPoint(Stage(p)));
        await Frame(60);
        Snap(desk, well, cursor, Path.Combine(dir, $"done-{t}-{lang}.png"));
        recording = false;

        // 2) stills: petals for a photo, the garden for a video
        well.Close();
        well = new Well();
        well.DemoShow();
        well.DemoHold(files);
        cursor = new Point(WellAt.X + 560, WellAt.Y + 300);
        await Frame(40);
        var png = well.PetalPoint("png");
        well.DemoPoint(png);
        cursor = new Point(WellAt.X + png.X + 6, WellAt.Y + png.Y + 8);
        await Frame(20);
        Snap(desk, well, cursor, Path.Combine(dir, $"petals-{t}-{lang}.png"));
        well.Close();

        well = new Well();
        well.DemoShow();
        well.DemoHold([video]);
        await Frame(30);
        well.DemoMore("");
        cursor = new Point(-100, -100);
        await Frame(30);
        Snap(desk, well, cursor, Path.Combine(dir, $"garden-{t}-{lang}.png"));
        well.Close();

        // 3) settings, as they come out of the box
        Springs.ManualClock = false;
        settings.Output = OutputPlace.BesideOriginal;
        var sw = new SettingsWindow { Left = -32000, Top = -32000, ShowActivated = false };
        sw.Show();
        await Task.Delay(500);
        sw.UpdateLayout();
        Save(Snapshot((FrameworkElement)sw.Content, 860, 680, 1.25, Theme.Brush("Haze")), Path.Combine(dir, $"settings-{t}-{lang}.png"));
        sw.Close();

        // encode
        if (Engines.FFmpeg.Path is { } ff)
        {
            string pattern = Path.Combine(frames, "f%04d.png");
            await Proc.Run(ff, ["-y", "-hide_banner", "-framerate", "30", "-i", pattern, "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", Path.Combine(dir, $"demo-{t}-{lang}.mp4")], CancellationToken.None);
            await Proc.Run(ff, ["-y", "-hide_banner", "-framerate", "30", "-i", pattern, "-filter_complex",
                "fps=15,scale=720:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=160:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
                "-loop", "0", Path.Combine(dir, $"demo-{t}-{lang}.gif")], CancellationToken.None);
        }
        if (!args.Contains("--keep")) try { Directory.Delete(frames, true); } catch { }
        try { Directory.Delete(outDir, true); } catch { }
    }

    // ── palettes ────────────────────────────────────────────────────────────

    /// <summary>
    /// --palettes DIR: every palette, in light and dark. A contact sheet of Morph waiting in each one, a recording of
    /// Morph changing palette live (the real Theme.Apply, so its gel melts from one colour to the next), and the new
    /// Look section of the settings.
    /// </summary>
    public static async Task Palettes(App app, string dir, string[] args)
    {
        Directory.CreateDirectory(dir);
        Engines.Detect();
        var settings = new Settings { Language = Loc.Turkish ? "tr" : "en" };
        app.UseSettings(settings);
        Planner.Build(App.AllConverters());
        string lang = Loc.Turkish ? "tr" : "en";
        const double SW = 640, SH = 700;            // one stage: the Well and a margin of its palette's desk
        var at = new Point(20, 4);

        foreach (var mode in new[] { "light", "dark" })
        {
            Springs.ManualClock = true;
            Theme.Apply(mode, "candy");
            var well = new Well();
            well.DemoShow();
            for (int i = 0; i < 40; i++) { Springs.Step(1 / 30.0); await Dispatcher.Yield(DispatcherPriority.Background); }

            // 1) a sheet: Morph waiting in each palette
            var cells = new List<(Palette p, BitmapSource img)>();
            foreach (var p in UI.Palettes.All)
            {
                Theme.Apply(mode, p.Key);
                for (int i = 0; i < 45; i++) { Springs.Step(1 / 30.0); await Dispatcher.Yield(DispatcherPriority.Background); }
                well.UpdateLayout();
                cells.Add((p, Stage(well, p, SW, SH, at, 0.5)));
            }
            Save(Sheet(cells, mode == "dark"), Path.Combine(dir, $"palettes-{mode}-{lang}.png"));

            // 2) the recording: palette after palette, live
            var frames = Path.Combine(dir, $"~pal-{mode}-{lang}");
            if (Directory.Exists(frames)) Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);
            int n = 0;
            foreach (var p in UI.Palettes.All.Append(UI.Palettes.All[0]))
            {
                Theme.Apply(mode, p.Key);
                well.DemoBounce();
                for (int i = 0; i < 40; i++)
                {
                    Springs.Step(1 / 30.0);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    well.UpdateLayout();
                    Save(Stage(well, p, SW, SH, at, 0.75), Path.Combine(frames, $"f{n++:D4}.png"));
                }
            }
            well.Close();
            if (Engines.FFmpeg.Path is { } ff)
            {
                string pattern = Path.Combine(frames, "f%04d.png");
                await Proc.Run(ff, ["-y", "-hide_banner", "-framerate", "30", "-i", pattern, "-filter_complex",
                    "fps=15,scale=420:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=200:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
                    "-loop", "0", Path.Combine(dir, $"palettes-{mode}-{lang}.gif")], CancellationToken.None);
                await Proc.Run(ff, ["-y", "-hide_banner", "-framerate", "30", "-i", pattern, "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-pix_fmt", "yuv420p",
                    "-movflags", "+faststart", Path.Combine(dir, $"palettes-{mode}-{lang}.mp4")], CancellationToken.None);
            }
            if (!args.Contains("--keep")) try { Directory.Delete(frames, true); } catch { }

            // 3) the Look section of the settings, as it comes out of the box (Candy)
            Springs.ManualClock = false;
            Theme.Apply(mode, "candy");
            var sw = new SettingsWindow { Left = -32000, Top = -32000, ShowActivated = false };
            sw.Show();
            await Task.Delay(600);
            sw.UpdateLayout();
            Save(Section(sw, "HLook", "PaletteTiles", 1.25), Path.Combine(dir, $"look-{mode}-{lang}.png"));
            Save(Section(sw, "HPlace", "AdminHint", 1.25), Path.Combine(dir, $"place-{mode}-{lang}.png"));
            sw.Close();
        }
        Theme.Apply("light", "candy");
    }

    /// <summary>The Well on its palette's desk, at <paramref name="k"/> of its size.</summary>
    static BitmapSource Stage(Well well, Palette p, double w, double h, Point at, double k)
    {
        var (a, b) = UI.Palettes.Desk(p, Theme.Dark);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawRectangle(new LinearGradientBrush(a, b, 35), null, new Rect(0, 0, w, h));
            var root = well.Surface;
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(at.X, at.Y, root.ActualWidth, root.ActualHeight));
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap((int)(w * k), (int)(h * k), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Four across, two down, each with the palette's name.</summary>
    static BitmapSource Sheet(List<(Palette p, BitmapSource img)> cells, bool dark)
    {
        double cw = cells[0].img.PixelWidth, ch = cells[0].img.PixelHeight, label = 34, gap = 14;
        int cols = 4, rows = (cells.Count + cols - 1) / cols;
        double w = cols * cw + (cols + 1) * gap, h = rows * (ch + label) + (rows + 1) * gap;
        var dv = new DrawingVisual();
        var face = new Typeface(Theme.Rounded, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Blob.Hex(dark ? "#121016" : "#FBFAFD")), null, new Rect(0, 0, w, h));
            for (int i = 0; i < cells.Count; i++)
            {
                var (p, img) = cells[i];
                double x = gap + (i % cols) * (cw + gap), y = gap + (i / cols) * (ch + label + gap);
                dc.PushClip(new RectangleGeometry(new Rect(x, y, cw, ch), 22, 22));
                dc.DrawImage(img, new Rect(x, y, cw, ch));
                dc.Pop();
                var m = p.For(dark);
                var t = Text(p.Name, face, 17, new SolidColorBrush(Blob.Hex(dark ? "#F2EEF8" : "#231D2E")));
                dc.DrawText(t, new Point(x + 6, y + ch + 7));
                for (int d = 0; d < 3; d++)
                    dc.DrawEllipse(new SolidColorBrush(Blob.Hex(new[] { m.Accent, p.GelBottom, m.Accent2 }[d])), null, new Point(x + 16 + t.Width + d * 15, y + ch + 18), 5, 5);
            }
        }
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    /// <summary>A strip of the settings, from one named element down to another.</summary>
    static BitmapSource Section(SettingsWindow sw, string from, string to, double k)
    {
        var top = (FrameworkElement)sw.FindName(from);
        var bottom = (FrameworkElement)sw.FindName(to);
        var page = (FrameworkElement)VisualTreeHelper.GetParent(top);
        double y0 = top.TranslatePoint(new Point(0, 0), page).Y - 10;
        double y1 = bottom.TranslatePoint(new Point(0, bottom.ActualHeight), page).Y + 14;
        double w = page.ActualWidth + 44, h = y1 - y0;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawRectangle(Theme.Brush("Haze"), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(page) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(-20, y0, w, h) },
                null, new Rect(0, 0, w, h));
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap((int)(w * k), (int)(h * k), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    // ── composing a frame ───────────────────────────────────────────────────

    /// <summary>An abstract desktop: soft colour fields and one folder window holding the photo.</summary>
    static DrawingImage Desktop()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            var bg = new LinearGradientBrush(Blob.Hex(Dark ? "#1C1630" : "#EFE9FF"), Blob.Hex(Dark ? "#2A1426" : "#FFE6F0"), 35);
            dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));
            void Glow(double x, double y, double r, string hue, byte a) =>
                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(a, Blob.Hex(hue).R, Blob.Hex(hue).G, Blob.Hex(hue).B), Color.FromArgb(0, 0, 0, 0)), null, new Point(x, y), r, r);
            Glow(160, 620, 380, "#8B7CFF", (byte)(Dark ? 90 : 70));
            Glow(900, 120, 360, "#FF6B9A", (byte)(Dark ? 80 : 60));
            Glow(980, 700, 300, "#2EC4B6", (byte)(Dark ? 60 : 40));

            // a plain folder window
            var win = new Rect(48, 170, 300, 300);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, new Rect(win.X + 2, win.Y + 8, win.Width, win.Height), 14, 14);
            dc.DrawRoundedRectangle(new SolidColorBrush(Blob.Hex(Dark ? "#262033" : "#FFFFFF")), new Pen(new SolidColorBrush(Blob.Hex(Dark ? "#3A3147" : "#E7E1F0")), 1), win, 14, 14);
            var ui = new Typeface(Theme.Rounded, FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);
            var ink = new SolidColorBrush(Blob.Hex(Dark ? "#F6F0FF" : "#2B2140"));
            var ink2 = new SolidColorBrush(Blob.Hex(Dark ? "#B8ADCC" : "#6F6585"));
            dc.DrawText(Text(Loc.Turkish ? "Resimler" : "Pictures", ui, 15, ink), new Point(win.X + 20, win.Y + 16));
            string[] names = Loc.Turkish ? ["gün-batımı.jpg", "kedi.heic", "ekran.png", "logo.psd"] : ["sunset.jpg", "cat.heic", "screen.png", "logo.psd"];
            string[] hues = ["#FF9A62", "#FF6B9A", "#8B7CFF", "#4C8DFF"];
            for (int i = 0; i < 4; i++)
            {
                double x = win.X + 22 + (i % 2) * 136, y = win.Y + 58 + (i / 2) * 116;
                var tile = new Rect(x, y, 118, 78);
                dc.DrawRoundedRectangle(new LinearGradientBrush(Blob.Mix(Blob.Hex(hues[i]), Colors.White, 0.35), Blob.Hex(hues[i]), 60), null, tile, 10, 10);
                if (i == 0)
                {
                    dc.DrawEllipse(new SolidColorBrush(Blob.Hex("#FFE7A8")), null, new Point(x + 70, y + 40), 14, 14);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 60, 40, 120)), null, new Rect(x, y + 50, 118, 28));
                }
                dc.DrawText(Text(names[i], ui, 12, i == 0 ? ink : ink2), new Point(x + 2, y + 84));
            }
        }
        g.ClipGeometry = new RectangleGeometry(new Rect(0, 0, W, H));
        g.Freeze();
        return new DrawingImage(g);
    }

    static FormattedText Text(string s, Typeface f, double size, Brush b) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, f, size, b, 1);

    static BitmapSource Compose(DrawingImage desk, Well well, Point cursor, string? ghost)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(K, K));
            dc.DrawImage(desk, new Rect(0, 0, W, H));
            var root = well.Surface;
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(WellAt.X, WellAt.Y, root.ActualWidth, root.ActualHeight));
            if (ghost != null) Ghost(dc, cursor, ghost);
            Cursor(dc, cursor);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap((int)(W * K), (int)(H * K), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>The file being carried: a small photo tile with its name, slightly see-through.</summary>
    static void Ghost(DrawingContext dc, Point p, string name)
    {
        var tile = new Rect(p.X + 6, p.Y + 14, 92, 62);
        dc.PushOpacity(0.92);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), null, new Rect(tile.X + 1, tile.Y + 5, tile.Width, tile.Height), 9, 9);
        dc.DrawRoundedRectangle(new LinearGradientBrush(Blob.Hex("#FFC27A"), Blob.Hex("#FF6B9A"), 60), new Pen(Brushes.White, 2), tile, 9, 9);
        dc.DrawEllipse(new SolidColorBrush(Blob.Hex("#FFE7A8")), null, new Point(tile.X + 56, tile.Y + 30), 11, 11);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 60, 40, 120)), null, new Rect(tile.X + 1, tile.Y + 40, tile.Width - 2, 20));
        var f = new Typeface(Theme.Rounded, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
        var label = Text(name, f, 12, Brushes.White);
        var tag = new Rect(tile.X, tile.Bottom + 6, label.Width + 16, 22);
        dc.DrawRoundedRectangle(new SolidColorBrush(Blob.Hex("#8B7CFF")), null, tag, 7, 7);
        dc.DrawText(label, new Point(tag.X + 8, tag.Y + 3));
        dc.Pop();
    }

    static void Cursor(DrawingContext dc, Point p)
    {
        var g = Geometry.Parse("M0,0 L0,17 L4.6,12.8 L7.6,19.6 L10.4,18.4 L7.4,11.7 L13,11.7 Z");
        dc.PushTransform(new TranslateTransform(p.X, p.Y));
        dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.3) { LineJoin = PenLineJoin.Round }, g);
        dc.Pop();
    }

    static void Snap(DrawingImage desk, Well well, Point cursor, string file) => Save(Compose(desk, well, cursor, null), file);

    static BitmapSource Snapshot(FrameworkElement e, double w, double h, double k, Brush bg)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(e) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, e.ActualWidth, e.ActualHeight));
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap((int)(w * k), (int)(h * k), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    static void Save(BitmapSource src, string file)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>A big photo to convert in the recording: a sunset over the sea.</summary>
    static void Sunset(string file)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            sky.GradientStops.Add(new GradientStop(Blob.Hex("#3B2A6E"), 0));
            sky.GradientStops.Add(new GradientStop(Blob.Hex("#B24D8E"), 0.45));
            sky.GradientStops.Add(new GradientStop(Blob.Hex("#FF9A62"), 0.62));
            dc.DrawRectangle(sky, null, new Rect(0, 0, 3200, 2000));
            dc.DrawEllipse(new RadialGradientBrush(Blob.Hex("#FFF1B8"), Blob.Hex("#FFB86B")), null, new Point(1650, 1180), 260, 260);
            var rnd = new Random(4);
            for (int i = 0; i < 260; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)rnd.Next(60, 200), 255, 255, 255)), null, new Point(rnd.Next(3200), rnd.Next(700)), 2, 2);
            var hills = Geometry.Parse("M0,1240 L300,1080 L620,1200 L980,980 L1320,1180 L1700,1020 L2100,1190 L2500,1000 L2900,1160 L3200,1060 L3200,2000 L0,2000 Z");
            dc.DrawGeometry(new SolidColorBrush(Blob.Hex("#2A1E4A")), null, hills);
            var sea = new LinearGradientBrush(Blob.Hex("#6B3F86"), Blob.Hex("#1E1838"), 90);
            dc.DrawRectangle(sea, null, new Rect(0, 1300, 3200, 700));
            for (int i = 0; i < 90; i++)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)rnd.Next(40, 140), 255, 214, 160)), null, new Rect(1650 - rnd.Next(30, 300), 1320 + i * 7, rnd.Next(80, 600), 3));
        }
        var rtb = new RenderTargetBitmap(3200, 2000, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
