using System.IO;

namespace SweaxMorph.Core;

public sealed record Output(string Source, string Path, long Before, long After, bool IsFolder);
public sealed record Failure(string Source, string Message);

/// <summary>
/// Turns the dropped files into the chosen format: plans each route, downloads a missing engine once,
/// runs the hops in a scratch folder and moves the results next to the originals (never overwriting).
/// </summary>
public sealed class Job
{
    public required IReadOnlyList<string> Files { get; init; }
    public required string Target { get; init; }
    public required Options Options { get; init; }
    public required Settings Settings { get; init; }

    public List<Output> Outputs { get; } = [];
    public List<Failure> Failures { get; } = [];

    /// <summary>Overall progress 0..1 and what is happening, from any thread.</summary>
    public event Action<double, string>? Progress;

    double _base, _span = 1;

    void Report(double inner, string stage) => Progress?.Invoke(Math.Clamp(_base + inner * _span, 0, 1), stage);

    public async Task Run(CancellationToken ct)
    {
        var formats = Files.Select(f => Formats.Of(f)?.Id ?? "?").ToList();
        if (Options.Combine && Files.Count > 1 && Planner.Combiner(formats, Target) is { } combiner)
        {
            await RunOne(Files.ToArray(), Formats.Get(formats[0]), [combiner], Formats.BaseName(Files[0]), ct);
            return;
        }

        for (int i = 0; i < Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            _base = (double)i / Files.Count;
            _span = 1.0 / Files.Count;
            string file = Files[i];
            var from = Formats.Of(file);
            if (from == null) { Failures.Add(new(file, Loc.T("Err.Unknown"))); continue; }
            IReadOnlyList<Hop>? hops = Target == from.Id
                ? Planner.Shrink(from.Id) is { } s ? [s] : null
                : Planner.Targets(from.Id).TryGetValue(Target, out var route) ? route.Hops : null;
            if (hops == null) { Failures.Add(new(file, Loc.T("Err.NoRoute", from.Label, Formats.Get(Target).Label))); continue; }
            await RunOne([file], from, hops, Formats.BaseName(file), ct);
        }
    }

    async Task RunOne(string[] inputs, Format from, IReadOnlyList<Hop> hops, string name, CancellationToken ct)
    {
        string source = inputs[0];
        var scratch = new List<string>();
        try
        {
            // engines first: a missing FFmpeg or Pandoc is fetched once, then kept
            foreach (var engine in hops.Select(h => h.Converter.Engine).Where(e => e is { Ready: false }).Distinct())
            {
                var p = new Progress<double>(v => Report(v * 0.3, Loc.T("Stage.Download", engine!.Name)));
                if (!await Engines.Install(engine!, p, ct)) throw new ConvertException(Loc.T("Err.Download", engine!.Name));
            }

            string[] current = inputs;
            var fmt = from;
            for (int h = 0; h < hops.Count; h++)
            {
                ct.ThrowIfCancellationRequested();
                var hop = hops[h];
                var to = Formats.Get(hop.Edge.To);
                string dir = Paths.Scratch();
                scratch.Add(dir);
                int index = h;
                var step = new Step
                {
                    Inputs = current,
                    From = fmt,
                    To = to,
                    OutDir = dir,
                    Name = SafeName(name),
                    Options = Options,
                    Cancel = ct,
                    Progress = new Progress<double>(v => Report((index + Math.Clamp(v, 0, 1)) / hops.Count, Loc.T("Stage.Convert", to.Label == "" ? Loc.T("Folder") : to.Label, hop.Converter.Name))),
                };
                Report((double)h / hops.Count, Loc.T("Stage.Convert", to.Label == "" ? Loc.T("Folder") : to.Label, hop.Converter.Name));
                var made = await hop.Converter.Run(step);
                if (made.Length == 0) throw new ConvertException(Loc.T("Err.Empty"));
                current = made;
                fmt = to;
            }

            Report(1, Loc.T("Stage.Save"));
            long before = inputs.Sum(SizeOf);
            string dest = Destination(source);
            bool same = hops.Count == 1 && hops[0].Edge.From == hops[0].Edge.To;
            string suffix = same ? " " + Loc.T("SmallSuffix") : "";
            if (current.Length == 1)
            {
                string made = current[0];
                bool isDir = Directory.Exists(made);
                string target = Unique(Path.Combine(dest, name + suffix + (isDir ? "" : Ext(made, fmt))));
                Move(made, target);
                Outputs.Add(new(source, target, before, SizeOf(target), isDir));
            }
            else
            {
                string folder = Unique(Path.Combine(dest, $"{name} ({fmt.Label})"));
                Directory.CreateDirectory(folder);
                foreach (var f in current) Move(f, Path.Combine(folder, Path.GetFileName(f)));
                Outputs.Add(new(source, folder, before, SizeOf(folder), true));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ConvertException ex) { Failures.Add(new(source, ex.Message)); Paths.Log($"fail {source}: {ex.Message}"); }
        catch (Exception ex)
        {
            Paths.Log($"error {source} → {Target}: {ex}");
            Failures.Add(new(source, Loc.T("Err.General", Short(ex.Message))));
        }
        finally
        {
            foreach (var d in scratch) try { Directory.Delete(d, true); } catch { }
        }
    }

    /// <summary>The extension a result keeps: ".tar.gz" stays whole; otherwise the format's own.</summary>
    static string Ext(string made, Format fmt)
    {
        string n = Path.GetFileName(made).ToLowerInvariant();
        foreach (var e in fmt.Extensions) if (n.EndsWith(e)) return e;
        return fmt.Ext;
    }

    string Destination(string source)
    {
        string? dir = Settings.Output switch
        {
            OutputPlace.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            OutputPlace.Custom when Directory.Exists(Settings.OutputFolder) => Settings.OutputFolder,
            _ => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source)),
        };
        if (dir == null || !Writable(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return dir;
    }

    static bool Writable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".sweaxmorph-" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>"photo.png", then "photo (2).png", "photo (3).png" … — nothing is ever overwritten.</summary>
    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string file = Path.GetFileName(path);
        string ext = "";
        foreach (var multi in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (file.EndsWith(multi, StringComparison.OrdinalIgnoreCase)) { ext = file[^multi.Length..]; break; }
        if (ext == "" && !Directory.Exists(path)) ext = Path.GetExtension(file);
        string stem = file[..^ext.Length];
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    static void Move(string from, string to)
    {
        if (Directory.Exists(from))
        {
            try { Directory.Move(from, to); }
            catch (IOException) { CopyDir(from, to); }
        }
        else File.Move(from, to);
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path)) return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch { }
        return 0;
    }

    /// <summary>Engines choke on some characters; the final name is restored when the result is moved.</summary>
    static string SafeName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.' ? c : '_').ToArray();
        string s = new string(chars).Trim();
        return s.Length == 0 ? "file" : s.Length > 80 ? s[..80] : s;
    }

    static string Short(string s) => s.Length > 160 ? s[..160] + "…" : s;
}
