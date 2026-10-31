using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SweaxMorph.UI;

/// <summary>
/// Two moods for the same candy world: a milky light and a deep plum night. Everything else takes its
/// colour from the files themselves (see <see cref="Core.Formats.Hue"/>).
/// </summary>
public static class Theme
{
    public static readonly FontFamily Rounded = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Nunito");
    public static bool Dark { get; private set; }

    public static void Apply(string mode)
    {
        Dark = mode switch { "dark" => true, "light" => false, _ => SystemDark() };
        var r = Application.Current.Resources;
        void C(string key, string light, string dark)
        {
            var c = Blob.Hex(Dark ? dark : light);
            r[key + "Color"] = c;
            var b = new SolidColorBrush(c);
            b.Freeze();
            r[key] = b;
        }
        C("Haze", "#FFFBFD", "#1A1522");
        C("Ink", "#2B2140", "#F6F0FF");
        C("Ink2", "#6F6585", "#B8ADCC");
        C("Ink3", "#A49BB6", "#7D7392");
        C("Surface", "#FFFFFF", "#272033");
        C("Surface2", "#F6F1FB", "#211B2B");
        C("Line", "#EAE3F2", "#3A3147");
        C("Accent", "#8B7CFF", "#A498FF");
        C("Accent2", "#FF6B9A", "#FF7FA9");
        C("Good", "#2BB673", "#4FD394");
        C("Bad", "#E5484D", "#FF6B70");
        var haze = (System.Windows.Media.Color)r["HazeColor"];
        r["HazeClearColor"] = System.Windows.Media.Color.FromArgb(0, haze.R, haze.G, haze.B); // fades out without going grey
        r["Rounded"] = Rounded;
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
}
