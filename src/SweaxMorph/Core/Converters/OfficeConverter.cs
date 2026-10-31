using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SweaxMorph.Core.Converters;

/// <summary>
/// Word, Excel and PowerPoint documents through Microsoft Office itself (hidden, read-only, macros off):
/// the most faithful way there is, slides → pictures and MP4 included.
/// </summary>
public sealed class OfficeConverter(Engine app) : Converter
{
    public override string Name => app.Name.Replace("Microsoft ", "");
    public override Engine Engine => app;

    static readonly SemaphoreSlim Gate = new(1, 1);

    public override IEnumerable<Edge> Edges()
    {
        if (app == Engines.Word)
        {
            string[] from = ["docx", "doc", "docm", "odt", "rtf", "mht"];
            // (PDF → Word is left to LibreOffice: Word's PDF reflow stalls when it runs hidden)
            foreach (var e in Cross(from, ["docx", "doc", "odt", "rtf", "txt", "html", "mht", "pdf", "xps"], 1)) yield return e;
        }
        else if (app == Engines.Excel)
        {
            foreach (var e in Cross(["xlsx", "xls", "xlsm", "xlsb", "ods"], ["xlsx", "xls", "xlsb", "ods", "pdf", "xps"], 1)) yield return e;
        }
        else
        {
            string[] from = ["pptx", "ppt", "ppsx", "odp"];
            foreach (var e in Cross(from, ["pptx", "ppt", "ppsx", "odp", "pdf", "xps"], 1)) yield return e;
            foreach (var e in Cross(from, ["png", "jpg"], 2, many: true)) yield return e;
            foreach (var e in Cross(from, ["mp4", "wmv"], 3)) yield return e;
        }
    }

    public override async Task<string[]> Run(Step s)
    {
        await Gate.WaitAsync(s.Cancel);
        try
        {
            string exe = app == Engines.Word ? "WINWORD" : app == Engines.Excel ? "EXCEL" : "POWERPNT";
            var before = Process.GetProcessesByName(exe).Select(p => p.Id).ToHashSet();
            var job = Sta(() => app == Engines.Word ? Word(s, before.Count > 0) : app == Engines.Excel ? Excel(s, before.Count > 0) : PowerPoint(s, before.Count > 0));
            var limit = TimeSpan.FromMinutes(s.To.Id is "mp4" or "wmv" ? 30 : 4);
            var done = await Task.WhenAny(job, Task.Delay(limit, s.Cancel));
            if (done != job)
            {
                // a hidden dialog or a hang: end only the copies we started
                foreach (var p in Process.GetProcessesByName(exe)) if (!before.Contains(p.Id)) try { p.Kill(); } catch { }
                s.Cancel.ThrowIfCancellationRequested();
                throw new ConvertException(Loc.T("Err.Timeout", Name));
            }
            return await job;
        }
        finally { Gate.Release(); }
    }

