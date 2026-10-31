using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using ImageMagick;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SweaxMorph.Core.Converters;

/// <summary>
/// Archives: unpack anything (ZIP, 7Z, RAR, TAR.*, ISO, CAB), repack into another kind, or pack files and
/// folders into ZIP, 7Z or TAR.GZ. Comic books (CBZ/CBR) can become a PDF, and pictures a comic book.
/// </summary>
public sealed class ArchiveConverter : Converter
{
    public override string Name => "SweaxMorph";

    static readonly string[] Readable = ["zip", "7z", "rar", "tar", "tgz", "tbz2", "txz", "gz", "bz2", "xz", "iso", "cab", "cbz"];
    static readonly string[] Packs = ["zip", "7z", "tgz", "tar"];

    public override IEnumerable<Edge> Edges()
    {
        foreach (var a in Readable) yield return new Edge(a, "folder", 1);
        foreach (var e in Cross(Readable, Packs, 2)) yield return e;
        yield return new Edge("cbz", "pdf", 3);
        yield return new Edge("zip", "cbz", 1);
        yield return new Edge("cbz", "zip", 1);
        foreach (var img in new[] { "png", "jpg", "webp", "gif", "bmp", "avif" }) yield return new Edge(img, "cbz", 2, Combines: true);
        // pack anything (alone, or everything dropped together)
        foreach (var f in Formats.All.Select(f => f.Id))
            foreach (var p in new[] { "zip", "7z", "tgz" })
            {
                if (f == p || Readable.Contains(f) && f != "folder") continue;
                yield return new Edge(f, p, f == "folder" ? 1 : 6);
                yield return new Edge(f, p, 1, Combines: true);
            }
    }

    public override async Task<string[]> Run(Step s)
    {
        string to = s.To.Id;
        if (to == "7z" && !Engines.SevenZip.Ready && !await Engines.Install(Engines.SevenZip, null, s.Cancel))
            throw new ConvertException(Loc.T("Err.Download", "7-Zip"));

        return await Task.Run(async () =>
        {
            bool fromArchive = s.Inputs.Length == 1 && Readable.Contains(s.From.Id) && s.From.Id != "folder";
            if (to == "folder")
            {
                string dir = Path.Combine(s.OutDir, s.Name);
                await Extract(s.Input, s.From.Id, dir, s);
                return new[] { dir };
            }
            if (to == "pdf") return new[] { ComicToPdf(s) };

            // what goes in: an unpacked archive, a folder, or the dropped files themselves
            string[] items;
            if (fromArchive && !(s.From.Id == "zip" && to == "cbz" || s.From.Id == "cbz" && to == "zip"))
            {
                string tmp = Path.Combine(s.OutDir, "~in");
                await Extract(s.Input, s.From.Id, tmp, s);
                items = Directory.EnumerateFileSystemEntries(tmp).ToArray();
            }
            else if (fromArchive)
            {
                // zip ⇄ cbz is the same thing with another name
                string copy = s.Out();
                File.Copy(s.Input, copy);
                return new[] { copy };
            }
            else items = s.Inputs;

            string outFile = Path.Combine(s.OutDir, s.Name + (to switch { "tgz" => ".tar.gz", "tbz2" => ".tar.bz2", _ => s.To.Ext }));
            s.Progress.Report(0.5);
            await Pack(items, to, outFile, s);
            s.Progress.Report(1);
            return new[] { outFile };
        }, s.Cancel);
    }

