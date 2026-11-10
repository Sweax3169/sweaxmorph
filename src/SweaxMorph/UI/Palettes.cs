using System.Windows.Media;

namespace SweaxMorph.UI;

/// <summary>The colours of the Well, the settings and the text in one mode (light or dark).</summary>
public sealed record Mode(
    string Haze, string Ink, string Ink2, string Ink3, string Surface, string Surface2, string Line,
    string Accent, string Accent2, string More, string Good = "", string Bad = "");

/// <summary>
/// One flavour of Morph's gel world: the gel Morph is made of when it holds nothing, the clear gel it turns into while
/// it works, the ink its face is printed in, the two soft accents of the round buttons, and a light and a dark mode.
/// Files still paint Morph in their own colour (see <see cref="Core.Formats.Hue"/>): a palette changes the world
/// around them, never what a format looks like.
/// </summary>
public sealed record Palette(
    string Key, string NameEn, string NameTr,
    string GelTop, string GelBottom, string GlassTop, string GlassBottom, string Face, string Bubble, string Drop,
    Mode Light, Mode Dark)
{
    public string Name => Core.Loc.Turkish ? NameTr : NameEn;
    public Mode For(bool dark) => dark ? Dark : Light;
}

public static class Palettes
{
    public static readonly IReadOnlyList<Palette> All =
    [
        // the original 1.0 look, colour for colour
        new("candy", "Candy", "Şeker",
            "#CDB8FF", "#FF8FC1", "#F4F0FB", "#D6CCEC", "#2B2140", "#B9AEFF", "#C8BEFF",
            new("#FFFBFD", "#2B2140", "#6F6585", "#A49BB6", "#FFFFFF", "#F6F1FB", "#EAE3F2", "#8B7CFF", "#FF6B9A", "#B3A8C9"),
            new("#1A1522", "#F6F0FF", "#B8ADCC", "#7D7392", "#272033", "#211B2B", "#3A3147", "#A498FF", "#FF7FA9", "#6E6385")),
        // fresh mint gel, a sea-glass night
        new("mint", "Mint", "Nane",
            "#B9F6E0", "#4FD1B0", "#F0FBF7", "#C7E6DC", "#133A33", "#8EE8CF", "#A9EEDB",
            new("#F7FFFC", "#133A33", "#4E726A", "#8DAAA3", "#FFFFFF", "#EEF8F4", "#DDEFE8", "#13A88A", "#3FB7E8", "#A3C9BF"),
            new("#0E1B19", "#E8FFF8", "#A4C9BF", "#6A8C84", "#16302B", "#122622", "#24423B", "#4FE3C1", "#6ACBF5", "#4F766D")),
        // a peach jelly on a cocoa night
        new("peach", "Peach", "Şeftali",
            "#FFD6B8", "#FF8A7A", "#FFF5EF", "#EED6CC", "#3A1F1A", "#FFB59C", "#FFC8B4",
            new("#FFFAF7", "#3A1F1A", "#7E5E56", "#B79D95", "#FFFFFF", "#FBF1EC", "#F1E2DA", "#F2704F", "#F6A33C", "#D9B6AA"),
            new("#20140F", "#FFF2EC", "#D2B3A8", "#93766C", "#33231D", "#2A1C17", "#4A342C", "#FF8F70", "#FFB65C", "#7E5F55")),
        // lagoon blue, a deep-water night
        new("lagoon", "Lagoon", "Lagün",
            "#BCE4FF", "#6D8CFF", "#F0F6FF", "#CCD8EE", "#15213F", "#9DB6FF", "#B3C7FF",
            new("#F8FBFF", "#15213F", "#56638A", "#9AA5C2", "#FFFFFF", "#EFF4FC", "#DFE7F4", "#3F72F5", "#18B5C9", "#A8B6D6"),
            new("#0D1424", "#EEF4FF", "#A9B6D4", "#6C7898", "#18233C", "#131C31", "#283655", "#6F9CFF", "#3BD3E3", "#4E5D82")),
        // berry jam: violet gel, a blackcurrant night
        new("berry", "Berry", "Böğürtlen",
            "#EBC3FF", "#B44BD9", "#FAF1FF", "#DFC8EA", "#2D1238", "#D191F0", "#DDABF4",
            new("#FFF9FF", "#2D1238", "#6E517A", "#AC96B5", "#FFFFFF", "#F8EEFB", "#EEDDF3", "#A93BD6", "#F0468F", "#C9A9D4"),
            new("#1B0E22", "#FBEFFF", "#CDB0D8", "#8C7097", "#2E1A38", "#24142D", "#432A4F", "#CF7BFF", "#FF6FAE", "#6E5079")),
        // lemonade: lemon-lime gel, a garden at night
        new("lemonade", "Lemonade", "Limonata",
            "#F4F7A8", "#8FD95B", "#FBFDEE", "#DCE8C4", "#22300F", "#BCE57E", "#CDEC9C",
            new("#FCFFF6", "#22300F", "#5F6E4A", "#A0AC8D", "#FFFFFF", "#F3F8EA", "#E3ECD3", "#4E9C18", "#E8902C", "#B4C79A"),
            new("#131A0C", "#F5FFE8", "#BACBA3", "#7D8C68", "#212C16", "#1A2311", "#33421F", "#9BE05C", "#FFB45C", "#5E6E46")),
        // sunset: peach melting into violet
        new("sunset", "Sunset", "Gün batımı",
            "#FFC9A3", "#C66BD8", "#FFF4EE", "#E6CEE0", "#331A2E", "#E99BC2", "#F0B4D0",
            new("#FFF9F8", "#331A2E", "#775A70", "#B39CAD", "#FFFFFF", "#FAF0F2", "#F0E0E6", "#DE5591", "#F29A3E", "#D2AFC4"),
            new("#1D1119", "#FFF0F6", "#D3B2C6", "#927189", "#30202B", "#271A23", "#46303F", "#FF7AB4", "#FFAE5C", "#75566B")),
        // cloud: a quiet pearl gel for calm desks
        new("cloud", "Cloud", "Bulut",
            "#EEF1F8", "#A9B4CF", "#FAFBFD", "#D9DEE9", "#232A3B", "#B9C3DB", "#CAD2E5",
            new("#FBFCFE", "#232A3B", "#646C80", "#A2A9B8", "#FFFFFF", "#F2F4F8", "#E3E7EE", "#5B6C93", "#8C7BB8", "#B5BCCB"),
            new("#14161B", "#EEF1F7", "#AEB4C2", "#727889", "#22262F", "#1B1E25", "#343945", "#A3B4DA", "#B9A8E0", "#5A6070")),
    ];

    public static Palette? Find(string? key) => All.FirstOrDefault(p => p.Key == key);
    public static Palette Get(string? key) => Find(key) ?? All[0];

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>A desk for recordings in this palette: soft fields of the gel colours over the haze.</summary>
    public static (Color a, Color b) Desk(Palette p, bool dark)
    {
        var haze = C(p.For(dark).Haze);
        return dark ? (Blob.Mix(haze, C(p.GelTop), 0.08), Blob.Mix(haze, C(p.GelBottom), 0.12))
                    : (Blob.Mix(haze, C(p.GelTop), 0.32), Blob.Mix(haze, C(p.GelBottom), 0.22));
    }
}
