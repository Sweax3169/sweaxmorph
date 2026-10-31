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

    [JsonIgnore] string _file = "";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        string f = Path.Combine(Paths.Data, "settings.json");
        Settings s;
        try { s = File.Exists(f) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(f), Json) ?? new() : new(); }
        catch { s = new(); }
        s._file = f;
        return s;
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
