using System.Globalization;

namespace SweaxMorph.Core;

/// <summary>English and Turkish. "auto" follows the Windows display language.</summary>
public static class Loc
{
    public static bool Turkish { get; private set; }

    public static void Init(string? language)
    {
        Turkish = language switch
        {
            "tr" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr",
        };
    }

    public static string T(string key, params object?[] args)
    {
        var table = Turkish ? Tr : En;
        string s = table.TryGetValue(key, out var v) ? v : En.TryGetValue(key, out var e) ? e : key;
        return args.Length == 0 ? s : string.Format(s, args);
    }

    /// <summary>"1,4 MB" / "1.4 MB".</summary>
    public static string Size(long bytes)
    {
        var c = Turkish ? CultureInfo.GetCultureInfo("tr-TR") : CultureInfo.InvariantCulture;
        if (bytes < 1024) return bytes.ToString(c) + " B";
        double k = bytes / 1024.0;
        if (k < 1024) return k.ToString(k < 10 ? "0.0" : "0", c) + " KB";
        double m = k / 1024;
        if (m < 1024) return m.ToString(m < 10 ? "0.0" : "0", c) + " MB";
        return (m / 1024).ToString("0.0", c) + " GB";
    }

    static readonly Dictionary<string, string> En = new()
    {
        ["Kind.Image"] = "Image", ["Kind.Video"] = "Video", ["Kind.Audio"] = "Audio", ["Kind.Document"] = "Document", ["Kind.Sheet"] = "Spreadsheet",
        ["Kind.Slides"] = "Slides", ["Kind.Pdf"] = "PDF", ["Kind.Ebook"] = "E-book", ["Kind.Data"] = "Data", ["Kind.Archive"] = "Archive",
        ["Kind.Subtitle"] = "Subtitles", ["Kind.Font"] = "Font", ["Kind.Folder"] = "Folder",

        ["Folder"] = "Folder",
        ["SmallSuffix"] = "(smaller)",
        ["Stage.Download"] = "Getting {0} ready — once only",
        ["Stage.Convert"] = "Making {0} with {1}",
        ["Stage.Save"] = "Saving",

        ["Err.Unknown"] = "SweaxMorph doesn't know this kind of file.",
        ["Err.NoRoute"] = "There's no way from {0} to {1} yet.",
        ["Err.Download"] = "Couldn't download {0}. Check the connection and try again.",
        ["Err.Empty"] = "The conversion finished but made nothing.",
        ["Err.General"] = "Something went wrong: {0}",
        ["Err.NoSubs"] = "This video has no subtitles inside.",
        ["Err.NoAudio"] = "This file has no sound to keep.",
        ["Err.Engine"] = "{0} said: {1}",
        ["Err.Timeout"] = "{0} stopped answering, so it was closed.",
        ["Err.Missing"] = "{0} isn't installed.",
        ["Err.Password"] = "This PDF is locked with a password.",
        ["Err.NoText"] = "No text was found in this picture.",
        ["Err.NoOcr"] = "Windows has no text recognition language installed.",
        ["Err.Unsafe"] = "This archive tries to write outside its folder, so it was stopped.",
        ["Err.NoPages"] = "There are no pictures inside this comic book.",
        ["Err.NeedsEngine"] = "{1} needs {0}.",

        // the well
        ["Drop.Title"] = "Drop files here",
        ["Drop.Hint"] = "or click to pick · {0} to open me anywhere",
        ["Drop.Over"] = "Let go!",
        ["Drop.Petal"] = "Drop on Morph, or right on a petal",
        ["Drop.Many"] = "{0} files",
        ["Drop.Mixed"] = "{0} files · {1} kinds",
        ["Pick.Title"] = "Turn {0} into…",
        ["Pick.More"] = "More",
        ["Pick.Search"] = "Search formats",
        ["Pick.Shrink"] = "Shrink",
        ["Pick.ShrinkHint"] = "Same format, much smaller",
        ["Pick.Extract"] = "Unpack",
        ["Pick.Combine"] = "Combine into one file",
        ["Pick.NoTargets"] = "Nothing to make from these yet.",
        ["Pick.Needs"] = "needs {0}",
        ["Pick.Download"] = "downloads {0} once ({1})",
        ["Quality.Best"] = "Best", ["Quality.Balanced"] = "Balanced", ["Quality.Small"] = "Small",
        ["Work.Title"] = "Converting",
        ["Work.Cancel"] = "Stop",
        ["Done.One"] = "Done!",
        ["Done.Many"] = "{0} done!",
        ["Done.Partial"] = "{0} done, {1} failed",
        ["Done.Failed"] = "That didn't work",
        ["Done.Open"] = "Open",
        ["Done.Show"] = "Show in folder",
        ["Done.Copy"] = "Copy",
        ["Done.Copied"] = "Copied",
        ["Done.Again"] = "Convert again",
        ["Done.Drag"] = "Drag the result anywhere",
        ["Done.Saved"] = "Saved",
        ["Done.Install"] = "Get {0}",

        // tray and settings
        ["Tray.Tip"] = "SweaxMorph — drop, pick, done",
        ["Tray.Open"] = "Open SweaxMorph",
        ["Tray.Settings"] = "Settings",
        ["Tray.Quit"] = "Quit",
        ["Menu.Verb"] = "Convert with SweaxMorph",
        ["Settings.Title"] = "SweaxMorph settings",
        ["S.Shortcut"] = "Shortcut",
        ["S.ShortcutHint"] = "Opens the drop well under your pointer, from anywhere.",
        ["S.Record"] = "Change",
        ["S.Recording"] = "Press the new shortcut… (Esc cancels)",
        ["S.DragTrigger"] = "Tap Space while dragging files in Explorer",
        ["S.DragTriggerHint"] = "The well pops up right under the files you're carrying.",
        ["S.Explorer"] = "Explorer",
        ["S.ContextMenu"] = "\"Convert with SweaxMorph\" in the right-click menu",
        ["S.ContextMenuHint"] = "On Windows 11 it's under \"Show more options\" (Shift + F10). Also added to Send to.",
        ["S.Output"] = "Where results go",
        ["S.OutBeside"] = "Next to the original",
        ["S.OutDesktop"] = "Desktop",
        ["S.OutCustom"] = "A folder I choose",
        ["S.Choose"] = "Choose…",
        ["S.Quality"] = "Quality",
        ["S.QualityHint"] = "Best keeps everything, Small makes files light for sharing.",
        ["S.Engines"] = "Engines",
        ["S.EnginesHint"] = "Most conversions run inside SweaxMorph. These add the rest.",
        ["S.Ready"] = "Ready",
        ["S.NotFound"] = "Not installed",
        ["S.Get"] = "Download",
        ["S.Install"] = "Install page",
        ["S.General"] = "General",
        ["S.Startup"] = "Start with Windows",
        ["S.OpenFolder"] = "Open the folder when a conversion is done",
        ["S.Language"] = "Language",
        ["S.Theme"] = "Theme",
        ["S.ThemeSystem"] = "Follow Windows", ["S.ThemeLight"] = "Light", ["S.ThemeDark"] = "Dark",
        ["S.LangAuto"] = "Follow Windows",
        ["S.Formats"] = "Formats",
        ["S.FormatsHint"] = "{0} formats, {1} conversions available on this PC.",
        ["S.About"] = "About",
        ["S.Version"] = "Version {0}",
    };

