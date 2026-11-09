using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SweaxMorph.Core;
using SweaxMorph.Native;
using SweaxMorph.UI;

namespace SweaxMorph.Dev;

/// <summary>
/// Times the Well the way it is really used, in a copy of SweaxMorph that shares nothing with the installed one:
/// its own mutex and pipe (--instance bench), its own data folder, no keyboard hook, no tray icon, no registry.
/// The Well is cloaked: Windows composes nothing of it and it never takes the mouse or focus, yet WPF draws every
/// frame exactly as on screen (a window moved off-screen is not drawn at all, which hides the real cost).
///   SweaxMorph.exe --bench FILE --instance bench [--data DIR] [--quick]
/// Measures: request → visible → first frames; frame pacing while opening, dragging, converting and idle; CPU per
/// thread; dispatcher stalls; layout passes; GC pauses; idle CPU and memory; requests that were lost (rapid
/// toggles, --toggle from other processes, the shortcut while Morph waits on another monitor); placement and DPI
/// on every monitor.
/// </summary>
public static class Bench
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double Now => Clock.Elapsed.TotalMilliseconds;
    static readonly List<double> Frames = [];
    static readonly List<(double t, bool visible)> Visibility = [];
    static double _stallMax, _stallSum;
    static int _stallCount, _layouts;
    static readonly Dictionary<DispatcherOperation, long> Started = [];
    static string _stallWhat = "";
    static double _refMs = 1000 / 60.0;
    static Process? _listener;

    public static async void Run(App app, string[] args)
    {
        string file = Arg(args, "--bench") ?? Path.Combine(Path.GetTempPath(), "sweaxmorph-bench.txt");
        bool quick = args.Contains("--quick");
        var r = new StringBuilder();
        try
        {
            await Body(app, args, r, quick);
        }
        catch (Exception ex) { r.AppendLine("BENCH FAILED: " + ex); }
        File.WriteAllText(file, r.ToString());
        try { _listener?.Kill(); } catch { } // the listener this bench started, nothing else
        app.Quit();
    }

    static async Task Body(App app, string[] args, StringBuilder r, bool quick)
    {
        string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        r.AppendLine($"SweaxMorph {version} bench · {DateTime.Now:yyyy-MM-dd HH:mm} · {Environment.ProcessPath}");
        using (var p = Process.GetCurrentProcess())
            r.AppendLine($"process start → bench ready: {(DateTime.Now - p.StartTime).TotalMilliseconds:0} ms · render tier {RenderCapability.Tier >> 16}");
        r.AppendLine($"UI Automation listeners: structure {AutomationPeer.ListenerExists(AutomationEvents.StructureChanged)}, property {AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)}, focus {AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged)}");

        if (args.Contains("--uia"))
        {
            _listener = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--uia-listen 400 --uia-pid {Environment.ProcessId}") { UseShellExecute = false });
            await Task.Delay(1500);
            r.AppendLine("a UI Automation client walks this copy's windows every 0.7 s (--uia)");
        }
        var mons = Monitors();
        foreach (var m in mons) r.AppendLine($"monitor {m.Name}: {m.Rect.Left},{m.Rect.Top} {m.Rect.Width}×{m.Rect.Height} · {m.Dpi} dpi ({m.Dpi / 96.0 * 100:0}%) · {m.Hz} Hz{(m.Primary ? " · primary" : "")}");

        string samples = Path.Combine(Paths.Data, "samples");
        Directory.CreateDirectory(samples);
        string photo = Path.Combine(samples, "big-photo.jpg");
        if (!File.Exists(photo)) Photo(photo, 4000, 2600);
        string doc = Path.Combine(samples, "notes.md");
        File.WriteAllText(doc, "# Notes\n\nHello.\n");
        app.Settings.Output = OutputPlace.Custom;
        app.Settings.OutputFolder = Path.Combine(Paths.Data, "out");
        Directory.CreateDirectory(app.Settings.OutputFolder);

        // the pointer the Well opens at, and the window kept out of sight
        var primary = mons.First(m => m.Primary);
        Win32.POINT cursor = Center(primary);
        Well.BenchCursor = () => cursor;
        Well.BenchQuiet = true;
        Well.BenchActive = true;
        Well.BenchCreated = w =>
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            int on = 1;
            Win32.DwmSetWindowAttribute(h, 13, ref on, 4); // DWMWA_CLOAK
            w.IsVisibleChanged += (_, _) => Visibility.Add((Now, w.IsVisible));
            w.LayoutUpdated += (_, _) => _layouts++;
        };

        var d = Dispatcher.CurrentDispatcher;
        d.Hooks.OperationStarted += (_, e) => Started[e.Operation] = Stopwatch.GetTimestamp();
        d.Hooks.OperationCompleted += (_, e) =>
        {
            if (!Started.Remove(e.Operation, out long t0) || e.Operation.Priority == DispatcherPriority.ContextIdle) return; // the bench's own work
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _stallSum += ms;
            _stallCount++;
            if (ms > _stallMax) { _stallMax = ms; _stallWhat = e.Operation.Priority.ToString(); }
        };
        int ui = (int)Win32.GetCurrentThreadId();

        // the clock: how often WPF draws with nothing to do but one empty animation
        double clock0 = Now;
        Springs.Run(_ => Now - clock0 < 600);
        CompositionTarget.Rendering += OnFrame;
        Frames.Clear();
        await Task.Delay(600);
        CompositionTarget.Rendering -= OnFrame;
        double idleClock = Median(Gaps(Frames.ToList()));
        _refMs = 1000.0 / Math.Max(30, mons.First(m => m.Primary).Hz);
        r.AppendLine($"WPF frame clock with no window shown: {idleClock:0.00} ms · a missed frame = a gap over {_refMs * 1.6:0.0} ms (1.6 refreshes of the primary monitor)");
        await Settle();

        // ── 1. opening and closing, on every monitor in turn ──────────────────
        r.AppendLine();
        r.AppendLine("== open / drag / idle / close (shortcut path: a worker thread posts ToggleWell at Send priority, like the keyboard hook)");
        int cycles = quick ? 4 : 8;
        var openMs = new List<double>();
        var firstMs = new List<double>();
        var placeErr = new List<double>();
        for (int c = 0; c < cycles; c++)
        {
            var mon = mons[c % mons.Count];
            cursor = Center(mon);
            await Task.Delay(c == 0 ? 50 : 900);
            var phase = Begin();
            double t0 = Now;
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            double shown = await Until(() => app.GetWell().IsVisible, 3000);
            double first = await FrameAfter(shown);
            double ready = await FrameAfter(first);
            await Task.Delay(700);
            var open = End(phase, t0, ui);
            openMs.Add(shown - t0);
            firstMs.Add(ready - t0);
            var w = app.GetWell();
            string place = Placement(w, cursor, mon, out double err);
            placeErr.Add(err);
            r.AppendLine($"open {c + 1} on {mon.Name}{(c == 0 ? " (cold: first open after start)" : "")}: visible after {shown - t0:0.0} ms · first frame {first - t0:0.0} ms · painted {ready - t0:0.0} ms");
            r.AppendLine($"    opening   {open}");
            r.AppendLine($"    placement {place}");

            // a file carried over the Well for 1.5 s, the way DragOver feeds it
            var files = new List<string> { photo };
            var heart = Well.Center;
            phase = Begin();
            double d0 = Now;
            int steps = 0;
            EventHandler carry = (_, _) =>
            {
                double u = (Now - d0) / 1500.0;
                double a = u * Math.PI * 2;
                w.DemoCarry(files, new Point(heart.X + Math.Cos(a) * (60 + 140 * (1 - u)), heart.Y + Math.Sin(a) * (60 + 140 * (1 - u))));
                steps++;
            };
            CompositionTarget.Rendering += carry;
            await Task.Delay(1500);
            CompositionTarget.Rendering -= carry;
            r.AppendLine($"    dragging  {End(phase, d0, ui)} · {steps} drag moves");
            w.DemoLeave();
            await Task.Delay(400);

            phase = Begin();
            double i0 = Now;
            await Task.Delay(1500);
            r.AppendLine($"    idle      {End(phase, i0, ui)}");

            double h0 = Now;
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            double hidden = await Until(() => !app.GetWell().IsVisible, 3000);
            r.AppendLine($"    close: hidden after {hidden - h0:0} ms");
        }
        r.AppendLine($"SUMMARY open: visible median {Median(openMs):0.0} ms (max {openMs.Max():0.0}) · painted median {Median(firstMs):0.0} ms (max {firstMs.Max():0.0}) · placement error max {placeErr.Max():0} px");

        // ── 2. a real conversion while Morph is on screen ─────────────────────
        r.AppendLine();
        r.AppendLine("== converting a 4000×2600 photo to WEBP while the Well animates");
        {
            cursor = Center(primary);
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            await Until(() => app.GetWell().IsVisible, 3000);
            await Task.Delay(500);
            var w = app.GetWell();
            w.DemoHold([photo]);
            await Task.Delay(700);
            var phase = Begin();
            double c0 = Now;
            w.DemoChoose("webp");
            await Until(() => w.Working, 2000);
            await Until(() => !w.Working, 60000);
            double took = Now - c0;
            await Task.Delay(400);
            r.AppendLine($"    converting {End(phase, c0, ui)} · took {took:0} ms");
            await Task.Delay(800);
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            await Until(() => !app.GetWell().IsVisible, 3000);
        }

        // ── 3. waiting, dozing, hidden: what Morph costs while you are elsewhere ──
        r.AppendLine();
        r.AppendLine("== while you are elsewhere");
        {
            Well.BenchActive = false; // out of focus, as when you click into Explorer
            cursor = Center(primary);
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            await Until(() => app.GetWell().IsVisible, 3000);
            await Task.Delay(1500);
            var phase = Begin();
            double w0 = Now;
            await Task.Delay(3000);
            r.AppendLine($"    resting (visible, out of focus)  {End(phase, w0, ui)}");
            await Task.Delay(quick ? 4500 : 5000); // 8 s of stillness: Morph dozes
            phase = Begin(false);
            w0 = Now;
            await Task.Delay(3000);
            r.AppendLine($"    dozing                           {End(phase, w0, ui)}");
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            await Until(() => !app.GetWell().IsVisible, 3000);
            Well.BenchActive = true;
            phase = Begin(false);
            w0 = Now;
            await Task.Delay(1500);
            r.AppendLine($"    hidden, just closed              {End(phase, w0, ui)} · {Memory()}");
            await Task.Delay(5000);
            phase = Begin(false);
            w0 = Now;
            await Task.Delay(3000);
            r.AppendLine($"    hidden, after the tray clean-up  {End(phase, w0, ui)} · {Memory()}");
            // the first open after the clean-up: the "sometimes it is slow" case
            phase = Begin();
            double t0 = Now;
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            double shown = await Until(() => app.GetWell().IsVisible, 3000);
            double first = await FrameAfter(shown);
            double ready = await FrameAfter(first);
            await Task.Delay(700);
            r.AppendLine($"    open after clean-up: visible {shown - t0:0.0} ms · painted {ready - t0:0.0} ms · {End(phase, t0, ui)}");
            ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
            await Until(() => !app.GetWell().IsVisible, 3000);
        }

        // ── 4. requests that get lost ─────────────────────────────────────────
        r.AppendLine();
        r.AppendLine("== lost requests");
        {
            // the shortcut while Morph waits (out of focus) on another monitor: you want it here
            if (mons.Count > 1)
            {
                Well.BenchActive = false;
                var a = mons[0];
                var b = mons[1];
                cursor = Center(a);
                ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
                await Until(() => app.GetWell().IsVisible, 3000);
                await Task.Delay(1500);
                cursor = Center(b);
                ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
                await Task.Delay(900);
                var w = app.GetWell();
                IntPtr h = new WindowInteropHelper(w).Handle;
                Win32.GetWindowRect(h, out var rect);
                bool there = w.IsVisible && rect.Left < b.Rect.Right && rect.Right > b.Rect.Left && rect.Top < b.Rect.Bottom && rect.Bottom > b.Rect.Top;
                r.AppendLine($"    shortcut while Morph waits on {a.Name}, pointer on {b.Name}: {(there ? "came to the pointer" : w.IsVisible ? "STAYED AWAY" : "HID instead of coming")}");
                if (w.IsVisible) { ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send)); await Until(() => !app.GetWell().IsVisible, 3000); }
                Well.BenchActive = true;
                await Task.Delay(500);
            }

            // shortcut presses in quick succession: every press should flip the Well
            cursor = Center(primary);
            foreach (int gap in new[] { 40, 90, 160, 260, 400 })
            {
                int lost = 0;
                bool expect = app.GetWell().IsVisible;
                for (int i = 0; i < 6; i++)
                {
                    ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send));
                    expect = !expect;
                    await Task.Delay(gap);
                    if (app.GetWell().Showing != expect) lost++;
                }
                r.AppendLine($"    6 presses {gap} ms apart: {lost} did not flip the Well");
                if (app.GetWell().IsVisible) { ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send)); }
                await Until(() => !app.GetWell().IsVisible, 3000);
                await Task.Delay(600);
            }

            // --toggle from another process (macro apps, launchers)
            string exe = Environment.ProcessPath!;
            string instance = Arg(args, "--instance") ?? "bench";
            var lat = new List<double>();
            int ipcLost = 0;
            for (int i = 0; i < (quick ? 3 : 6); i++)
            {
                bool before = app.GetWell().IsVisible;
                int seen = Visibility.Count;
                double t0 = Now;
                using var p = Process.Start(new ProcessStartInfo(exe, $"--toggle --instance {instance} --no-install") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() })!;
                double flip = await Until(() => Visibility.Count > seen, 6000);
                if (Visibility.Count > seen) lat.Add(flip - t0); else ipcLost++;
                await p.WaitForExitAsync();
                await Task.Delay(900);
            }
            r.AppendLine($"    --toggle from a new process: {lat.Count} of {lat.Count + ipcLost} arrived · latency median {Median(lat):0} ms, max {(lat.Count > 0 ? lat.Max() : 0):0} ms");
            int pairsLost = 0;
            for (int i = 0; i < 3; i++)
            {
                bool before = app.GetWell().IsVisible;
                var p1 = Process.Start(new ProcessStartInfo(exe, $"--toggle --instance {instance} --no-install") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() })!;
                await Task.Delay(60);
                var p2 = Process.Start(new ProcessStartInfo(exe, $"--toggle --instance {instance} --no-install") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() })!;
                await p1.WaitForExitAsync();
                await p2.WaitForExitAsync();
                await Task.Delay(1200);
                // two toggles: back where it was
                if (app.GetWell().Showing != before) pairsLost++;
                p1.Dispose(); p2.Dispose();
                if (app.GetWell().IsVisible) { ThreadPool.QueueUserWorkItem(_ => d.BeginInvoke(() => app.ToggleWell(), DispatcherPriority.Send)); await Until(() => !app.GetWell().IsVisible, 3000); }
                await Task.Delay(500);
            }
            r.AppendLine($"    two --toggle launches 60 ms apart: {pairsLost} of 3 pairs lost one of the two");
        }

        r.AppendLine();
        r.AppendLine($"memory at the end: {Memory()} · GC pauses in all {GC.GetTotalPauseDuration().TotalMilliseconds:0} ms · collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
    }

    /// <summary>
    /// --uia-listen SECONDS: a UI Automation client that listens to the whole desktop, like the touch keyboard, the emoji
    /// panel or PowerToys do on this PC at times. With one running, WPF reports every layout pass of every window to it.
    /// </summary>
    public static void Listen(int seconds, int pid)
    {
        var root = System.Windows.Automation.AutomationElement.RootElement;
        System.Windows.Automation.Automation.AddStructureChangedEventHandler(root, System.Windows.Automation.TreeScope.Subtree, (_, _) => { });
        System.Windows.Automation.Automation.AddAutomationPropertyChangedEventHandler(root, System.Windows.Automation.TreeScope.Subtree, (_, _) => { },
            System.Windows.Automation.AutomationElement.BoundingRectangleProperty, System.Windows.Automation.AutomationElement.NameProperty);
        // a focus-tracking client asks the windows it meets for their element tree; WPF then keeps reporting that tree
        var until = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < until)
        {
            if (pid > 0)
            {
                var windows = new List<IntPtr>();
                EnumWindows((h, _) => { GetWindowThreadProcessId(h, out uint p); if (p == pid) windows.Add(h); return true; }, IntPtr.Zero);
                foreach (var h in windows)
                {
                    try
                    {
                        var e = System.Windows.Automation.AutomationElement.FromHandle(h);
                        e.FindFirst(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                    }
                    catch { }
                }
            }
            Thread.Sleep(700);
        }
        System.Windows.Automation.Automation.RemoveAllEventHandlers();
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    // ── phases ──────────────────────────────────────────────────────────────

    sealed record Phase(Dictionary<int, TimeSpan> Threads, TimeSpan Cpu, double Pause, int Gc2, int Layouts);

    static bool _listening;

    /// <summary>Starts a measured stretch. Frames are only counted when asked: listening to every frame keeps WPF drawing.</summary>
    static Phase Begin(bool frames = true)
    {
        Frames.Clear();
        if (frames && !_listening) { CompositionTarget.Rendering += OnFrame; _listening = true; }
        if (!frames && _listening) { CompositionTarget.Rendering -= OnFrame; _listening = false; }
        _stallMax = 0; _stallSum = 0; _stallCount = 0; _stallWhat = "";
        using var p = Process.GetCurrentProcess();
        return new Phase(ThreadTimes(), p.TotalProcessorTime, GC.GetTotalPauseDuration().TotalMilliseconds, GC.CollectionCount(2), _layouts);
    }

    static string End(Phase b, double t0, int ui)
    {
        double wall = Now - t0;
        if (_listening) { CompositionTarget.Rendering -= OnFrame; _listening = false; }
        var stamps = Frames.Where(f => f >= t0).ToList();
        var gaps = Gaps(stamps);
        using var p = Process.GetCurrentProcess();
        double cpu = (p.TotalProcessorTime - b.Cpu).TotalMilliseconds / wall * 100;
        string frames = gaps.Count < 2 ? $"frames {stamps.Count} (none drawn)"
            : $"frames {stamps.Count} ({stamps.Count / wall * 1000:0}/s) median {Median(gaps):0.0} p95 {Pct(gaps, 0.95):0.0} worst {gaps.Max():0.0} ms · missed {gaps.Count(g => g > _refMs * 1.6)}";
        return $"{frames} · cpu {cpu:0}% ({Busiest(b.Threads, ThreadTimes(), wall, ui)}) · ui busiest op {_stallMax:0.0} ms ({_stallWhat}), ui busy {_stallSum / wall * 100:0}% · layouts {_layouts - b.Layouts} · gc pause {GC.GetTotalPauseDuration().TotalMilliseconds - b.Pause:0} ms, gen2 {GC.CollectionCount(2) - b.Gc2}";
    }

    static void OnFrame(object? sender, EventArgs e) => Frames.Add(Now);

    /// <summary>Waits without keeping the UI thread busy; the exact moment comes from the event log when there is one.</summary>
    static async Task<double> Until(Func<bool> done, double timeout)
    {
        double t0 = Now;
        int seen = Visibility.Count;
        while (!done() && Now - t0 < timeout) await Task.Delay(4);
        return Visibility.Count > seen ? Visibility[^1].t : Now;
    }

    /// <summary>The first frame WPF starts after <paramref name="t"/>.</summary>
    static async Task<double> FrameAfter(double t)
    {
        double t0 = Now;
        while (Now - t0 < 2000)
        {
            var hit = Frames.Where(f => f > t).Cast<double?>().FirstOrDefault();
            if (hit is { } h) return h;
            await Task.Delay(2);
        }
        return Now;
    }

    static async Task Settle() => await Task.Delay(300);

    static string Placement(Well w, Win32.POINT cursor, Mon mon, out double err)
    {
        IntPtr h = new WindowInteropHelper(w).Handle;
        Win32.GetWindowRect(h, out var rect);
        uint dpi = Win32.GetDpiForWindow(h);
        double k = dpi / 96.0;
        double wpf = VisualTreeHelper.GetDpi(w).PixelsPerDip;
        var heart = Well.Center;
        double hx = rect.Left + heart.X * k, hy = rect.Top + heart.Y * k;
        err = Math.Sqrt((hx - cursor.X) * (hx - cursor.X) + (hy - cursor.Y) * (hy - cursor.Y));
        int ew = (int)Math.Round(w.Width * mon.Dpi / 96.0), eh = (int)Math.Round(w.Height * mon.Dpi / 96.0);
        string size = rect.Width == ew && rect.Height == eh ? "size ok" : $"SIZE {rect.Width}×{rect.Height}, expected {ew}×{eh}";
        string dpiOk = dpi == mon.Dpi && Math.Abs(wpf - mon.Dpi / 96.0) < 0.01 ? "dpi ok" : $"DPI window {dpi}, WPF {wpf * 96:0}, monitor {mon.Dpi}";
        return $"Morph's centre {err:0} px from the pointer · {size} · {dpiOk}";
    }

    // ── measuring helpers ───────────────────────────────────────────────────

    static List<double> Gaps(List<double> s) => s.Zip(s.Skip(1), (a, b) => b - a).ToList();
    static double Median(List<double> v) => v.Count == 0 ? 0 : v.OrderBy(x => x).ElementAt(v.Count / 2);
    static double Pct(List<double> v, double p) => v.Count == 0 ? 0 : v.OrderBy(x => x).ElementAt(Math.Min(v.Count - 1, (int)(v.Count * p)));

    static string Memory()
    {
        using var p = Process.GetCurrentProcess();
        return $"working set {p.WorkingSet64 / 1048576} MB, private {p.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB";
    }

    static Dictionary<int, TimeSpan> ThreadTimes()
    {
        var d = new Dictionary<int, TimeSpan>();
        using var p = Process.GetCurrentProcess();
        foreach (ProcessThread t in p.Threads) { try { d[t.Id] = t.TotalProcessorTime; } catch { } }
        return d;
    }

    static string Busiest(Dictionary<int, TimeSpan> a, Dictionary<int, TimeSpan> b, double wallMs, int ui) =>
        string.Join(" ", b.Select(kv => (id: kv.Key, ms: (kv.Value - (a.TryGetValue(kv.Key, out var v) ? v : TimeSpan.Zero)).TotalMilliseconds))
            .OrderByDescending(x => x.ms).Take(3).Where(x => x.ms > 0).Select(x => $"{(x.id == ui ? "ui" : "t" + x.id)} {x.ms / wallMs * 100:0}%"));

    static string? Arg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static void Photo(string file, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(59, 42, 110), Color.FromRgb(255, 154, 98), 90), null, new Rect(0, 0, w, h));
            var rnd = new Random(3);
            for (int i = 0; i < 900; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)rnd.Next(40, 220), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256))), null, new Point(rnd.Next(w), rnd.Next(h)), rnd.Next(4, 90), rnd.Next(4, 90));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    // ── monitors ────────────────────────────────────────────────────────────

    internal sealed record Mon(string Name, Win32.RECT Rect, Win32.RECT Work, uint Dpi, int Hz, bool Primary);

    static Win32.POINT Center(Mon m) => new() { X = (m.Work.Left + m.Work.Right) / 2, Y = (m.Work.Top + m.Work.Bottom) / 2 };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize; public Win32.RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    internal static List<Mon> Monitors()
    {
        var handles = new List<IntPtr>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hm, IntPtr _, ref Win32.RECT _, IntPtr _) => { handles.Add(hm); return true; }, IntPtr.Zero);
        var list = new List<Mon>();
        foreach (var hm in handles)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(hm, ref mi);
            uint dpi = Win32.GetDpiForMonitor(hm, 0, out uint dx, out _) == 0 ? dx : 96;
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            int hz = EnumDisplaySettings(mi.szDevice, -1, ref dm) ? dm.dmDisplayFrequency : 0;
            list.Add(new Mon(mi.szDevice.Replace(@"\\.\", ""), mi.rcMonitor, mi.rcWork, dpi, hz, (mi.dwFlags & 1) != 0));
        }
        return list;
    }
}