    static async Task Extract(string file, string format, string dir, Step s)
    {
        Directory.CreateDirectory(dir);
        string full = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        if (format is "iso" or "cab")
        {
            // Windows' own tar (libarchive) reads disc images and cabinets; full 7-Zip if it's there
            var r = Engines.FullSevenZip
                ? await Proc.Run(Engines.SevenZip.Path!, ["x", "-y", "-o" + dir, file], s.Cancel)
                : await Proc.Run(Engines.SystemTar.Path ?? "tar.exe", ["-xf", file, "-C", dir], s.Cancel);
            if (r.Code != 0) throw new ConvertException(Loc.T("Err.Engine", "tar", r.Err.Split('\n')[0].Trim()));
            return;
        }
        var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };
        if (format is "zip" or "7z" or "rar" or "cbz")
        {
            using var archive = ArchiveFactory.OpenArchive(file, new ReaderOptions());
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            int i = 0;
            foreach (var e in entries)
            {
                s.Cancel.ThrowIfCancellationRequested();
                Guard(full, e.Key);
                e.WriteToDirectory(dir, opts);
                s.Progress.Report(0.5 * ++i / Math.Max(1, entries.Count));
            }
            return;
        }
        using (var fs = File.OpenRead(file))
        using (var reader = ReaderFactory.OpenReader(fs, new ReaderOptions()))
        {
            while (reader.MoveToNextEntry())
            {
                s.Cancel.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;
                Guard(full, reader.Entry.Key);
                if (format is "gz" or "bz2" or "xz" && string.IsNullOrEmpty(reader.Entry.Key))
                {
                    // a single compressed file has no name inside: use the archive's own name without the extension
                    reader.WriteEntryToFile(Path.Combine(dir, Path.GetFileNameWithoutExtension(file)), opts);
                }
                else reader.WriteEntryToDirectory(dir, opts);
                s.Progress.Report(0.5 * fs.Position / Math.Max(1, fs.Length));
            }
        }
    }

    /// <summary>Refuses entries like "../../Windows/x.dll" that would land outside the folder.</summary>
    static void Guard(string root, string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        string target = Path.GetFullPath(Path.Combine(root, key));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ConvertException(Loc.T("Err.Unsafe"));
    }

    static async Task Pack(string[] items, string to, string outFile, Step s)
    {
        var level = s.Quality switch { Quality.Best => CompressionLevel.SmallestSize, Quality.Small => CompressionLevel.SmallestSize, _ => CompressionLevel.Optimal };
        switch (to)
        {
            case "zip" or "cbz":
            {
                using var zip = ZipFile.Open(outFile, ZipArchiveMode.Create);
                int i = 0;
                var files = Expand(items).ToList();
                foreach (var (path, name) in files)
                {
                    s.Cancel.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(path, name, to == "cbz" ? CompressionLevel.NoCompression : level);
                    s.Progress.Report(0.5 + 0.5 * ++i / Math.Max(1, files.Count));
                }
                break;
            }
            case "7z":
            {
                string mx = s.Quality switch { Quality.Best => "-mx=9", Quality.Small => "-mx=9", _ => "-mx=7" };
                var r = await Proc.Run(Engines.SevenZip.Path!, ["a", "-t7z", mx, "-y", outFile, .. items], s.Cancel);
                if (r.Code != 0) throw new ConvertException(Loc.T("Err.Engine", "7-Zip", r.Err.Split('\n')[0].Trim()));
                break;
            }
            default:
            {
                await using var fs = File.Create(outFile);
                Stream body = to switch
                {
                    "tgz" => new GZipStream(fs, level, leaveOpen: true),
                    _ => fs,
                };
                await using (var tar = new TarWriter(body, TarEntryFormat.Pax, leaveOpen: true))
                    foreach (var (path, name) in Expand(items))
                    {
                        s.Cancel.ThrowIfCancellationRequested();
                        await tar.WriteEntryAsync(path, name.Replace('\\', '/'), s.Cancel);
                    }
                if (body != fs) await body.DisposeAsync();
                break;
            }
        }
    }

    /// <summary>Every file under the dropped items, with the name it gets inside the archive.</summary>
    static IEnumerable<(string path, string name)> Expand(string[] items)
    {
        foreach (var item in items)
        {
            if (File.Exists(item)) { yield return (item, Path.GetFileName(item)); continue; }
            if (!Directory.Exists(item)) continue;
            string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(item)) ?? item;
            foreach (var f in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                yield return (f, Path.GetRelativePath(parent, f));
        }
    }

    /// <summary>A comic book becomes a PDF, one page per picture, in reading order.</summary>
    static string ComicToPdf(Step s)
    {
        string tmp = Path.Combine(s.OutDir, "~pages");
        Extract(s.Input, "cbz", tmp, s).GetAwaiter().GetResult();
        var pages = Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories)
            .Where(f => Formats.Of(f)?.Kind == Kind.Image)
            .OrderBy(f => f, NaturalOrder.Instance).ToList();
        if (pages.Count == 0) throw new ConvertException(Loc.T("Err.NoPages"));
        using var set = new MagickImageCollection();
        foreach (var p in pages) { var img = new MagickImage(p); img.AutoOrient(); img.Density = new Density(150); set.Add(img); }
        string outFile = s.Out();
        set.Write(outFile, MagickFormat.Pdf);
        return outFile;
    }
}

/// <summary>"page2" before "page10", the way people count.</summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();
    public int Compare(string? a, string? b) => StrCmpLogicalW(a ?? "", b ?? "");
    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int StrCmpLogicalW(string x, string y);
}

/// <summary>E-books through Calibre's converter when Calibre is installed: Kindle formats included.</summary>
public sealed class EbookConverter : Converter
{
    public override string Name => "Calibre";
    public override Engine Engine => Engines.Calibre;

    public override IEnumerable<Edge> Edges()
    {
        string[] from = ["epub", "mobi", "azw3", "fb2", "lit", "cbz", "docx", "rtf", "txt", "html", "odt", "pdf"];
        string[] to = ["epub", "mobi", "azw3", "fb2", "pdf", "docx", "txt", "rtf"];
        foreach (var e in Cross(from, to, 3))
            if (Formats.Get(e.From).Kind == Kind.Ebook || Formats.Get(e.To).Kind == Kind.Ebook)
                yield return e;
    }

    public override async Task<string[]> Run(Step s)
    {
        string outFile = s.Out();
        var r = await Proc.Run(Engines.Calibre.Path!, [s.Input, outFile], s.Cancel, onOut: line =>
        {
            // "34% Converting input to HTML..."
            int pct = line.IndexOf('%');
            if (pct > 0 && int.TryParse(line.AsSpan(0, pct).Trim(), out int v)) s.Progress.Report(v / 100.0);
        }, timeout: TimeSpan.FromMinutes(10));
        if (!File.Exists(outFile)) throw new ConvertException(Loc.T("Err.Engine", "Calibre", r.Err.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? ""));
        return [outFile];
    }
}
