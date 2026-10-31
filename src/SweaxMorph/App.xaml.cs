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
    const string InstanceName = "SweaxMorph.SingleInstance.3b9e";
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
    readonly DispatcherTimer _rearm = new() { Interval = TimeSpan.FromMinutes(10) };
    readonly DispatcherTimer _trim = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(4) };

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
        bool Has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        var files = args.Where(a => !a.StartsWith("--") && (File.Exists(a) || Directory.Exists(a))).Select(Path.GetFullPath).ToList();

        if (Has("--selftest") || Has("--formats") || Has("--icon") || Has("--shots"))
        {
            Dev.Demo.Run(this, args);
            return;
        }

        _instance = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            // already running: hand over the files (or the request) and leave
            Ipc.Send(Has("--quit") ? "quit" : Has("--settings") ? "settings" : files.Count > 0 ? "convert" : "open", files);
            Shutdown();
            return;
        }
        if (Has("--quit")) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) => { Paths.Log("ui: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Paths.Log("fatal: " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Paths.Log("task: " + ex.Exception); ex.SetObserved(); };

        string? portable = Path.Combine(AppContext.BaseDirectory, "sweaxmorph-data");
        Paths.Init(Directory.Exists(portable) ? portable : null);
        bool firstRun = !File.Exists(Path.Combine(Paths.Data, "settings.json"));
        if (!Has("--no-install") && Shell.RelocateIfNeeded() is { } installed)
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
        Theme.Apply(Settings.Theme);
        Engines.Detect();
        Planner.Build(AllConverters());

        _host = new HostWindow();
        _host.ThemeChanged += () => { _themeDelay.Stop(); _themeDelay.Start(); };
        _host.SessionUnlocked += () => _hook?.Rearm();
        _themeDelay.Tick += (_, _) => { _themeDelay.Stop(); Theme.Apply(Settings.Theme); };
        _tray = new TrayIcon(_host, Loc.T("Tray.Tip"));
        _tray.LeftClick += () => OpenWell(Well.Opening.Hotkey, null);
        _tray.RightClick += ShowTrayMenu;

        _hook = new KeyboardHook();
        _hook.Pressed += () => Dispatcher.BeginInvoke(() => ToggleWell(), DispatcherPriority.Send);
        _hook.DragTapped += () => Dispatcher.BeginInvoke(() => OpenWell(Well.Opening.Drag, null), DispatcherPriority.Send);
        ApplyHotkey();
        _rearm.Tick += (_, _) => _hook?.Rearm();
        _rearm.Start();

        _ipc = new Ipc();
        _ipc.Received += (command, list) => Dispatcher.BeginInvoke(() => Handle(command, list));
        _ipc.Start();

        if (firstRun)
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

        _trim.Tick += (_, _) => { _trim.Stop(); if (_well is not { IsVisible: true } && _settings == null) Trim(); };
        _trim.Start();

        if (files.Count > 0) OpenWell(Well.Opening.Files, files);
        else if (Has("--settings")) ShowSettings();
        else if (!Has("--background")) OpenWell(Well.Opening.Hotkey, null);
    }

    void Handle(string command, List<string> files)
    {
        switch (command)
        {
            case "quit": Quit(); break;
            case "settings": ShowSettings(); break;
            default: OpenWell(files.Count > 0 ? Well.Opening.Files : Well.Opening.Hotkey, files.Count > 0 ? files : null); break;
        }
    }

    Well GetWell()
    {
        if (_well != null) return _well;
        _well = new Well();
        _well.Status += s => _tray?.SetTip(s == null ? Loc.T("Tray.Tip") : "SweaxMorph — " + s);
        _well.IsVisibleChanged += (_, _) => { if (!_well.IsVisible) { _trim.Stop(); _trim.Start(); } };
        return _well;
    }

    public void OpenWell(Well.Opening how, IReadOnlyList<string>? files) => GetWell().Open(how, files);

    void ToggleWell()
    {
        var w = GetWell();
        // the shortcut puts Morph away again, also while it waits on top of another window
        if (w.Showing) w.HideWell();
        else w.Open(Well.Opening.Hotkey);
    }

    /// <summary>Recordings run on their own settings, never the user's.</summary>
    internal void UseSettings(Settings s) => Settings = s;

    /// <summary>After a language change: new words everywhere.</summary>
    public void Relabel()
    {
        _tray?.SetTip(Loc.T("Tray.Tip"));
        if (_well is { IsVisible: false, Busy: false }) { _well.Close(); _well = null; }
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

    /// <summary>Resting in the tray costs next to nothing: give memory back while nobody is looking.</summary>
    static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        Win32.TrimWorkingSet();
    }

    public void Quit()
    {
        _ipc?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        _instance?.ReleaseMutex();
        Shutdown();
    }
}