    static Task<T> Sta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex is COMException or ArgumentException or System.Reflection.TargetInvocationException ? new ConvertException(Loc.T("Err.Engine", "Office", Clean(ex))) : ex); }
        }) { IsBackground = true, Name = "SweaxMorph.Office" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    static string Clean(Exception ex)
    {
        var e = ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
        string m = e.Message.Replace("\r", " ").Replace("\n", " ");
        return m.Length > 160 ? m[..160] : m;
    }

    static dynamic Create(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new ConvertException(Loc.T("Err.Missing", progId)))!;

    static void Release(object? o)
    {
        if (o != null && Marshal.IsComObject(o)) try { Marshal.FinalReleaseComObject(o); } catch { }
    }

    // ── Word ────────────────────────────────────────────────────────────────

    string[] Word(Step s, bool wasRunning)
    {
        dynamic? word = null, doc = null;
        object? previousPdfWarning = null;
        const string Options = @"Software\Microsoft\Office\16.0\Word\Options";
        try
        {
            if (s.From.Id == "pdf")
            {
                // Word asks "convert this PDF?" in a dialog nobody would see; skip that question, then put it back
                using var k = Registry.CurrentUser.CreateSubKey(Options);
                previousPdfWarning = k.GetValue("DisableConvertPdfWarning");
                k.SetValue("DisableConvertPdfWarning", 1, RegistryValueKind.DWord);
            }
            word = Create("Word.Application");
            bool attached = wasRunning && word.Documents.Count > 0;
            word.Visible = false;
            word.DisplayAlerts = 0;
            word.AutomationSecurity = 3; // macros never run
            word.ScreenUpdating = false;
            s.Progress.Report(0.2);
            doc = word.Documents.Open(FileName: s.Input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
            s.Progress.Report(0.6);
            string outFile = s.Out();
            switch (s.To.Id)
            {
                case "pdf": doc.ExportAsFixedFormat(OutputFileName: outFile, ExportFormat: 17, OpenAfterExport: false, OptimizeFor: s.Quality == Quality.Small ? 1 : 0, CreateBookmarks: 1, DocStructureTags: true); break;
                case "xps": doc.ExportAsFixedFormat(OutputFileName: outFile, ExportFormat: 18, OpenAfterExport: false); break;
                case "txt": doc.SaveAs2(FileName: outFile, FileFormat: 2, Encoding: 65001, AddToRecentFiles: false); break;
                default:
                    int code = s.To.Id switch { "docx" => 16, "doc" => 0, "rtf" => 6, "odt" => 23, "html" => 10, "mht" => 9, _ => 16 };
                    doc.SaveAs2(FileName: outFile, FileFormat: code, AddToRecentFiles: false);
                    break;
            }
            doc.Close(SaveChanges: 0);
            Release(doc); doc = null;
            if (!attached) word.Quit(SaveChanges: 0);
            s.Progress.Report(1);
            var made = Directory.GetFiles(s.OutDir, s.Name + ".*");
            return made.Length > 0 ? [made[0]] : [];
        }
        finally
        {
            if (doc != null) try { doc.Close(SaveChanges: 0); } catch { }
            Release(doc);
            Release(word);
            if (s.From.Id == "pdf")
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(Options);
                    if (previousPdfWarning == null) k.DeleteValue("DisableConvertPdfWarning", false);
                    else k.SetValue("DisableConvertPdfWarning", previousPdfWarning, RegistryValueKind.DWord);
                }
                catch { }
        }
    }

    // ── Excel ───────────────────────────────────────────────────────────────

    string[] Excel(Step s, bool wasRunning)
    {
        dynamic? xl = null, wb = null;
        try
        {
            xl = Create("Excel.Application");
            bool attached = wasRunning && xl.Workbooks.Count > 0;
            xl.Visible = false;
            xl.DisplayAlerts = false;
            xl.ScreenUpdating = false;
            xl.AutomationSecurity = 3;
            s.Progress.Report(0.2);
            wb = xl.Workbooks.Open(Filename: s.Input, UpdateLinks: 0, ReadOnly: true, AddToMru: false);
            s.Progress.Report(0.6);
            string outFile = s.Out();
            if (s.To.Id is "pdf" or "xps")
                wb.ExportAsFixedFormat(Type: s.To.Id == "pdf" ? 0 : 1, Filename: outFile, Quality: s.Quality == Quality.Small ? 1 : 0, IgnorePrintAreas: false, OpenAfterPublish: false);
            else
                wb.SaveAs(Filename: outFile, FileFormat: s.To.Id switch { "xls" => 56, "xlsb" => 50, "ods" => 60, _ => 51 }, AddToMru: false);
            wb.Close(SaveChanges: false);
            Release(wb); wb = null;
            if (!attached) xl.Quit();
            s.Progress.Report(1);
            var made = Directory.GetFiles(s.OutDir, s.Name + ".*");
            return made.Length > 0 ? [made[0]] : [];
        }
        finally
        {
            if (wb != null) try { wb.Close(SaveChanges: false); } catch { }
            Release(wb);
            Release(xl);
        }
    }

    // ── PowerPoint ──────────────────────────────────────────────────────────

    string[] PowerPoint(Step s, bool wasRunning)
    {
        dynamic? pp = null, pres = null;
        try
        {
            pp = Create("PowerPoint.Application");
            bool attached = wasRunning;
            pp.DisplayAlerts = 1; // ppAlertsNone
            pp.AutomationSecurity = 3;
            s.Progress.Report(0.15);
            pres = pp.Presentations.Open(FileName: s.Input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
            s.Progress.Report(0.4);
            string outFile = s.Out();
            string[] result;
            switch (s.To.Id)
            {
                case "png" or "jpg":
                {
                    // one picture per slide, at a crisp 1920 px wide
                    int w = s.Quality == Quality.Small ? 1280 : s.Quality == Quality.Best ? 2560 : 1920;
                    double ratio = (double)pres.PageSetup.SlideHeight / (double)pres.PageSetup.SlideWidth;
                    int count = pres.Slides.Count;
                    var files = new List<string>();
                    for (int i = 1; i <= count; i++)
                    {
                        string f = Path.Combine(s.OutDir, $"{s.Name} - {i:D2}.{s.To.Id}");
                        dynamic slide = pres.Slides[i];
                        slide.Export(f, s.To.Id == "png" ? "PNG" : "JPG", w, (int)Math.Round(w * ratio));
                        Release(slide);
                        files.Add(f);
                        s.Progress.Report(0.4 + 0.6 * i / count);
                    }
                    result = files.ToArray();
                    break;
                }
                case "mp4" or "wmv":
                {
                    int height = s.Quality switch { Quality.Best => 1080, Quality.Balanced => 720, _ => 480 };
                    pres.CreateVideo(FileName: outFile, UseTimingsAndNarrations: true, DefaultSlideDuration: 4, VertResolution: height, FramesPerSecond: 30, Quality: 85);
                    // the export runs in the background inside PowerPoint
                    while (true)
                    {
                        Thread.Sleep(500);
                        int status = pres.CreateVideoStatus;
                        if (status == 3) break;            // done
                        if (status == 4) throw new ConvertException(Loc.T("Err.Engine", "PowerPoint", "video export failed"));
                        s.Progress.Report(0.5);
                    }
                    result = [outFile];
                    break;
                }
                default:
                {
                    int code = s.To.Id switch { "pptx" => 24, "ppt" => 1, "ppsx" => 28, "odp" => 35, "pdf" => 32, "xps" => 33, _ => 24 };
                    pres.SaveAs(FileName: outFile, FileFormat: code);
                    result = [outFile];
                    break;
                }
            }
            pres.Close();
            Release(pres); pres = null;
            if (!attached) pp.Quit();
            s.Progress.Report(1);
            return result.Where(File.Exists).ToArray();
        }
        finally
        {
            if (pres != null) try { pres.Close(); } catch { }
            Release(pres);
            Release(pp);
        }
    }
}

/// <summary>
/// LibreOffice, headless: the fallback for Office files when Office isn't installed, and the only way in for
/// Apple Pages / Numbers / Keynote, Visio, Publisher, WordPerfect, CorelDRAW and Hangul documents.
/// </summary>
public sealed class LibreOfficeConverter : Converter
{
    public override string Name => "LibreOffice";
    public override Engine Engine => Engines.LibreOffice;
    static readonly SemaphoreSlim Gate = new(1, 1);

    static readonly string[] Text = ["docx", "doc", "docm", "odt", "rtf", "html", "wpd", "pages", "hwp", "txt"];
    static readonly string[] Sheets = ["xlsx", "xls", "xlsm", "xlsb", "ods", "numbers", "csv"];
    static readonly string[] Slides = ["pptx", "ppt", "ppsx", "odp", "key"];
    static readonly string[] Drawings = ["vsdx", "odg", "pub"];

    public override IEnumerable<Edge> Edges()
    {
        int Cost(string f) => f is "pages" or "numbers" or "key" or "wpd" or "hwp" or "vsdx" or "odg" or "pub" ? 2 : 4;
        foreach (var f in Text) foreach (var t in new[] { "docx", "doc", "odt", "rtf", "txt", "html", "pdf", "epub" }) if (f != t && !(f == "txt" && t is "txt" or "html")) yield return new Edge(f, t, Cost(f));
        foreach (var f in Sheets) foreach (var t in new[] { "xlsx", "xls", "ods", "csv", "pdf", "html" }) if (f != t) yield return new Edge(f, t, f == "csv" ? 5 : Cost(f));
        foreach (var f in Slides) foreach (var t in new[] { "pptx", "ppt", "odp", "pdf" }) if (f != t) yield return new Edge(f, t, Cost(f));
        foreach (var f in Drawings) foreach (var t in new[] { "pdf", "png", "svg", "odg" }) if (f != t) yield return new Edge(f, t, Cost(f));
        // a PDF opened as an editable document, text and pictures in place
        foreach (var t in new[] { "docx", "odt", "rtf" }) yield return new Edge("pdf", t, 3);
    }

    static string Filter(Step s) => (s.To.Id, s.From.Kind) switch
    {
        ("docx", _) => "docx:MS Word 2007 XML",
        ("doc", _) => "doc:MS Word 97",
        ("txt", _) => "txt:Text (encoded):UTF8",
        ("html", Kind.Sheet) => "html:HTML (StarCalc)",
        ("html", _) => "html:XHTML Writer File:UTF8",
        ("xlsx", _) => "xlsx:Calc MS Excel 2007 XML",
        ("xls", _) => "xls:MS Excel 97",
        ("csv", _) => "csv:Text - txt - csv (StarCalc):44,34,76,1",
        ("pptx", _) => "pptx:Impress MS PowerPoint 2007 XML",
        ("ppt", _) => "ppt:MS PowerPoint 97",
        ("svg", _) => "svg:draw_svg_Export",
        (var t, _) => t,
    };

    public override async Task<string[]> Run(Step s)
    {
        await Gate.WaitAsync(s.Cancel);
        try
        {
            // a fresh profile every time: LibreOffice started headless on a profile it has used before can wait
            // forever on something it never shows, while a new one converts in seconds
            string profile = Path.Combine(s.OutDir, "lo-profile");
            s.Progress.Report(0.15);
            Proc.Result r;
            try
            {
                r = await Proc.Run(Engines.LibreOffice.Path!, ["--headless", "--invisible", "--norestore", "--nologo", "--nodefault", "--nolockcheck", "--nofirststartwizard",
                    "-env:UserInstallation=" + new Uri(profile).AbsoluteUri, .. s.From.Id == "pdf" ? new[] { "--infilter=writer_pdf_import" } : [],
                    "--convert-to", Filter(s), "--outdir", s.OutDir, s.Input], s.Cancel, timeout: TimeSpan.FromMinutes(3));
            }
            catch (TimeoutException) { throw new ConvertException(Loc.T("Err.Timeout", "LibreOffice")); }
            // LibreOffice names the result after the input file
            string expected = Path.Combine(s.OutDir, Path.GetFileNameWithoutExtension(s.Input) + s.To.Ext);
            if (!File.Exists(expected))
            {
                string why = (r.Err + "\n" + r.Out).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase)) ?? "no output";
                throw new ConvertException(Loc.T("Err.Engine", "LibreOffice", why));
            }
            string final = s.Out();
            if (!string.Equals(expected, final, StringComparison.OrdinalIgnoreCase)) File.Move(expected, final, true);
            s.Progress.Report(1);
            return [final];
        }
        finally { Gate.Release(); }
    }
}
