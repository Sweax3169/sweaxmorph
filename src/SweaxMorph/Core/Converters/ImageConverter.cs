using System.IO;
using ImageMagick;

namespace SweaxMorph.Core.Converters;

/// <summary>Every still image, in-process with ImageMagick: HEIC, AVIF, RAW, PSD, SVG, ICO, PDF pages out…</summary>
public sealed class ImageConverter : Converter
{
    public override string Name => "ImageMagick";

    static readonly string[] Reads = ["png", "jpg", "webp", "avif", "heic", "gif", "bmp", "tiff", "ico", "svg", "psd", "tga", "jxl", "jp2", "dds", "exr", "hdr", "qoi", "pcx", "ppm", "emf", "cur", "raw", "xcf"];
    static readonly string[] Writes = ["png", "jpg", "webp", "avif", "heic", "gif", "bmp", "tiff", "ico", "psd", "tga", "jxl", "jp2", "dds", "exr", "hdr", "qoi", "pcx", "ppm"];
    static readonly string[] Shrinkable = ["png", "jpg", "webp", "avif", "heic", "gif", "tiff", "bmp", "jxl"];

    static HashSet<string>? _canRead, _canWrite;

    /// <summary>What this build of Magick.NET really supports (HEIC writing, for one, depends on the codecs inside).</summary>
    static void Probe()
    {
        if (_canRead != null) return;
        var map = MagickNET.SupportedFormats.ToDictionary(f => f.Format);
        bool R(MagickFormat f) => map.TryGetValue(f, out var i) && i.SupportsReading;
        bool W(MagickFormat f) => map.TryGetValue(f, out var i) && i.SupportsWriting;
        _canRead = Reads.Where(id => id == "raw" ? R(MagickFormat.Dng) : MagickOf(id) is { } m && R(m)).ToHashSet();
        _canWrite = Writes.Where(id => MagickOf(id) is { } m && W(m)).ToHashSet();
        if (W(MagickFormat.Pdf)) _canWrite.Add("pdf");
    }

    static MagickFormat? MagickOf(string id) => id switch
    {
        "png" => MagickFormat.Png, "jpg" => MagickFormat.Jpeg, "webp" => MagickFormat.WebP, "avif" => MagickFormat.Avif, "heic" => MagickFormat.Heic,
        "gif" => MagickFormat.Gif, "bmp" => MagickFormat.Bmp, "tiff" => MagickFormat.Tiff, "ico" => MagickFormat.Ico, "svg" => MagickFormat.Svg,
        "psd" => MagickFormat.Psd, "tga" => MagickFormat.Tga, "jxl" => MagickFormat.Jxl, "jp2" => MagickFormat.Jp2, "dds" => MagickFormat.Dds,
        "exr" => MagickFormat.Exr, "hdr" => MagickFormat.Hdr, "qoi" => MagickFormat.Qoi, "pcx" => MagickFormat.Pcx, "ppm" => MagickFormat.Ppm,
        "emf" => MagickFormat.Emf, "cur" => MagickFormat.Cur, "xcf" => MagickFormat.Xcf, "pdf" => MagickFormat.Pdf,
        _ => null,
    };

    public override IEnumerable<Edge> Edges()
    {
        Probe();
        foreach (var e in Cross(_canRead!, _canWrite!, 2)) yield return e with { Cost = Cost(e.From, e.To) };
        foreach (var s in Shrinkable.Where(_canWrite!.Contains)) yield return new Edge(s, s, 2);
        // many pictures, one result
        foreach (var r in _canRead!)
        {
            if (_canWrite!.Contains("pdf")) yield return new Edge(r, "pdf", 2, Combines: true);
            yield return new Edge(r, "gif", 3, Combines: true);
            yield return new Edge(r, "tiff", 3, Combines: true);
            yield return new Edge(r, "ico", 3, Combines: true);
        }
    }

    // lossy → lossless costs nothing extra, but vector → raster and raw developing are a bit slower
    static int Cost(string from, string to) => from is "svg" or "raw" or "psd" or "xcf" ? 3 : 2;

    public override Task<string[]> Run(Step s) => Task.Run(() => Convert(s), s.Cancel);

    static string[] Convert(Step s)
    {
        if (s.Inputs.Length > 1) return [Combine(s)];
        s.Progress.Report(0.1);
        var settings = ReadSettings(s.From.Id);
        string to = s.To.Id;

        if (to == "ico")
        {
            using var img = new MagickImage(s.Input, settings);
            img.AutoOrient();
            return [Icon([img], s.Out())];
        }

        // animations keep moving when the target can hold it (GIF ↔ WEBP ↔ APNG-capable formats)
        bool animatedTarget = to is "gif" or "webp";
        using var frames = new MagickImageCollection();
        if (animatedTarget && s.From.Id is "gif" or "webp") frames.Read(s.Input, settings);
        else frames.Add(new MagickImage(s.Input, settings));
        s.Progress.Report(0.5);

        foreach (var f in frames) Prepare(f, s);
        if (frames.Count > 1)
        {
            frames.Coalesce();
            if (to == "gif") frames.OptimizePlus();
        }
        if (to == "pdf") foreach (var f in frames) f.Density = new Density(150);
        string outFile = s.Out();
        if (frames.Count > 1) frames.Write(outFile, MagickOf(to)!.Value);
        else frames[0].Write(outFile, MagickOf(to)!.Value);
        s.Progress.Report(1);
        return [outFile];
    }

