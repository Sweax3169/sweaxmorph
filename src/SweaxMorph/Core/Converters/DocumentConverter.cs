using System.IO;

namespace SweaxMorph.Core.Converters;

/// <summary>Markup and documents through Pandoc: Markdown, Word, HTML, EPUB, LaTeX, Jupyter, wiki formats…</summary>
public sealed class DocumentConverter : Converter
{
    public override string Name => "Pandoc";
    public override Engine Engine => Engines.Pandoc;

    static readonly Dictionary<string, string> Readers = new()
    {
        ["md"] = "markdown", ["docx"] = "docx", ["odt"] = "odt", ["rtf"] = "rtf", ["epub"] = "epub", ["html"] = "html", ["rst"] = "rst",
        ["tex"] = "latex", ["org"] = "org", ["textile"] = "textile", ["wiki"] = "mediawiki", ["ipynb"] = "ipynb", ["fb2"] = "fb2",
        ["dbk"] = "docbook", ["typ"] = "typst", ["txt"] = "markdown", ["csv"] = "csv", ["tsv"] = "tsv",
    };

    static readonly Dictionary<string, string> Writers = new()
    {
        ["md"] = "gfm", ["docx"] = "docx", ["odt"] = "odt", ["rtf"] = "rtf", ["epub"] = "epub3", ["html"] = "html5", ["rst"] = "rst",
        ["tex"] = "latex", ["org"] = "org", ["textile"] = "textile", ["wiki"] = "mediawiki", ["ipynb"] = "ipynb", ["fb2"] = "fb2",
        ["dbk"] = "docbook5", ["typ"] = "typst", ["adoc"] = "asciidoc", ["txt"] = "plain", ["pptx"] = "pptx",
    };

    public override IEnumerable<Edge> Edges()
    {
        foreach (var (from, _) in Readers)
            foreach (var (to, _) in Writers)
                if (from != to && !(from is "csv" or "tsv" && to is "pptx" or "epub" or "fb2"))
                    yield return new Edge(from, to, Cost(from, to));
    }

    // Office files are better served by Office itself; Pandoc is the fallback there
    static int Cost(string from, string to) => (from, to) switch
    {
        ("docx" or "odt" or "rtf", "docx" or "odt" or "rtf") => 5,
        (_, "pptx") => 4,
        (_, "rtf") => 4, // Pandoc's RTF is plain; better as a last resort
        (_, "html") => 1, // its best writer, and the way to a styled PDF
        _ => 2,
    };

    public override async Task<string[]> Run(Step s)
    {
        string from = s.From.Id, to = s.To.Id;
        string outFile = s.Out();
        var args = new List<string> { "--from", Readers[from], "--to", Writers[to], "--output", outFile, "--resource-path", Path.GetDirectoryName(s.Input) ?? "." };
        if (from is "md" or "txt") args[1] = "markdown+hard_line_breaks";
        if (to is "html")
        {
            string css = Path.Combine(s.OutDir, "sweaxmorph.css");
            await File.WriteAllTextAsync(css, Css, s.Cancel);
            args.AddRange(["--standalone", "--embed-resources", "--css", css, "--metadata", "pagetitle=" + Formats.BaseName(s.Input)]);
        }
        else if (to is "docx" or "odt" or "rtf" or "epub" or "fb2" or "tex" or "typ" or "ipynb" or "pptx" or "dbk") args.Add("--standalone");
        if (to == "epub") args.AddRange(["--metadata", "title=" + Formats.BaseName(s.Input)]);
        if (to is "docx" or "odt" or "epub" && from is "docx" or "odt" or "epub" or "html") args.Add("--extract-media=" + Path.Combine(s.OutDir, "media"));
        if (to == "md") args.Add("--wrap=none");
        args.Add(s.Input);

        s.Progress.Report(0.2);
        var r = await Proc.Run(Engines.Pandoc.Path!, args, s.Cancel, cwd: s.OutDir, timeout: TimeSpan.FromMinutes(5));
        if (r.Code != 0 || !File.Exists(outFile))
        {
            string why = r.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "Pandoc";
            throw new ConvertException(Loc.T("Err.Engine", "Pandoc", why.Length > 180 ? why[..180] : why));
        }
        s.Progress.Report(1);
        return [outFile];
    }

