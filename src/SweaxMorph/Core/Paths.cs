using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SweaxMorph.Core;

/// <summary>Where SweaxMorph keeps its settings, engines and log: %LOCALAPPDATA%\SweaxMorph.</summary>
public static class Paths
{
    public static string Data { get; private set; } = "";
    public static string Engines => Path.Combine(Data, "engines");
    public static string Temp => Path.Combine(Path.GetTempPath(), "SweaxMorph");
    static readonly object LogGate = new();

    public static void Init(string? dir = null)
    {
        Data = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SweaxMorph");
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Engines);
        Directory.CreateDirectory(Temp);
    }

    public static void Log(string line)
    {
        try
        {
            lock (LogGate)
            {
                string f = Path.Combine(Data, "sweaxmorph.log");
                if (File.Exists(f) && new FileInfo(f).Length > 512_000) File.Delete(f);
                File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}");
            }
        }
        catch { }
    }

    /// <summary>A fresh scratch folder for one conversion; deleted when the job ends.</summary>
    public static string Scratch()
    {
        string d = Path.Combine(Temp, Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(d);
        return d;
    }
}

public enum OutputPlace { BesideOriginal, Desktop, Custom }
public enum Quality { Best, Balanced, Small }

public sealed class Settings
{
    public string Language { get; set; } = "auto";
    public string Hotkey { get; set; } = "Win+Alt+F";
    public bool HotkeyEnabled { get; set; } = true;
    /// <summary>Tap this key while dragging files in Explorer to open the wheel under the pointer.</summary>
    public bool DragTrigger { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool ContextMenu { get; set; } = true;
    public string Theme { get; set; } = "system";
    public OutputPlace Output { get; set; } = OutputPlace.BesideOriginal;
    public string? OutputFolder { get; set; }
    public Quality Quality { get; set; } = Quality.Balanced;
    public bool OpenFolderWhenDone { get; set; }
    public Dictionary<string, int> Used { get; set; } = [];
    public bool FirstRunDone { get; set; }

    // ── 1.1 ──
    /// <summary>Where the Well opens: "cursor" (around the pointer), "center" (the middle of the pointer's monitor), "remember" (where you last dragged it).</summary>
    public string Placement { get; set; } = "cursor";
    /// <summary>Morph's centre in screen pixels, saved whenever you drag the cloud to move it.</summary>
    public int? WellX { get; set; }
    public int? WellY { get; set; }
    /// <summary>Colour palette (see <see cref="UI.Palettes"/>); "candy" is the original look.</summary>
    public string Palette { get; set; } = "candy";
    /// <summary>A helper with administrator rights catches the shortcut over administrator windows too (opt-in, Windows asks once).</summary>
    public bool AdminKeys { get; set; }
    /// <summary>Settings layout version: 1 = 1.0.x, 2 = 1.1.</summary>
    public int Schema { get; set; } = 2;

    [JsonIgnore] string _file = "";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        string f = Path.Combine(Paths.Data, "settings.json");
        Settings s;
        bool existed = File.Exists(f);
        string? old = null;
        try
        {
            old = existed ? File.ReadAllText(f) : null;
            s = old != null ? JsonSerializer.Deserialize<Settings>(old, Json) ?? new() : new();
        }
        catch { s = new(); }
        s._file = f;
        s.Normalize();
        // 1.0.x files have no Schema: they read with the 1.1 defaults; the old file is kept beside the new one, once
        if (existed && old != null && !old.Contains("\"Schema\""))
        {
            try { File.WriteAllText(f + ".1.0.bak", old); } catch { }
            s.Schema = 2;
            s.Save();
        }
        return s;
    }

    /// <summary>Unknown values (a hand-edited file, a newer version) fall back to the defaults.</summary>
    void Normalize()
    {
        if (Placement is not ("cursor" or "center" or "remember")) Placement = "cursor";
        if (UI.Palettes.Find(Palette) == null) Palette = "candy";
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        Used ??= [];
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(this, Json));
            File.Move(_file + ".tmp", _file, true);
        }
        catch { }
    }

    /// <summary>Remembers which targets get picked, so the wheel puts your favourites first.</summary>
    public void Count(string target)
    {
        Used[target] = Used.TryGetValue(target, out int n) ? n + 1 : 1;
        Save();
    }
}