    static readonly Dictionary<string, string> Tr = new()
    {
        ["Kind.Image"] = "Görsel", ["Kind.Video"] = "Video", ["Kind.Audio"] = "Ses", ["Kind.Document"] = "Belge", ["Kind.Sheet"] = "Tablo",
        ["Kind.Slides"] = "Sunu", ["Kind.Pdf"] = "PDF", ["Kind.Ebook"] = "E-kitap", ["Kind.Data"] = "Veri", ["Kind.Archive"] = "Arşiv",
        ["Kind.Subtitle"] = "Altyazı", ["Kind.Font"] = "Yazı tipi", ["Kind.Folder"] = "Klasör",

        ["Folder"] = "Klasör",
        ["SmallSuffix"] = "(küçük)",
        ["Stage.Download"] = "{0} hazırlanıyor — yalnızca bir kez",
        ["Stage.Convert"] = "{1} ile {0} yapılıyor",
        ["Stage.Save"] = "Kaydediliyor",

        ["Err.Unknown"] = "SweaxMorph bu dosya türünü tanımıyor.",
        ["Err.NoRoute"] = "{0} dosyasından {1} yapmanın henüz bir yolu yok.",
        ["Err.Download"] = "{0} indirilemedi. Bağlantıyı kontrol edip yeniden dene.",
        ["Err.Empty"] = "Dönüştürme bitti ama bir şey çıkmadı.",
        ["Err.General"] = "Bir şeyler ters gitti: {0}",
        ["Err.NoSubs"] = "Bu videonun içinde altyazı yok.",
        ["Err.NoAudio"] = "Bu dosyada alınacak ses yok.",
        ["Err.Engine"] = "{0} şunu söyledi: {1}",
        ["Err.Timeout"] = "{0} yanıt vermeyi bıraktı, kapatıldı.",
        ["Err.Missing"] = "{0} kurulu değil.",
        ["Err.Password"] = "Bu PDF parolayla kilitli.",
        ["Err.NoText"] = "Bu görselde yazı bulunamadı.",
        ["Err.NoOcr"] = "Windows'ta yazı tanıma dili kurulu değil.",
        ["Err.Unsafe"] = "Bu arşiv kendi klasörünün dışına yazmaya çalışıyor, durduruldu.",
        ["Err.NoPages"] = "Bu çizgi romanın içinde görsel yok.",
        ["Err.NeedsEngine"] = "{1} için {0} gerekiyor.",

        ["Drop.Title"] = "Dosyaları buraya bırak",
        ["Drop.Hint"] = "ya da tıklayıp seç · her yerden açmak için {0}",
        ["Drop.Over"] = "Bırak gelsin!",
        ["Drop.Petal"] = "Morph'a ya da doğrudan bir yaprağa bırak",
        ["Drop.Many"] = "{0} dosya",
        ["Drop.Mixed"] = "{0} dosya · {1} tür",
        ["Pick.Title"] = "{0} neye dönüşsün?",
        ["Pick.More"] = "Daha fazla",
        ["Pick.Search"] = "Format ara",
        ["Pick.Shrink"] = "Küçült",
        ["Pick.ShrinkHint"] = "Aynı format, çok daha küçük",
        ["Pick.Extract"] = "Aç",
        ["Pick.Combine"] = "Tek dosyada birleştir",
        ["Pick.NoTargets"] = "Bunlardan şimdilik bir şey yapılamıyor.",
        ["Pick.Needs"] = "{0} gerekir",
        ["Pick.Download"] = "{0} bir kez iner ({1})",
        ["Quality.Best"] = "En iyi", ["Quality.Balanced"] = "Dengeli", ["Quality.Small"] = "Küçük",
        ["Work.Title"] = "Dönüştürülüyor",
        ["Work.Cancel"] = "Durdur",
        ["Done.One"] = "Hazır!",
        ["Done.Many"] = "{0} dosya hazır!",
        ["Done.Partial"] = "{0} hazır, {1} olmadı",
        ["Done.Failed"] = "Bu olmadı",
        ["Done.Open"] = "Aç",
        ["Done.Show"] = "Klasörde göster",
        ["Done.Copy"] = "Kopyala",
        ["Done.Copied"] = "Kopyalandı",
        ["Done.Again"] = "Yeniden dönüştür",
        ["Done.Drag"] = "Sonucu istediğin yere sürükle",
        ["Done.Saved"] = "Kaydedildi",
        ["Done.Install"] = "{0} edin",

        ["Tray.Tip"] = "SweaxMorph — bırak, seç, bitti",
        ["Tray.Open"] = "SweaxMorph'u aç",
        ["Tray.Settings"] = "Ayarlar",
        ["Tray.Quit"] = "Çıkış",
        ["Menu.Verb"] = "SweaxMorph ile dönüştür",
        ["Settings.Title"] = "SweaxMorph ayarları",
        ["S.Shortcut"] = "Kısayol",
        ["S.ShortcutHint"] = "Bırakma kuyusunu her yerden, imlecinin altında açar.",
        ["S.Record"] = "Değiştir",
        ["S.Recording"] = "Yeni kısayola bas… (Esc vazgeçer)",
        ["S.DragTrigger"] = "Gezginde dosya sürüklerken Boşluk'a dokun",
        ["S.DragTriggerHint"] = "Kuyu, taşıdığın dosyaların hemen altında belirir.",
        ["S.Explorer"] = "Dosya Gezgini",
        ["S.ContextMenu"] = "Sağ tık menüsünde \"SweaxMorph ile dönüştür\"",
        ["S.ContextMenuHint"] = "Windows 11'de \"Diğer seçenekleri göster\" altında (Shift + F10). Gönder menüsüne de eklenir.",
        ["S.Output"] = "Sonuçlar nereye",
        ["S.OutBeside"] = "Asıl dosyanın yanına",
        ["S.OutDesktop"] = "Masaüstü",
        ["S.OutCustom"] = "Seçtiğim bir klasör",
        ["S.Choose"] = "Seç…",
        ["S.Quality"] = "Kalite",
        ["S.QualityHint"] = "En iyi her şeyi korur, Küçük paylaşmak için dosyayı hafifletir.",
        ["S.Engines"] = "Motorlar",
        ["S.EnginesHint"] = "Dönüşümlerin çoğu SweaxMorph'un içinde yapılır. Bunlar kalanını ekler.",
        ["S.Ready"] = "Hazır",
        ["S.NotFound"] = "Kurulu değil",
        ["S.Get"] = "İndir",
        ["S.Install"] = "Kurulum sayfası",
        ["S.General"] = "Genel",
        ["S.Startup"] = "Windows ile başlat",
        ["S.OpenFolder"] = "Dönüştürme bitince klasörü aç",
        ["S.Language"] = "Dil",
        ["S.Theme"] = "Tema",
        ["S.ThemeSystem"] = "Windows'a uy", ["S.ThemeLight"] = "Açık", ["S.ThemeDark"] = "Koyu",
        ["S.LangAuto"] = "Windows'a uy",
        ["S.Formats"] = "Formatlar",
        ["S.FormatsHint"] = "Bu bilgisayarda {0} format, {1} dönüşüm hazır.",
        ["S.About"] = "Hakkında",
        ["S.Version"] = "Sürüm {0}",
    };
}
