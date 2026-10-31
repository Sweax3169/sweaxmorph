using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SweaxMorph.Core;

/// <summary>
/// A program SweaxMorph can drive. Most of the work is done in-process; the big media and document
/// engines are found on the PC, or downloaded once into %LOCALAPPDATA%\SweaxMorph\engines when first needed.
/// </summary>
public sealed class Engine(string id, string name, string url, bool installable, long approxBytes = 0)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    /// <summary>Home page, shown when it has to be installed by hand.</summary>
    public string Url { get; } = url;
    /// <summary>SweaxMorph can fetch it by itself (a portable zip).</summary>
    public bool Installable { get; } = installable;
    public long ApproxBytes { get; } = approxBytes;
    public string? Path { get; internal set; }
    public bool Ready => Path != null;
    public override string ToString() => Name;
}

public static class Engines
{
    public static readonly Engine FFmpeg = new("ffmpeg", "FFmpeg", "https://ffmpeg.org", true, 100_000_000);
    public static readonly Engine Pandoc = new("pandoc", "Pandoc", "https://pandoc.org", true, 40_000_000);
    public static readonly Engine Word = new("word", "Microsoft Word", "https://www.microsoft.com/microsoft-365/word", false);
    public static readonly Engine Excel = new("excel", "Microsoft Excel", "https://www.microsoft.com/microsoft-365/excel", false);
    public static readonly Engine PowerPoint = new("powerpoint", "Microsoft PowerPoint", "https://www.microsoft.com/microsoft-365/powerpoint", false);
    public static readonly Engine LibreOffice = new("libreoffice", "LibreOffice", "https://www.libreoffice.org/download/", false);
    public static readonly Engine Calibre = new("calibre", "Calibre", "https://calibre-ebook.com/download_windows", false);
    public static readonly Engine Browser = new("edge", "Microsoft Edge", "https://www.microsoft.com/edge", false);
    public static readonly Engine SevenZip = new("7zip", "7-Zip", "https://www.7-zip.org", true, 600_000);
    public static readonly Engine SystemTar = new("tar", "Windows tar", "https://learn.microsoft.com/windows/", false);

    public static IReadOnlyList<Engine> All => [FFmpeg, Pandoc, Word, Excel, PowerPoint, LibreOffice, Calibre, Browser, SevenZip, SystemTar];

    public static event Action? Changed;

    static string Dir(string id) => System.IO.Path.Combine(Paths.Engines, id);

