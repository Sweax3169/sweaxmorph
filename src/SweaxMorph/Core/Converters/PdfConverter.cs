using System.IO;
using System.Text;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Writer;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SweaxMorph.Core.Converters;

/// <summary>
/// PDF pages to pictures with the renderer built into Windows, PDF to text (reading scanned pages with
/// Windows OCR when there is no text inside), and many PDFs merged into one.
/// </summary>
public sealed class PdfConverter : Converter
{
    public override string Name => "Windows PDF";

    public override IEnumerable<Edge> Edges()
    {
        yield return new Edge("pdf", "png", 2, Many: true);
        yield return new Edge("pdf", "jpg", 2, Many: true);
        yield return new Edge("pdf", "txt", 2);
        // plain text as Markdown: the way into Word, EPUB… when LibreOffice isn't there to keep the layout
        yield return new Edge("pdf", "md", 5);
        yield return new Edge("pdf", "pdf", 1, Combines: true);
    }

    public override async Task<string[]> Run(Step s)
    {
        if (s.Inputs.Length > 1 || s.To.Id == "pdf") return [Merge(s)];
        if (s.To.Id is "txt" or "md") return [await Text(s)];
        return await Pages(s);
    }

    static double Dpi(Quality q) => q switch { Quality.Best => 300, Quality.Balanced => 200, _ => 120 };

    async Task<string[]> Pages(Step s)
    {
        var doc = await Open(s.Input);
        var files = new List<string>();
        uint count = doc.PageCount;
        for (uint i = 0; i < count; i++)
        {
            s.Cancel.ThrowIfCancellationRequested();
            string f = count == 1 ? s.Out() : Path.Combine(s.OutDir, $"{s.Name} - {i + 1:D3}{s.To.Ext}");
            await Render(doc, i, Dpi(s.Quality), f, s.To.Id == "jpg");
            files.Add(f);
            s.Progress.Report((i + 1.0) / count);
        }
        return files.ToArray();
    }

    static async Task<PdfDocument> Open(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        try { return await PdfDocument.LoadFromFileAsync(file); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x8007052B)) { throw new ConvertException(Loc.T("Err.Password")); }
    }

    internal static async Task Render(PdfDocument doc, uint index, double dpi, string file, bool jpeg)
    {
        using var page = doc.GetPage(index);
        var opts = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, page.Size.Width * dpi / 96),
            DestinationHeight = (uint)Math.Max(1, page.Size.Height * dpi / 96),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        if (jpeg) opts.BitmapEncoderId = BitmapEncoder.JpegEncoderId;
        using var mem = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(mem, opts);
        mem.Seek(0);
        await using var fs = File.Create(file);
        await mem.AsStreamForRead().CopyToAsync(fs);
    }

    async Task<string> Text(Step s)
    {
        var sb = new StringBuilder();
        int pages;
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(s.Input))
        {
            pages = pdf.NumberOfPages;
            foreach (var page in pdf.GetPages())
            {
                s.Cancel.ThrowIfCancellationRequested();
                string text = ContentOrderTextExtractor.GetText(page);
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(text.TrimEnd());
                s.Progress.Report(0.5 * page.Number / pages);
            }
        }
        // no text layer: a scan. Read it the way a person would.
        if (sb.ToString().Count(char.IsLetterOrDigit) < pages * 8)
        {
            sb.Clear();
            var doc = await Open(s.Input);
            for (uint i = 0; i < doc.PageCount; i++)
            {
                string png = Path.Combine(s.OutDir, $"ocr-{i}.png");
                await Render(doc, i, 220, png, false);
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(await OcrConverter.Read(png));
                File.Delete(png);
                s.Progress.Report(0.5 + 0.5 * (i + 1) / doc.PageCount);
            }
        }
        string outFile = s.Out();
        await File.WriteAllTextAsync(outFile, sb.ToString(), new UTF8Encoding(false), s.Cancel);
        return outFile;
    }

    static string Merge(Step s)
    {
        s.Progress.Report(0.3);
        byte[] merged = PdfMerger.Merge(s.Inputs.Select(File.ReadAllBytes).ToArray());
        string outFile = s.Out();
        File.WriteAllBytes(outFile, merged);
        s.Progress.Report(1);
        return outFile;
    }
}

/// <summary>Text out of pictures, with the OCR engine that ships with Windows (offline, in your languages).</summary>
public sealed class OcrConverter : Converter
{
    public override string Name => "Windows OCR";

    static readonly string[] Pictures = ["png", "jpg", "bmp", "tiff", "gif", "webp", "heic", "avif", "jxl", "psd", "raw", "ico", "tga", "jp2"];

    public override IEnumerable<Edge> Edges()
    {
        foreach (var p in Pictures) yield return new Edge(p, "txt", 4);
    }

    public override async Task<string[]> Run(Step s)
    {
        string input = s.Input;
        if (s.From.Id is not ("png" or "jpg" or "bmp" or "tiff" or "gif"))
        {
            // Windows decodes the common ones; everything else goes through ImageMagick first
            input = Path.Combine(s.OutDir, "ocr.png");
            using var img = new ImageMagick.MagickImage(s.Input);
            img.AutoOrient();
            img.Write(input, ImageMagick.MagickFormat.Png);
        }
        s.Progress.Report(0.3);
        string text = await Read(input);
        if (string.IsNullOrWhiteSpace(text)) throw new ConvertException(Loc.T("Err.NoText"));
        string outFile = s.Out();
        await File.WriteAllTextAsync(outFile, text, new UTF8Encoding(false), s.Cancel);
        s.Progress.Report(1);
        return [outFile];
    }

    static OcrEngine? _engine;

    internal static async Task<string> Read(string file)
    {
        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (_engine == null) throw new ConvertException(Loc.T("Err.NoOcr"));
        var sf = await StorageFile.GetFileFromPathAsync(file);
        using var stream = await sf.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        uint max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        double k = Math.Min(1, Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight));
        if (decoder.PixelWidth < 900 && decoder.PixelHeight < 900) k = 2; // small text reads better bigger
        k = Math.Min(k, Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight));
        transform.ScaledWidth = (uint)(decoder.PixelWidth * k);
        transform.ScaledHeight = (uint)(decoder.PixelHeight * k);
        transform.InterpolationMode = BitmapInterpolationMode.Cubic;
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await _engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }
}
