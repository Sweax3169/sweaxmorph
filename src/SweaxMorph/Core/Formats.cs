using System.IO;

namespace SweaxMorph.Core;

public enum Kind { Image, Video, Audio, Document, Sheet, Slides, Pdf, Ebook, Data, Archive, Subtitle, Font, Folder }

/// <summary>One file format SweaxMorph knows: its id (also the extension it writes), a label and what it is for.</summary>
public sealed record Format(string Id, string Label, Kind Kind, string[] Extensions, string En, string Tr)
{
    public string Description => Loc.Turkish ? Tr : En;
    public string Ext => Extensions.Length > 0 ? Extensions[0] : "";
    public override string ToString() => Label;
}

public static class Formats
{
    /// <summary>The colour of each kind of file, used on petals, chips and the liquid inside the blob.</summary>
    public static string Hue(Kind k) => k switch
    {
        Kind.Image => "#FF6B9A",
        Kind.Video => "#8B7CFF",
        Kind.Audio => "#2EC4B6",
        Kind.Document => "#4C8DFF",
        Kind.Sheet => "#3CCB7F",
        Kind.Slides => "#FF8A4C",
        Kind.Pdf => "#FF5A5F",
        Kind.Ebook => "#E879F9",
        Kind.Data => "#A3C93A",
        Kind.Archive => "#C08B5C",
        Kind.Subtitle => "#8E9AAF",
        Kind.Font => "#F4B740",
        _ => "#9AA4B2",
    };

    public static string KindName(Kind k) => Loc.T("Kind." + k);

    static Format F(string id, string label, Kind kind, string exts, string en, string tr) =>
        new(id, label, kind, exts.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(e => "." + e).ToArray(), en, tr);