    public static void Detect()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        FFmpeg.Path = First(System.IO.Path.Combine(Dir("ffmpeg"), "ffmpeg.exe"), OnPath("ffmpeg.exe"), @"C:\ffmpeg\bin\ffmpeg.exe",
            System.IO.Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe"), System.IO.Path.Combine(pf, @"ffmpeg\bin\ffmpeg.exe"));
        Pandoc.Path = First(System.IO.Path.Combine(Dir("pandoc"), "pandoc.exe"), OnPath("pandoc.exe"), System.IO.Path.Combine(local, @"Pandoc\pandoc.exe"), System.IO.Path.Combine(pf, @"Pandoc\pandoc.exe"));
        Word.Path = Com("Word.Application");
        Excel.Path = Com("Excel.Application");
        PowerPoint.Path = Com("PowerPoint.Application");
        string? loDir = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\LibreOffice\UNO\InstallPath", "", null) as string;
        LibreOffice.Path = First(loDir != null ? System.IO.Path.Combine(loDir, "soffice.com") : null, System.IO.Path.Combine(pf, @"LibreOffice\program\soffice.com"),
            System.IO.Path.Combine(pf86, @"LibreOffice\program\soffice.com"), System.IO.Path.Combine(pf, @"LibreOffice\program\soffice.exe"));
        Calibre.Path = First(System.IO.Path.Combine(pf, @"Calibre2\ebook-convert.exe"), System.IO.Path.Combine(pf86, @"Calibre2\ebook-convert.exe"), OnPath("ebook-convert.exe"));
        Browser.Path = First(System.IO.Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), System.IO.Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), System.IO.Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
        SevenZip.Path = First(System.IO.Path.Combine(pf, @"7-Zip\7z.exe"), System.IO.Path.Combine(pf86, @"7-Zip\7z.exe"), System.IO.Path.Combine(Dir("7zip"), "7zr.exe"), OnPath("7z.exe"));
        SystemTar.Path = First(System.IO.Path.Combine(Environment.SystemDirectory, "tar.exe"));
        Changed?.Invoke();
    }

    public static string? FFprobe => FFmpeg.Path is { } f && File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f)!, "ffprobe.exe"))
        ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f)!, "ffprobe.exe") : null;

    /// <summary>7-Zip proper (not the 7z-only 7zr), which also opens ISO, CAB and the rest.</summary>
    public static bool FullSevenZip => SevenZip.Path?.EndsWith("7z.exe", StringComparison.OrdinalIgnoreCase) == true;

    static string? First(params string?[] candidates) => candidates.FirstOrDefault(c => c != null && File.Exists(c));

    static string? OnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string p = System.IO.Path.Combine(dir.Trim().Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    static string? Com(string progId)
    {
        try
        {
            using var k = Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID");
            return k?.GetValue("") is string clsid ? progId + " " + clsid : null;
        }
        catch { return null; }
    }

    // ── downloads ───────────────────────────────────────────────────────────

    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("SweaxMorph/1.0 (+https://github.com/Sweax3169/sweaxmorph)");
        return h;
    }

    static readonly SemaphoreSlim InstallGate = new(1, 1);

    /// <summary>Downloads a portable engine. Progress runs 0..1; returns false (and logs why) on failure.</summary>
    public static async Task<bool> Install(Engine e, IProgress<double>? progress, CancellationToken ct)
    {
        await InstallGate.WaitAsync(ct);
        try
        {
            if (e.Ready) return true;
            string dir = Dir(e.Id);
            Directory.CreateDirectory(dir);
            if (e == SevenZip)
            {
                await Download("https://www.7-zip.org/a/7zr.exe", System.IO.Path.Combine(dir, "7zr.exe"), progress, ct);
            }
            else
            {
                string url = e == FFmpeg
                    ? await GitHubAsset("GyanD/codexffmpeg", n => n.EndsWith("essentials_build.zip"), ct) ?? "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
                    : await GitHubAsset("jgm/pandoc", n => n.EndsWith("windows-x86_64.zip"), ct) ?? throw new IOException("no pandoc release found");
                string zip = System.IO.Path.Combine(Paths.Temp, e.Id + ".zip");
                await Download(url, zip, progress, ct);
                using (var archive = ZipFile.OpenRead(zip))
                {
                    string[] wanted = e == FFmpeg ? ["ffmpeg.exe", "ffprobe.exe"] : ["pandoc.exe"];
                    foreach (var entry in archive.Entries)
                        if (wanted.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                            entry.ExtractToFile(System.IO.Path.Combine(dir, entry.Name), true);
                }
                File.Delete(zip);
            }
            Detect();
            Paths.Log($"installed {e.Name} → {e.Path}");
            return e.Ready;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Paths.Log($"install {e.Name}: {ex.Message}");
            return false;
        }
        finally { InstallGate.Release(); }
    }

    static async Task<string?> GitHubAsset(string repo, Func<string, bool> pick, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", ct));
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString() is { } n && pick(n))
                    return a.GetProperty("browser_download_url").GetString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Paths.Log($"github {repo}: {ex.Message}"); }
        return null;
    }

    static async Task Download(string url, string file, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using (var dst = File.Create(file + ".part"))
        {
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        File.Move(file + ".part", file, true);
    }
}

/// <summary>Runs a console engine without a window; cancelling kills it with its children.</summary>
public static class Proc
{
    public sealed record Result(int Code, string Out, string Err);

    public static async Task<Result> Run(string exe, IEnumerable<string> args, CancellationToken ct, Action<string>? onErr = null, Action<string>? onOut = null,
        string? cwd = null, TimeSpan? timeout = null, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = cwd ?? Paths.Temp,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env != null) foreach (var (k, v) in env) psi.Environment[k] = v;

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data == null) return; lock (stdout) { if (stdout.Length < 2_000_000) stdout.AppendLine(e.Data); } onOut?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data == null) return; lock (stderr) { if (stderr.Length > 200_000) stderr.Remove(0, 100_000); stderr.AppendLine(e.Data); } onErr?.Invoke(e.Data); };
        p.Start();
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) limit.CancelAfter(t);
        try { await p.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"{System.IO.Path.GetFileName(exe)} took too long");
        }
        p.WaitForExit(); // flush the async readers
        return new Result(p.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