    /// <summary>A calm, readable page for every HTML (and, through the browser, PDF) that SweaxMorph writes.</summary>
    internal const string Css = """
        :root { color-scheme: light; }
        html { font-size: 17px; }
        body { max-width: 46rem; margin: 3rem auto; padding: 0 1.5rem; font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
               line-height: 1.65; color: #1d1b26; background: #fff; }
        h1, h2, h3, h4 { font-family: "Segoe UI Variable Display", "Segoe UI", sans-serif; line-height: 1.25; margin: 2.2rem 0 .8rem; color: #14121c; }
        h1 { font-size: 2.1rem; letter-spacing: -.01em; } h2 { font-size: 1.55rem; } h3 { font-size: 1.2rem; }
        p, ul, ol, table, pre, blockquote { margin: 0 0 1.1rem; }
        a { color: #5b4ee0; }
        img { max-width: 100%; height: auto; border-radius: 6px; }
        code { font-family: "Cascadia Code", Consolas, monospace; font-size: .9em; background: #f3f1f8; padding: .1em .35em; border-radius: 4px; }
        pre { background: #f6f4fb; padding: 1rem 1.1rem; border-radius: 8px; overflow-x: auto; }
        pre code { background: none; padding: 0; }
        blockquote { border-left: 4px solid #d9d3f5; padding: .2rem 0 .2rem 1rem; color: #4a4659; }
        table { border-collapse: collapse; width: 100%; font-size: .94rem; }
        th, td { border: 1px solid #e4e0ee; padding: .45rem .7rem; text-align: left; vertical-align: top; }
        th { background: #f6f4fb; }
        hr { border: 0; border-top: 1px solid #e4e0ee; margin: 2rem 0; }
        @media print { body { margin: 0 auto; max-width: none; } a { color: inherit; } pre, blockquote, table, img { break-inside: avoid; } }
        """;
}

/// <summary>HTML (and SVG, Markdown via HTML…) to PDF with the browser's own print engine, headless.</summary>
public sealed class BrowserPdfConverter : Converter
{
    public override string Name => "Edge";
    public override Engine Engine => Engines.Browser;

    public override IEnumerable<Edge> Edges()
    {
        yield return new Edge("html", "pdf", 1);
        yield return new Edge("mht", "pdf", 1);
        yield return new Edge("svg", "pdf", 2);
    }

    public override async Task<string[]> Run(Step s)
    {
        string outFile = s.Out();
        string profile = Path.Combine(s.OutDir, "profile");
        string input = s.Input;
        if (s.From.Id == "svg")
        {
            // a page exactly as big as the drawing
            input = Path.Combine(s.OutDir, "page.html");
            string svg = await File.ReadAllTextAsync(s.Input, s.Cancel);
            await File.WriteAllTextAsync(input, $"<!doctype html><meta charset=utf-8><style>@page{{margin:0}}html,body{{margin:0}}svg{{display:block;max-width:100%;height:auto}}</style>{svg}", s.Cancel);
        }
        s.Progress.Report(0.2);
        var r = await Proc.Run(Engines.Browser.Path!, [
            "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--user-data-dir=" + profile,
            "--no-pdf-header-footer", "--print-to-pdf=" + outFile, new Uri(input).AbsoluteUri], s.Cancel, timeout: TimeSpan.FromMinutes(2));
        for (int i = 0; i < 20 && !File.Exists(outFile); i++) await Task.Delay(150, s.Cancel); // the browser may hand off to a child
        if (!File.Exists(outFile)) throw new ConvertException(Loc.T("Err.Engine", "Edge", r.Err.Split('\n').FirstOrDefault()?.Trim() ?? ""));
        try { Directory.Delete(profile, true); } catch { }
        s.Progress.Report(1);
        return [outFile];
    }
}