    public static readonly IReadOnlyList<Format> All =
    [
        // images
        F("png", "PNG", Kind.Image, "png", "Lossless image with transparency", "Saydamlık destekli kayıpsız görsel"),
        F("jpg", "JPG", Kind.Image, "jpg jpeg jpe jfif", "Small photo, works everywhere", "Her yerde açılan küçük fotoğraf"),
        F("webp", "WEBP", Kind.Image, "webp", "Modern web image, much smaller", "Çok daha küçük modern web görseli"),
        F("avif", "AVIF", Kind.Image, "avif", "Newest web image, smallest size", "En yeni ve en küçük web görseli"),
        F("heic", "HEIC", Kind.Image, "heic heif hif", "iPhone photo", "iPhone fotoğrafı"),
        F("gif", "GIF", Kind.Image, "gif", "Animation for chats and the web", "Sohbet ve web için animasyon"),
        F("bmp", "BMP", Kind.Image, "bmp dib", "Uncompressed Windows bitmap", "Sıkıştırmasız Windows bitmap"),
        F("tiff", "TIFF", Kind.Image, "tiff tif", "Print and scan quality", "Baskı ve tarama kalitesi"),
        F("ico", "ICO", Kind.Image, "ico", "Windows icon, every size inside", "Tüm boyutları içeren Windows simgesi"),
        F("svg", "SVG", Kind.Image, "svg svgz", "Vector drawing", "Vektör çizim"),
        F("psd", "PSD", Kind.Image, "psd psb", "Photoshop document", "Photoshop belgesi"),
        F("tga", "TGA", Kind.Image, "tga", "Game texture", "Oyun dokusu"),
        F("jxl", "JXL", Kind.Image, "jxl", "JPEG XL, next-gen photo", "JPEG XL, yeni nesil fotoğraf"),
        F("jp2", "JP2", Kind.Image, "jp2 j2k jpf jpx", "JPEG 2000", "JPEG 2000"),
        F("dds", "DDS", Kind.Image, "dds", "DirectX texture", "DirectX dokusu"),
        F("exr", "EXR", Kind.Image, "exr", "HDR for VFX", "Efekt işleri için HDR"),
        F("hdr", "HDR", Kind.Image, "hdr", "Radiance HDR", "Radiance HDR"),
        F("qoi", "QOI", Kind.Image, "qoi", "Quite OK Image", "Quite OK Image"),
        F("pcx", "PCX", Kind.Image, "pcx", "Classic PC Paintbrush", "Klasik PC Paintbrush"),
        F("ppm", "PPM", Kind.Image, "ppm pgm pbm pnm pam", "Netpbm image", "Netpbm görseli"),
        F("emf", "EMF", Kind.Image, "emf wmf", "Windows metafile", "Windows meta dosyası"),
        F("cur", "CUR", Kind.Image, "cur", "Windows cursor", "Windows imleci"),
        F("raw", "RAW", Kind.Image, "dng cr2 cr3 crw nef nrw arw srf sr2 orf rw2 raf srw pef x3f 3fr erf kdc mos mrw iiq rwl", "Camera RAW photo", "Fotoğraf makinesi RAW"),
        F("xcf", "XCF", Kind.Image, "xcf", "GIMP image", "GIMP görseli"),

        // video
        F("mp4", "MP4", Kind.Video, "mp4", "Plays on every device", "Her cihazda oynar"),
        F("mkv", "MKV", Kind.Video, "mkv", "Holds any track and subtitle", "Her izi ve altyazıyı taşır"),
        F("webm", "WEBM", Kind.Video, "webm", "Open web video", "Açık web videosu"),
        F("mov", "MOV", Kind.Video, "mov qt", "QuickTime video", "QuickTime videosu"),
        F("avi", "AVI", Kind.Video, "avi", "Classic Windows video", "Klasik Windows videosu"),
        F("wmv", "WMV", Kind.Video, "wmv asf", "Windows Media video", "Windows Media videosu"),
        F("flv", "FLV", Kind.Video, "flv f4v", "Flash video", "Flash videosu"),
        F("m4v", "M4V", Kind.Video, "m4v", "iTunes video", "iTunes videosu"),
        F("mpg", "MPG", Kind.Video, "mpg mpeg m2v vob", "MPEG-2 for DVD players", "DVD oynatıcılar için MPEG-2"),
        F("ts", "TS", Kind.Video, "ts mts m2ts", "Broadcast stream", "Yayın akışı"),
        F("3gp", "3GP", Kind.Video, "3gp 3g2", "Old phone video", "Eski telefon videosu"),
        F("ogv", "OGV", Kind.Video, "ogv", "Ogg Theora video", "Ogg Theora videosu"),
        F("apng", "APNG", Kind.Image, "apng", "Animated PNG", "Hareketli PNG"),

        // audio
        F("mp3", "MP3", Kind.Audio, "mp3", "Music that plays anywhere", "Her yerde çalan müzik"),
        F("wav", "WAV", Kind.Audio, "wav wave", "Uncompressed studio audio", "Sıkıştırmasız stüdyo sesi"),
        F("flac", "FLAC", Kind.Audio, "flac", "Lossless, half the size", "Kayıpsız, yarı boyut"),
        F("m4a", "M4A", Kind.Audio, "m4a", "AAC audio for Apple", "Apple için AAC ses"),
        F("aac", "AAC", Kind.Audio, "aac", "Raw AAC audio", "Ham AAC ses"),
        F("ogg", "OGG", Kind.Audio, "ogg oga", "Ogg Vorbis", "Ogg Vorbis"),
        F("opus", "OPUS", Kind.Audio, "opus", "Best quality per byte", "Bayt başına en iyi kalite"),
        F("wma", "WMA", Kind.Audio, "wma", "Windows Media audio", "Windows Media sesi"),
        F("aiff", "AIFF", Kind.Audio, "aiff aif aifc", "Mac uncompressed audio", "Mac sıkıştırmasız ses"),
        F("ac3", "AC3", Kind.Audio, "ac3", "Dolby Digital", "Dolby Digital"),
        F("m4r", "M4R", Kind.Audio, "m4r", "iPhone ringtone", "iPhone zil sesi"),
        F("mka", "MKA", Kind.Audio, "mka", "Matroska audio", "Matroska sesi"),
        F("wv", "WV", Kind.Audio, "wv", "WavPack", "WavPack"),
        F("ape", "APE", Kind.Audio, "ape", "Monkey's Audio", "Monkey's Audio"),
        F("amr", "AMR", Kind.Audio, "amr", "Voice recording", "Ses kaydı"),
        F("au", "AU", Kind.Audio, "au snd", "Sun audio", "Sun sesi"),
        F("dts", "DTS", Kind.Audio, "dts", "DTS surround", "DTS surround"),

        // subtitles
        F("srt", "SRT", Kind.Subtitle, "srt", "Subtitles for every player", "Her oynatıcı için altyazı"),
        F("vtt", "VTT", Kind.Subtitle, "vtt", "Web video subtitles", "Web video altyazısı"),
        F("ass", "ASS", Kind.Subtitle, "ass ssa", "Styled anime subtitles", "Stilli altyazı"),
        F("ttml", "TTML", Kind.Subtitle, "ttml dfxp", "Timed Text XML", "Timed Text XML"),
        F("lrc", "LRC", Kind.Subtitle, "lrc", "Song lyrics with timing", "Zamanlı şarkı sözü"),
        F("sub", "SUB", Kind.Subtitle, "sub", "MicroDVD subtitles", "MicroDVD altyazısı"),

        // documents
        F("docx", "DOCX", Kind.Document, "docx", "Word document", "Word belgesi"),
        F("doc", "DOC", Kind.Document, "doc", "Word 97–2003", "Word 97–2003"),
        F("docm", "DOCM", Kind.Document, "docm dotx dotm dot", "Word with macros or template", "Makrolu Word ya da şablon"),
        F("odt", "ODT", Kind.Document, "odt fodt", "OpenDocument text", "OpenDocument metni"),
        F("rtf", "RTF", Kind.Document, "rtf", "Rich text for any editor", "Her editörde zengin metin"),
        F("txt", "TXT", Kind.Document, "txt text log", "Plain text", "Düz metin"),
        F("md", "MD", Kind.Document, "md markdown mkd", "Markdown", "Markdown"),
        F("html", "HTML", Kind.Document, "html htm xhtml", "Web page", "Web sayfası"),
        F("mht", "MHT", Kind.Document, "mht mhtml", "Web page in one file", "Tek dosyada web sayfası"),
        F("tex", "TEX", Kind.Document, "tex latex", "LaTeX source", "LaTeX kaynağı"),
        F("rst", "RST", Kind.Document, "rst", "reStructuredText", "reStructuredText"),
        F("org", "ORG", Kind.Document, "org", "Emacs Org mode", "Emacs Org mode"),
        F("adoc", "ADOC", Kind.Document, "adoc asciidoc", "AsciiDoc", "AsciiDoc"),
        F("textile", "TEXTILE", Kind.Document, "textile", "Textile markup", "Textile işaretleme"),
        F("wiki", "WIKI", Kind.Document, "wiki mediawiki", "MediaWiki markup", "MediaWiki işaretleme"),
        F("ipynb", "IPYNB", Kind.Document, "ipynb", "Jupyter notebook", "Jupyter defteri"),
        F("typ", "TYP", Kind.Document, "typ", "Typst source", "Typst kaynağı"),
        F("dbk", "DOCBOOK", Kind.Document, "dbk docbook", "DocBook XML", "DocBook XML"),
        F("xps", "XPS", Kind.Document, "xps oxps", "XML Paper", "XML Kâğıt"),
        F("pages", "PAGES", Kind.Document, "pages", "Apple Pages", "Apple Pages"),
        F("wpd", "WPD", Kind.Document, "wpd wps wp5", "WordPerfect / Works", "WordPerfect / Works"),
        F("pub", "PUB", Kind.Document, "pub", "Publisher", "Publisher"),
        F("vsdx", "VSDX", Kind.Document, "vsdx vsd vdx", "Visio drawing", "Visio çizimi"),
        F("odg", "ODG", Kind.Document, "odg cdr", "Drawing (ODG, CorelDRAW)", "Çizim (ODG, CorelDRAW)"),
        F("hwp", "HWP", Kind.Document, "hwp", "Hangul document", "Hangul belgesi"),

        // spreadsheets
        F("xlsx", "XLSX", Kind.Sheet, "xlsx", "Excel workbook", "Excel çalışma kitabı"),
        F("xls", "XLS", Kind.Sheet, "xls", "Excel 97–2003", "Excel 97–2003"),
        F("xlsm", "XLSM", Kind.Sheet, "xlsm xltx xltm", "Excel with macros or template", "Makrolu Excel ya da şablon"),
        F("xlsb", "XLSB", Kind.Sheet, "xlsb", "Excel binary, fast and small", "Hızlı ve küçük Excel ikili"),
        F("ods", "ODS", Kind.Sheet, "ods fods", "OpenDocument sheet", "OpenDocument tablo"),
        F("csv", "CSV", Kind.Sheet, "csv", "Comma-separated table", "Virgülle ayrılmış tablo"),
        F("tsv", "TSV", Kind.Sheet, "tsv tab", "Tab-separated table", "Sekmeyle ayrılmış tablo"),
        F("numbers", "NUMBERS", Kind.Sheet, "numbers", "Apple Numbers", "Apple Numbers"),

        // slides
        F("pptx", "PPTX", Kind.Slides, "pptx", "PowerPoint deck", "PowerPoint sunusu"),
        F("ppt", "PPT", Kind.Slides, "ppt pps pot", "PowerPoint 97–2003", "PowerPoint 97–2003"),
        F("ppsx", "PPSX", Kind.Slides, "ppsx pptm potx", "PowerPoint show or template", "PowerPoint gösterisi ya da şablon"),
        F("odp", "ODP", Kind.Slides, "odp fodp", "OpenDocument slides", "OpenDocument sunu"),
        F("key", "KEY", Kind.Slides, "key", "Apple Keynote", "Apple Keynote"),

        F("pdf", "PDF", Kind.Pdf, "pdf", "Looks the same everywhere", "Her yerde aynı görünür"),

        // ebooks
        F("epub", "EPUB", Kind.Ebook, "epub", "E-book for every reader", "Her okuyucu için e-kitap"),
        F("mobi", "MOBI", Kind.Ebook, "mobi prc", "Kindle (older)", "Kindle (eski)"),
        F("azw3", "AZW3", Kind.Ebook, "azw3 azw kfx", "Kindle", "Kindle"),
        F("fb2", "FB2", Kind.Ebook, "fb2", "FictionBook", "FictionBook"),
        F("cbz", "CBZ", Kind.Ebook, "cbz cbr cb7", "Comic book", "Çizgi roman"),
        F("lit", "LIT", Kind.Ebook, "lit lrf pdb snb tcr", "Old e-reader formats", "Eski e-okuyucu biçimleri"),

        // data
        F("json", "JSON", Kind.Data, "json geojson", "Data for code and APIs", "Kod ve API verisi"),
        F("yaml", "YAML", Kind.Data, "yaml yml", "Human-friendly config", "Okunaklı yapılandırma"),
        F("toml", "TOML", Kind.Data, "toml", "Simple config", "Sade yapılandırma"),
        F("xml", "XML", Kind.Data, "xml plist", "Structured markup data", "Yapısal işaretleme verisi"),
        F("jsonl", "JSONL", Kind.Data, "jsonl ndjson", "One JSON record per line", "Satır başına bir JSON kaydı"),

        // archives
        F("zip", "ZIP", Kind.Archive, "zip", "Opens on every computer", "Her bilgisayarda açılır"),
        F("7z", "7Z", Kind.Archive, "7z", "Strongest compression", "En güçlü sıkıştırma"),
        F("rar", "RAR", Kind.Archive, "rar", "WinRAR archive", "WinRAR arşivi"),
        F("tar", "TAR", Kind.Archive, "tar", "Unix tape archive", "Unix arşivi"),
        F("tgz", "TAR.GZ", Kind.Archive, "tar.gz tgz", "Compressed tar (gzip)", "Sıkıştırılmış tar (gzip)"),
        F("tbz2", "TAR.BZ2", Kind.Archive, "tar.bz2 tbz2 tbz", "Compressed tar (bzip2)", "Sıkıştırılmış tar (bzip2)"),
        F("txz", "TAR.XZ", Kind.Archive, "tar.xz txz", "Compressed tar (xz)", "Sıkıştırılmış tar (xz)"),
        F("gz", "GZ", Kind.Archive, "gz", "Single gzip file", "Tek gzip dosyası"),
        F("bz2", "BZ2", Kind.Archive, "bz2", "Single bzip2 file", "Tek bzip2 dosyası"),
        F("xz", "XZ", Kind.Archive, "xz lzma", "Single xz file", "Tek xz dosyası"),
        F("iso", "ISO", Kind.Archive, "iso img", "Disc image", "Disk kalıbı"),
        F("cab", "CAB", Kind.Archive, "cab", "Windows cabinet", "Windows cabinet"),

        // fonts
        F("ttf", "TTF", Kind.Font, "ttf", "TrueType font", "TrueType yazı tipi"),
        F("otf", "OTF", Kind.Font, "otf", "OpenType font", "OpenType yazı tipi"),
        F("woff", "WOFF", Kind.Font, "woff", "Web font", "Web yazı tipi"),
        F("woff2", "WOFF2", Kind.Font, "woff2", "Smallest web font", "En küçük web yazı tipi"),

        // a folder: archives open into one, anything can be packed from one
        F("folder", "", Kind.Folder, "", "Unpack into a folder", "Bir klasöre aç"),
    ];

