using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SweaxMorph.UI;

/// <summary>
/// The candy world in one of its <see cref="Palettes"/>, light or dark. Everything else takes its colour from the
/// files themselves (see <see cref="Core.Formats.Hue"/>). Applying a palette swaps the brushes every window uses by
/// key, so the change shows at once, without a restart.
/// </summary>
public static class Theme
{
    public static readonly FontFamily Rounded = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Nunito");
    public static bool Dark { get; private set; }
    public static Palette Palette { get; private set; } = Palettes.All[0];

    /// <summary>Raised after the colours changed (theme or palette), for what draws by hand: Morph, the petals.</summary>
    public static event Action? Changed;

    public static void Apply(string mode, string? palette = null)
    {
        Dark = mode switch { "dark" => true, "light" => false, _ => SystemDark() };
        if (palette != null) Palette = Palettes.Get(palette);
        var m = Palette.For(Dark);
        var r = Application.Current.Resources;
        void C(string key, string hex)
        {
            var c = Blob.Hex(hex);
            r[key + "Color"] = c;
            var b = new SolidColorBrush(c);
            b.Freeze();
            r[key] = b;
        }
        C("Haze", m.Haze);
        C("Ink", m.Ink);
        C("Ink2", m.Ink2);
        C("Ink3", m.Ink3);
        C("Surface", m.Surface);
        C("Surface2", m.Surface2);
        C("Line", m.Line);
        C("Accent", m.Accent);
        C("Accent2", m.Accent2);
        C("Good", m.Good.Length > 0 ? m.Good : Dark ? "#4FD394" : "#2BB673");
        C("Bad", m.Bad.Length > 0 ? m.Bad : Dark ? "#FF6B70" : "#E5484D");
        // the soft first colour of the round gel buttons and toggles
        C("SoftBubble", Palette.Bubble);
        C("SoftDrop", Palette.Drop);
        // candy light in the corners of the settings window
        var top = Blob.Hex(Palette.GelTop);
        var bottom = Blob.Hex(Palette.GelBottom);
        r["GlowA"] = System.Windows.Media.Color.FromArgb(0x66, top.R, top.G, top.B);
        r["GlowAClear"] = System.Windows.Media.Color.FromArgb(0, top.R, top.G, top.B);
        r["GlowB"] = System.Windows.Media.Color.FromArgb(0x55, bottom.R, bottom.G, bottom.B);
        r["GlowBClear"] = System.Windows.Media.Color.FromArgb(0, bottom.R, bottom.G, bottom.B);
        var haze = (System.Windows.Media.Color)r["HazeColor"];
        r["HazeClearColor"] = System.Windows.Media.Color.FromArgb(0, haze.R, haze.G, haze.B); // fades out without going grey
        r["Rounded"] = Rounded;
        Changed?.Invoke();
    }

    public static bool SystemDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static Color Color(string key) => (Color)Application.Current.Resources[key + "Color"];
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>The colour of the "more formats" petal.</summary>
    public static string MoreHue => Palette.For(Dark).More;
}
