using System.IO;
using SweaxMorph.Core;

namespace SweaxMorph;

/// <summary>
/// Entry point. Most launches are not "start SweaxMorph" but "tell the running SweaxMorph something": the right-click
/// menu (one launch per file), --toggle from a macro key or a launcher, --quit from an update. Those are handed over
/// here, before WPF starts at all, which makes them several times faster.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Launch.Has(args, "--keys-helper")) { KeysHelper.Run(args); return 0; }
        if (Launch.Arg(args, "--task-xml") is { } xml)
        {
            File.WriteAllText(xml, Elevation.TaskXml(Launch.Has(args, "admin"), Launch.Arg(args, "--instance")), System.Text.Encoding.Unicode);
            return 0;
        }
        if (Launch.TryHandOver(args)) return 0;
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

/// <summary>What a launch asks for, read from its command line.</summary>
public static class Launch
{
    public const string InstanceName = "SweaxMorph.SingleInstance.3b9e";

    /// <summary>Modes that run on their own and never talk to a running copy.</summary>
    static readonly string[] Standalone = ["--selftest", "--formats", "--icon", "--shots", "--palettes", "--bench", "--uia-listen", "--keys-helper", "--task-xml"];
    /// <summary>Options followed by a value (which is never a file to convert).</summary>
    static readonly string[] Valued = ["--instance", "--data", "--bench", "--uia-listen", "--uia-pid", "--selftest", "--shots", "--icon", "--formats", "--palettes", "--only", "--lang", "--theme", "--palette", "--task-xml"];

    public static bool Has(string[] args, string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));

    public static string? Arg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static List<string> Files(string[] args)
    {
        var skip = new HashSet<int>(args.Select((a, i) => (a, i)).Where(x => Valued.Contains(x.a, StringComparer.OrdinalIgnoreCase)).Select(x => x.i + 1));
        return args.Where((a, i) => !skip.Contains(i) && !a.StartsWith("--") && (File.Exists(a) || Directory.Exists(a))).Select(Path.GetFullPath).ToList();
    }

    /// <summary>The request for a running copy; null when there is nothing to ask (a second start-up in the background).</summary>
    public static string? Command(string[] args, List<string> files) =>
        Has(args, "--quit") ? "quit" : Has(args, "--settings") ? "settings" : Has(args, "--toggle") ? "toggle"
        : files.Count > 0 ? "convert" : Has(args, "--background") ? null : "open";

    public static string Suffix(string[] args) => Arg(args, "--instance") is { } i ? "." + i : "";

    /// <summary>When SweaxMorph already runs: passes the request on and returns true. The new process then just ends.</summary>
    public static bool TryHandOver(string[] args)
    {
        if (Standalone.Any(m => Has(args, m))) return false;
        string suffix = Suffix(args);
        if (!Mutex.TryOpenExisting(InstanceName + suffix, out var running)) return false;
        running.Dispose();
        var files = Files(args);
        if (Command(args, files) is not { } command) return true;
        Ipc.Suffix = suffix;
        return Ipc.Send(command, files);
    }
}