    static readonly Dictionary<string, Format> ById = All.ToDictionary(f => f.Id);
    static readonly Dictionary<string, Format> ByExt = BuildExt();

    static Dictionary<string, Format> BuildExt()
    {
        var d = new Dictionary<string, Format>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in All)
            foreach (var e in f.Extensions)
                d.TryAdd(e, f);
        return d;
    }

    public static Format Get(string id) => ById[id];
    public static Format? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>What a file is, from its name (double extensions such as .tar.gz first), or null when unknown.</summary>
    public static Format? Of(string path)
    {
        if (Directory.Exists(path)) return ById["folder"];
        string name = Path.GetFileName(path).ToLowerInvariant();
        int dot = name.IndexOf('.');
        while (dot >= 0)
        {
            if (ByExt.TryGetValue(name[dot..], out var f)) return f;
            dot = name.IndexOf('.', dot + 1);
        }
        return Sniff(path);
    }

    /// <summary>For files with a missing or wrong extension: look at the first bytes.</summary>
    static Format? Sniff(string path)
    {
        try
        {
            Span<byte> b = stackalloc byte[16];
            using var fs = File.OpenRead(path);
            int n = fs.Read(b);
            if (n < 4) return null;
            if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return ById["png"];
            if (b[0] == 0xFF && b[1] == 0xD8) return ById["jpg"];
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return ById["gif"];
            if (b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F') return ById["pdf"];
            if (b[0] == 'P' && b[1] == 'K') return ById["zip"];
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && n >= 12)
                return b[8] == 'W' && b[9] == 'E' && b[10] == 'B' ? ById["webp"] : b[8] == 'W' && b[9] == 'A' ? ById["wav"] : b[8] == 'A' && b[9] == 'V' ? ById["avi"] : null;
            if (n >= 12 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p')
            {
                string brand = System.Text.Encoding.ASCII.GetString(b[8..12]);
                return brand is "heic" or "heix" or "mif1" or "msf1" ? ById["heic"] : brand is "avif" or "avis" ? ById["avif"] : brand.StartsWith("M4A") ? ById["m4a"] : ById["mp4"];
            }
            if (b[0] == 'I' && b[1] == 'D' && b[2] == '3') return ById["mp3"];
            if (b[0] == 'f' && b[1] == 'L' && b[2] == 'a' && b[3] == 'C') return ById["flac"];
            if (b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S') return ById["ogg"];
            if (b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return ById["mkv"];
            if (b[0] == '7' && b[1] == 'z' && b[2] == 0xBC && b[3] == 0xAF) return ById["7z"];
            if (b[0] == 'R' && b[1] == 'a' && b[2] == 'r' && b[3] == '!') return ById["rar"];
            if (b[0] == 0x1F && b[1] == 0x8B) return ById["gz"];
            if (b[0] == '{' || b[0] == '[') return ById["json"];
        }
        catch { }
        return null;
    }

    /// <summary>The file name a result gets: "photo.heic" → "photo.png"; a folder for unpacked archives.</summary>
    public static string BaseName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        foreach (var multi in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (name.EndsWith(multi, StringComparison.OrdinalIgnoreCase)) return name[..^multi.Length];
        return Directory.Exists(path) ? name : Path.GetFileNameWithoutExtension(name);
    }
}
