using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SweaxMorph.Core;
using SweaxMorph.Core.Converters;
using SweaxMorph.Native;
using SweaxMorph.UI;

namespace SweaxMorph;

public partial class App : Application
{
    const string InstanceName = Launch.InstanceName;
    Mutex? _instance;

    public static App Instance => (App)Current;
    public Settings Settings { get; private set; } = null!;
    HostWindow? _host;
    TrayIcon? _tray;
    KeyboardHook? _hook;
    Ipc? _ipc;
    Well? _well;
    SettingsWindow? _settings;
    Window? _menuHost;
    readonly DispatcherTimer _themeDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    // Windows drops a keyboard hook without a word if it ever answers late (a busy disk, a long pause): re-arm it often,
    // it costs nothing. 1.0 re-armed every 10 minutes, so one slow moment could leave the shortcut dead for that long.
    readonly DispatcherTimer _rearm = new() { Interval = TimeSpan.FromSeconds(20) };
    // memory goes back only after the PC has been left alone for 10 minutes (1.0 emptied it after every close, so the
    // next open, and every key the hook saw, had to page it all back in)
    readonly DispatcherTimer _idleTrim = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMinutes(2) };
    bool _trimmed;
    long _lastHotkey;

    /// <summary>The Well has been built and drawn once, invisibly, so the first open is as quick as any other.</summary>
    internal bool WarmedUp { get; private set; }

    public static IReadOnlyList<Converter> AllConverters() =>
    [
        new ImageConverter(), new MediaConverter(), new DocumentConverter(), new BrowserPdfConverter(),
        new OfficeConverter(Engines.Word), new OfficeConverter(Engines.Excel), new OfficeConverter(Engines.PowerPoint),
        new LibreOfficeConverter(), new PdfConverter(), new OcrConverter(), new DataConverter(), new ArchiveConverter(),
        new EbookConverter(), new FontConverter(),
    ];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var clock = Stopwatch.StartNew();
        bool Has(string a) => Launch.Has(args, a);
        var files = Launch.Files(args);

        if (Arg(args, "--uia-listen") is { } listen)
        {
            Dev.Bench.Listen(int.TryParse(listen, out int sec) ? sec : 60, int.TryParse(Arg(args, "--uia-pid"), out int pid) ? pid : 0);
            Shutdown();
            return;
        }
        if (Has("--selftest") || Has("--formats") || Has("--icon") || Has("--shots") || Has("--palettes"))
        {
            Dev.Demo.Run(this, args);
            return;
        }

        // --instance NAME runs a separate copy (its own mutex and pipe) that never installs or registers anything: the bench uses it
        string? instance = Arg(args, "--instance");
        bool isolated = instance != null;
        bool bench = Has("--bench");
        if (isolated) Ipc.Suffix = Launch.Suffix(args);
        if (bench) Dev.Bench.Prepare();

        _instance = new Mutex(true, InstanceName + Ipc.Suffix, out bool first);
        if (!first)
        {
            // already running: hand over the files (or the request) and leave
            // --toggle is the shortcut for macro keys and launchers that run a program instead of pressing keys
            if (Launch.Command(args, files) is { } command) Ipc.Send(command, files);
            Shutdown();
            return;
        }
        if (Has("--quit")) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) => { Paths.Log("ui: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Paths.Log("fatal: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Paths.Log("task: " + ex.Exception); ex.SetObserved(); };

        string? portable = Path.Combine(AppContext.BaseDirectory, "sweaxmorph-data");
        Paths.Init(Arg(args, "--data") ?? (Directory.Exists(portable) ? portable : null));
        bool firstRun = !File.Exists(Path.Combine(Paths.Data, "settings.json"));
        if (!isolated && !Has("--no-install") && Shell.RelocateIfNeeded() is { } installed)
        {
            _instance.ReleaseMutex();
            _instance.Dispose();
            _instance = null;
            Process.Start(new ProcessStartInfo(installed, string.Join(' ', args.Select(a => $"\"{a}\""))) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installed) });
            Shutdown();
            return;
        }

        Settings = Settings.Load();
        Loc.Init(Settings.Language);
        Theme.Apply(Settings.Theme, Settings.Palette);
        long tSettings = clock.ElapsedMilliseconds;

        _host = new HostWindow();
        _host.ThemeChanged += () => { _themeDelay.Stop(); _themeDelay.Start(); };
        _host.SessionUnlocked += () => _hook?.Rearm();
        _themeDelay.Tick += (_, _) => { _themeDelay.Stop(); Theme.Apply(Settings.Theme, Settings.Palette); };
        if (!bench)
        {
            _tray = new TrayIcon(_host, Loc.T("Tray.Tip"));
            _tray.LeftClick += () => OpenWell(Well.Opening.Hotkey, null);
            _tray.RightClick += ShowTrayMenu;

            _hook = new KeyboardHook();
            _hook.Pressed += () => Dispatcher.BeginInvoke(() => ToggleWell(fromHook: true), DispatcherPriority.Send);
            _hook.DragTapped += () => Dispatcher.BeginInvoke(() => OpenWell(Well.Opening.Drag, null), DispatcherPriority.Send);
            ApplyHotkey();
            _rearm.Tick += (_, _) => _hook?.Rearm();
            _rearm.Start();
        }

        _ipc = new Ipc();
        _ipc.Received += (command, list) => Dispatcher.BeginInvoke(() => Handle(command, list), DispatcherPriority.Send);
        _ipc.Start();
        long tReady = clock.ElapsedMilliseconds;

        // the shortcut works from here on; the converters' map and the first drawing of the Well follow
        Engines.Detect();
        Planner.Build(AllConverters());
        long tPlanner = clock.ElapsedMilliseconds;
        Dispatcher.BeginInvoke(() => WarmUp(() =>
        {
            WarmedUp = true;
            Paths.Log($"started in {clock.ElapsedMilliseconds} ms (settings {tSettings}, shortcut ready {tReady}, formats {tPlanner}, Morph drawn {clock.ElapsedMilliseconds}) · {(Process.GetCurrentProcess().StartTime is var st ? (DateTime.Now - st).TotalMilliseconds : 0):0} ms since the process began");
            // ContextIdle: the bench's own steps are told apart from the app's work by their priority
            if (bench) Dispatcher.BeginInvoke(() => Dev.Bench.Run(this, args), DispatcherPriority.ContextIdle);
        }), DispatcherPriority.ApplicationIdle);
        if (bench) return;
        if (isolated) { }
        else if (firstRun)
        {
            Settings.Save();
            if (Settings.StartWithWindows) Shell.AutoStart(true);
            if (Settings.ContextMenu) Shell.ContextMenu(true);
            Shell.StartMenu();
        }
        else
        {
            // the exe may have moved (update, portable copy): keep the registrations pointing at it
            if (Settings.StartWithWindows && !Shell.AutoStartOn()) Shell.AutoStart(true);
            if (Settings.ContextMenu && !Shell.ContextMenuInstalled()) Shell.ContextMenu(true);
        }

        if (Settings.AdminKeys) _ = Elevation.StartHelper();

        _idleTrim.Tick += (_, _) =>
        {
            long idle = Win32.IdleMs();
            if (idle < 60_000) _trimmed = false;
            else if (!_trimmed && idle > 10 * 60_000 && _well is not { IsVisible: true } && _settings == null) { _trimmed = true; Trim(); }
        };
        _idleTrim.Start();

        if (files.Count > 0) OpenWell(Well.Opening.Files, files);
        else if (Has("--settings")) ShowSettings();
        else if (!Has("--background")) OpenWell(Well.Opening.Hotkey, null);
    }

    static string? Arg(string[] args, string name) => Launch.Arg(args, name);

    internal void Handle(string command, List<string> files)
    {
        switch (command)
        {
            case "quit": Quit(); break;
            case "settings": ShowSettings(); break;
            case "toggle": ToggleWell(); break;
            case "toggle-admin": ToggleWell(fromHelper: true); break;
            default: OpenWell(files.Count > 0 ? Well.Opening.Files : Well.Opening.Hotkey, files.Count > 0 ? files : null); break;
        }
    }

    /// <summary>Draws the Well once, invisibly; whatever happens, <paramref name="done"/> runs.</summary>
    void WarmUp(Action done)
    {
        try { GetWell().Warm(done); }
        catch (Exception ex) { Paths.Log("warm-up: " + ex); done(); }
    }

    internal Well GetWell()
    {
        if (_well != null) return _well;
        _well = new Well();
        _well.Status += s => _tray?.SetTip(s == null ? Loc.T("Tray.Tip") : "SweaxMorph — " + s);
        return _well;
    }

    public void OpenWell(Well.Opening how, IReadOnlyList<string>? files) => GetWell().Open(how, files);

    /// <summary>
    /// The shortcut. Morph with you (under the pointer, or in use on this monitor): it goes away. Morph waiting
    /// somewhere else — on another monitor, or far off while you were busy — comes to you instead of hiding unseen.
    /// </summary>
    internal void ToggleWell(bool fromHook = false, bool fromHelper = false)
    {
        // the administrator helper and our own hook can both see one press (a window that is administrator-ish to one
        // and not the other): the second report within a moment is the same press
        long now = Environment.TickCount64;
        if ((fromHook || fromHelper) && now - _lastHotkey < 300) return;
        if (fromHook || fromHelper) _lastHotkey = now;
        var w = GetWell();
        if (w.Showing && w.IsHere()) w.HideWell();
        else w.Open(Well.Opening.Hotkey);
    }

    /// <summary>Recordings run on their own settings, never the user's.</summary>
    internal void UseSettings(Settings s) => Settings = s;

    /// <summary>After a language change: new words everywhere.</summary>
    public void Relabel()
    {
        _tray?.SetTip(Loc.T("Tray.Tip"));
        if (_well is { IsVisible: false, Busy: false })
        {
            _well.Close();
            _well = null;
            Dispatcher.BeginInvoke(() => WarmUp(() => { }), DispatcherPriority.ApplicationIdle); // ready again for the first open
        }
    }

    public void ApplyHotkey()
    {
        if (_hook == null) return;
        _hook.Hotkey = Hotkey.Parse(Settings.Hotkey);
        _hook.Enabled = Settings.HotkeyEnabled || Settings.DragTrigger;
        _hook.DragTrigger = Settings.DragTrigger;
    }

    public void RecordHotkey(Action<Hotkey?> done)
    {
        if (_hook == null) { done(null); return; }
        _hook.Record(h => Dispatcher.BeginInvoke(() => done(h)));
    }

    public void StopRecordingHotkey() => _hook?.StopRecording();

    public void ShowSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    void ShowTrayMenu()
    {
        _menuHost ??= new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = null, ShowInTaskbar = false, Width = 0, Height = 0, Left = -32000, Top = -32000 };
        _menuHost.Show();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_menuHost).Handle);
        var menu = new ContextMenu { FontFamily = Theme.Rounded, FontWeight = FontWeights.Bold };
        MenuItem Item(string text, Action act) { var m = new MenuItem { Header = text }; m.Click += (_, _) => act(); return m; }
        menu.Items.Add(Item(Loc.T("Tray.Open"), () => OpenWell(Well.Opening.Hotkey, null)));
        menu.Items.Add(Item(Loc.T("Tray.Settings"), ShowSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Tray.Quit"), Quit));
        menu.Closed += (_, _) => _menuHost.Hide();
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>Resting in the tray costs next to nothing: give memory back once the PC has been left alone for a while.</summary>
    static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        Win32.TrimWorkingSet();
    }

    public void Quit()
    {
        Elevation.StopHelper(); // the helper would leave by itself within seconds; an update can't wait for that
        _ipc?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        _instance?.ReleaseMutex();
        Shutdown();
    }
}