    static MagickReadSettings ReadSettings(string from)
    {
        var rs = new MagickReadSettings();
        if (from == "svg") { rs.Density = new Density(192); rs.BackgroundColor = MagickColors.None; }
        if (from is "psd" or "xcf") rs.FrameIndex = 0; // the flattened picture
        if (from == "emf") rs.Density = new Density(192);
        return rs;
    }

    /// <summary>Upright, right colour depth, alpha only where the format holds it, and the chosen quality.</summary>
    static void Prepare(IMagickImage<byte> img, Step s)
    {
        img.AutoOrient();
        string to = s.To.Id;
        bool keepsAlpha = to is "png" or "webp" or "avif" or "heic" or "gif" or "tiff" or "ico" or "psd" or "tga" or "jxl" or "jp2" or "dds" or "exr" or "qoi" or "pdf";
        if (!keepsAlpha && img.HasAlpha)
        {
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaOption.Remove);
        }
        bool same = s.From.Id == to;
        var q = s.Quality;
        if (same) q = Quality.Small;
        img.Quality = to switch
        {
            "jpg" => q switch { Quality.Best => 95u, Quality.Balanced => 86u, _ => 74u },
            "webp" => q switch { Quality.Best => 92u, Quality.Balanced => 82u, _ => 70u },
            "avif" or "heic" or "jxl" => q switch { Quality.Best => 88u, Quality.Balanced => 72u, _ => 58u },
            "png" => 95u, // zlib level 9 + adaptive filtering
            _ => img.Quality,
        };
        if (to == "jpg") img.Settings.Interlace = Interlace.Plane;
        if (q == Quality.Small)
        {
            img.Strip(); // camera, GPS and editing metadata
            if (same && Math.Max(img.Width, img.Height) > 2560) img.Resize(new MagickGeometry(2560, 2560) { Greater = true });
        }
        if (to == "png" && same) img.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        if (img.Depth > 8 && to is "jpg" or "webp" or "gif" or "bmp" or "pcx" or "ico") img.Depth = 8;
        if (to == "gif" && !same) img.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
    }

    /// <summary>A real Windows icon: 16 to 256 pixels, all in one file, each size resampled from the original.</summary>
    static string Icon(IEnumerable<IMagickImage<byte>> sources, string outFile)
    {
        var src = sources.First();
        using var set = new MagickImageCollection();
        foreach (uint size in new uint[] { 256, 128, 64, 48, 32, 24, 16 })
        {
            var f = (MagickImage)src.Clone();
            f.BackgroundColor = MagickColors.None;
            f.Resize(new MagickGeometry(size, size));
            f.Extent(size, size, Gravity.Center, MagickColors.None);
            if (size <= 32) f.UnsharpMask(0, 0.6, 0.8, 0.02);
            set.Add(f);
        }
        set.Write(outFile, MagickFormat.Ico);
        return outFile;
    }

    /// <summary>Several pictures in one file: a PDF with one page each, an animated GIF, a multi-page TIFF or one icon.</summary>
    static string Combine(Step s)
    {
        using var set = new MagickImageCollection();
        for (int i = 0; i < s.Inputs.Length; i++)
        {
            s.Cancel.ThrowIfCancellationRequested();
            var img = new MagickImage(s.Inputs[i], ReadSettings(Formats.Of(s.Inputs[i])?.Id ?? ""));
            Prepare(img, s);
            set.Add(img);
            s.Progress.Report(0.8 * (i + 1) / s.Inputs.Length);
        }
        string outFile = s.Out();
        switch (s.To.Id)
        {
            case "pdf":
                foreach (var f in set) f.Density = new Density(150);
                set.Write(outFile, MagickFormat.Pdf);
                break;
            case "gif":
                uint w = set.Max(f => f.Width), h = set.Max(f => f.Height);
                foreach (var f in set)
                {
                    f.Resize(new MagickGeometry(w, h));
                    f.Extent(w, h, Gravity.Center, MagickColors.Black);
                    f.AnimationDelay = 80;
                }
                set.Quantize(new QuantizeSettings { Colors = 256 });
                set.Optimize();
                set.Write(outFile, MagickFormat.Gif);
                break;
            case "ico":
                return Icon(set, outFile);
            default:
                set.Write(outFile, MagickFormat.Tiff);
                break;
        }
        s.Progress.Report(1);
        return outFile;
    }
}
