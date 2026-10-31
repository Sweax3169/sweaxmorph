using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SweaxMorph.Core;
using SweaxMorph.UI;

namespace SweaxMorph.Dev;

/// <summary>
/// Developer modes. --selftest DIR makes sample files and converts them every which way, writing a report;
/// --icon DIR renders Morph as the app icon; --shots DIR renders the README pictures.
/// </summary>
public static class Demo
{
    static string? Arg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static async void Run(App app, string[] args)
    {
        try
        {
            Paths.Init();
            Loc.Init(Arg(args, "--lang") ?? "en");
            Theme.Apply(Arg(args, "--theme") ?? "light");
            if (Arg(args, "--selftest") is { } dir) await SelfTest(dir, Arg(args, "--only"));
            else if (Arg(args, "--icon") is { } iconDir) Icon(iconDir);
            else if (Arg(args, "--shots") is { } shotDir) await Shots.Render(app, shotDir, args);
            else if (Arg(args, "--formats") is { } catalogDir) Catalog(catalogDir);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "sweaxmorph-demo-error.txt"), ex.ToString());
        }
        app.Shutdown();
    }

    // ── self test ───────────────────────────────────────────────────────────

    static async Task SelfTest(string dir, string? only)
    {
        Directory.CreateDirectory(dir);
        string samples = Path.Combine(dir, "samples");
        string outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(samples);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        Engines.Detect();
        Planner.Build(App.AllConverters());
        var settings = new Settings { Output = OutputPlace.Custom, OutputFolder = outDir, Quality = Quality.Balanced };
        var report = new StringBuilder();
        report.AppendLine($"SweaxMorph self test · {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine("engines: " + string.Join(", ", Engines.All.Select(e => $"{e.Name}={(e.Ready ? "yes" : "no")}")));
        int edges = Planner.Converters.Sum(c => c.Edges().Count());
        report.AppendLine($"converters: {Planner.Converters.Count}, edges: {edges}");

        await MakeSamples(samples);
        var tests = Plan(samples);
        int ok = 0, fail = 0;
        foreach (var (input, target, combine) in tests)
        {
            if (only != null && !$"{Path.GetFileName(input[0])}>{target}".Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
            var job = new Job { Files = input, Target = target, Options = new Options { Quality = Quality.Balanced, Combine = combine }, Settings = settings };
            var sw = Stopwatch.StartNew();
            string route = combine ? "combine" : Formats.Of(input[0]) is { } f && Planner.Targets(f.Id).TryGetValue(target, out var r)
                ? string.Join(" > ", r.Hops.Select(h => $"{h.Converter.Name}:{h.Edge.To}")) : f2(input[0], target);
            try { await Task.Run(() => job.Run(CancellationToken.None)); }
            catch (Exception ex) { job.Failures.Add(new Failure(input[0], ex.Message)); }
            string name = $"{string.Join("+", input.Select(Path.GetFileName))} → {target}";
            if (job.Outputs.Count > 0 && job.Failures.Count == 0)
            {
                ok++;
                var o = job.Outputs[0];
                report.AppendLine($"OK   {name,-44} {sw.ElapsedMilliseconds,6} ms  {Loc.Size(o.After),9}  [{route}]  {Path.GetFileName(o.Path)}");
            }
            else
            {
                fail++;
                report.AppendLine($"FAIL {name,-44} {sw.ElapsedMilliseconds,6} ms  [{route}]  {job.Failures.FirstOrDefault()?.Message}");
            }
            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        }
        report.AppendLine($"\n{ok} ok, {fail} failed");
        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());

        static string f2(string input, string target) => target == Formats.Of(input)?.Id ? "shrink" : "no route";
    }

    static List<(List<string> input, string target, bool combine)> Plan(string s)
    {
        string S(string name) => Path.Combine(s, name);
        var t = new List<(List<string>, string, bool)>();
        void Add(string file, params string[] targets) { foreach (var x in targets) t.Add(([S(file)], x, false)); }
        Add("photo.png", "jpg", "webp", "avif", "gif", "bmp", "tiff", "ico", "pdf", "tga", "jxl", "qoi", "txt", "png");
        Add("photo.jpg", "png", "webp", "jpg", "pdf");
        Add("drawing.svg", "png", "pdf", "jpg");
        Add("clip.mp4", "mkv", "webm", "mov", "avi", "gif", "mp3", "wav", "m4a", "jpg", "mp4", "wmv", "apng");
        Add("song.wav", "mp3", "flac", "ogg", "opus", "m4a", "aiff", "mp4", "m4r", "wma");
        Add("notes.md", "html", "docx", "pdf", "epub", "odt", "rtf", "txt", "tex", "pptx", "ipynb");
        Add("table.csv", "xlsx", "json", "yaml", "xml", "tsv", "md", "html", "toml");
        Add("config.json", "yaml", "toml", "xml", "csv", "xlsx", "jsonl");
        Add("config.yaml", "json", "toml");
        Add("lines.srt", "vtt", "ass", "lrc");
        Add("font.ttf", "woff", "woff2");
        Add("pack.zip", "folder", "tgz", "7z");
        t.Add(([S("photo.png"), S("photo.jpg")], "pdf", true));
        t.Add(([S("photo.png"), S("notes.md"), S("table.csv")], "zip", true));
        // second generation: results of the first round (see MakeSamples for docx, xlsx, pptx, pdf)
        Add("letter.docx", "pdf", "md", "html", "odt", "doc", "txt", "epub");
        Add("sheet.xlsx", "csv", "pdf", "ods", "json", "xls");
        Add("deck.pptx", "pdf", "png", "odp");
        Add("paper.pdf", "png", "jpg", "txt", "docx", "epub");
        Add("book.epub", "docx", "html", "txt", "md");
        Add("font.woff2", "ttf", "woff");
        return t;
    }

    static async Task MakeSamples(string dir)
    {
        string S(string name) => Path.Combine(dir, name);
        // a picture: soft gradient, shapes and text (Turkish letters too)
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Blob.Hex("#FFD3E6"), Blob.Hex("#C9BDFF"), 30), null, new Rect(0, 0, 1200, 760));
            dc.DrawEllipse(new SolidColorBrush(Blob.Hex("#FF6B9A")), null, new Point(900, 380), 190, 190);
            dc.DrawRectangle(new SolidColorBrush(Blob.Hex("#2EC4B6")), null, new Rect(140, 420, 380, 180));
            var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            dc.DrawText(new FormattedText("Merhaba Dünya — SweaxMorph", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 64, Brushes.Black, 1), new Point(120, 140));
            dc.DrawText(new FormattedText("Şişli, İstanbul · çğıöşü", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 40, Brushes.Black, 1), new Point(124, 250));
        }
        var rtb = new RenderTargetBitmap(1200, 760, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        Save(new PngBitmapEncoder(), rtb, S("photo.png"));
        Save(new JpegBitmapEncoder { QualityLevel = 92 }, rtb, S("photo.jpg"));
        File.WriteAllText(S("drawing.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300" viewBox="0 0 400 300"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#CDB8FF"/><stop offset="1" stop-color="#FF8FC1"/></linearGradient></defs><rect width="400" height="300" rx="40" fill="url(#g)"/><circle cx="150" cy="140" r="14" fill="#2B2140"/><circle cx="250" cy="140" r="14" fill="#2B2140"/><path d="M170 190 Q200 220 230 190" stroke="#2B2140" stroke-width="8" fill="none" stroke-linecap="round"/></svg>""");

        if (Engines.FFmpeg.Ready)
        {
            await Proc.Run(Engines.FFmpeg.Path!, ["-y", "-hide_banner", "-f", "lavfi", "-i", "testsrc2=duration=4:size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", S("clip.mp4")], CancellationToken.None);
            await Proc.Run(Engines.FFmpeg.Path!, ["-y", "-hide_banner", "-f", "lavfi", "-i", "sine=frequency=330:duration=6", "-ac", "2", S("song.wav")], CancellationToken.None);
        }
        File.WriteAllText(S("notes.md"), """
            # Release notes

            SweaxMorph turns **any file** into the format you need. Türkçe karakterler: ç ğ ı ö ş ü İ.

            ## What's new

            - Drop files on Morph, pick a petal
            - Right-click → *Convert with SweaxMorph*
            - 150+ formats

            | Format | Kind | Size |
            |---|---|---|
            | PNG | Image | 1.2 MB |
            | MP4 | Video | 14 MB |

            ```python
            print("Merhaba Dünya!")
            ```

            > Nothing leaves your computer.
            """);
        File.WriteAllText(S("table.csv"), "name,city,score,joined\r\nAyşe,İstanbul,92.5,2024-01-12\r\nMehmet,\"Ankara, Çankaya\",88,2023-11-02\r\nZeynep,İzmir,100,2025-03-30\r\n", new UTF8Encoding(true));
        File.WriteAllText(S("config.json"), """{ "name": "SweaxMorph", "version": "1.0.0", "hotkey": "Win+Alt+F", "quality": "balanced", "formats": 150, "engines": [ { "id": "ffmpeg", "ready": true }, { "id": "pandoc", "ready": false } ], "output": { "place": "beside", "openFolder": false } }""");
        File.WriteAllText(S("config.yaml"), "name: SweaxMorph\nversion: 1.0.0\nlimits:\n  files: 500\n  sizeMb: 4096\ntags: [convert, files, windows]\n");
        File.WriteAllText(S("lines.srt"), "1\r\n00:00:01,000 --> 00:00:03,500\r\nMerhaba, dünya!\r\n\r\n2\r\n00:00:04,000 --> 00:00:06,000\r\nHello, world!\r\n");
        string arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (File.Exists(arial)) File.Copy(arial, S("font.ttf"), true);
        if (File.Exists(S("pack.zip"))) File.Delete(S("pack.zip"));
        using (var z = System.IO.Compression.ZipFile.Open(S("pack.zip"), System.IO.Compression.ZipArchiveMode.Create))
        {
            z.CreateEntryFromFile(S("notes.md"), "docs/notes.md");
            z.CreateEntryFromFile(S("photo.jpg"), "photo.jpg");
        }

        // second-generation samples come out of SweaxMorph itself
        var settings = new Settings { Output = OutputPlace.Custom, OutputFolder = dir };
        async Task Make(string from, string to, string name)
        {
            if (File.Exists(S(name))) return;
            var job = new Job { Files = [S(from)], Target = to, Options = new Options(), Settings = settings };
            await Task.Run(() => job.Run(CancellationToken.None));
            if (job.Outputs.FirstOrDefault() is { } o) File.Move(o.Path, S(name), true);
        }
        await Make("notes.md", "docx", "letter.docx");
        await Make("table.csv", "xlsx", "sheet.xlsx");
        await Make("notes.md", "pptx", "deck.pptx");
        await Make("letter.docx", "pdf", "paper.pdf");
        await Make("notes.md", "epub", "book.epub");
        await Make("font.ttf", "woff2", "font.woff2");
    }

    static void Save(BitmapEncoder enc, BitmapSource src, string file)
    {
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    // ── the format catalogue for the README and the site ────────────────────

    /// <summary>Every format with what it can become, assuming every optional engine is installed.</summary>
    static void Catalog(string dir)
    {
        Directory.CreateDirectory(dir);
        Engines.Detect();
        foreach (var e in Engines.All) e.Path ??= "assumed";
        Planner.Build(App.AllConverters());
        var all = Formats.All.Where(f => f.Kind != Kind.Folder).ToList();
        var targets = all.ToDictionary(f => f.Id, f => Planner.Targets(f.Id).Keys.Where(k => k != "folder").ToHashSet());
        var writable = targets.Values.SelectMany(t => t).ToHashSet();
        int routes = targets.Values.Sum(t => t.Count);
        var catalogue = new
        {
            routes,
            formats = all.Select(f => new { id = f.Id, label = f.Label, kind = f.Kind.ToString(), hue = Formats.Hue(f.Kind), ext = f.Extensions, en = f.En, tr = f.Tr, to = targets[f.Id].Count, write = writable.Contains(f.Id) }),
        };
        var json = new StringBuilder(System.Text.Json.JsonSerializer.Serialize(catalogue, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.WriteAllText(Path.Combine(dir, "formats.json"), json.ToString(), new UTF8Encoding(false));

        foreach (var tr in new[] { false, true })
        {
            var md = new StringBuilder();
            md.AppendLine(tr ? "| Tür | Formatlar |" : "| Kind | Formats |").AppendLine("|---|---|");
            foreach (var g in all.GroupBy(f => f.Kind))
            {
                Loc.Init(tr ? "tr" : "en");
                md.AppendLine($"| **{Formats.KindName(g.Key)}** | {string.Join(" · ", g.Select(f => writable.Contains(f.Id) ? f.Label : f.Label + @"\*"))} |");
            }
            File.WriteAllText(Path.Combine(dir, tr ? "formats.tr.md" : "formats.md"), md.ToString(), new UTF8Encoding(false));
        }
        File.WriteAllLines(Path.Combine(dir, "counts.txt"), [$"formats {all.Count}", $"routes {routes}", $"writable {writable.Count}"]);
    }

    // ── icon ────────────────────────────────────────────────────────────────

    /// <summary>Morph itself, at every icon size, plus a big PNG for the README.</summary>
    static void Icon(string dir)
    {
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
        {
            var blob = new Blob { Frozen = true, Still = true, Shadow = false, Radius = size * 0.455, Width = size, Height = size, FaceScale = size <= 32 ? 1.5 : size <= 64 ? 1.28 : 1.1 };
            blob.Measure(new Size(size, size));
            blob.Arrange(new Rect(0, 0, size, size));
            blob.Step(0.016);
            // the icon is Morph without its shadow, centred
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var host = new System.Windows.Controls.Canvas { Width = size, Height = size };
            host.Children.Add(blob);
            host.Measure(new Size(size, size));
            host.Arrange(new Rect(0, 0, size, size));
            host.UpdateLayout();
            rtb.Render(host);
            string f = Path.Combine(dir, $"icon-{size}.png");
            Save(new PngBitmapEncoder(), rtb, f);
            if (size <= 256) files.Add(f);
        }
        using var set = new ImageMagick.MagickImageCollection();
        foreach (var f in files.OrderByDescending(f => f.Length).ThenByDescending(f => f)) set.Add(new ImageMagick.MagickImage(f));
        set.Write(Path.Combine(dir, "sweaxmorph.ico"), ImageMagick.MagickFormat.Ico);
    }
}
