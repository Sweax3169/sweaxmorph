using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Interop;
using Microsoft.Win32;
using SweaxMorph.Native;

namespace SweaxMorph.Core;

/// <summary>An invisible window for the tray icon, theme changes and session notifications.</summary>
public sealed class HostWindow : IDisposable
{
    public const int WM_TRAY = Win32.WM_APP + 1;
    readonly HwndSource _src;
    readonly uint _taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");

    public IntPtr Handle => _src.Handle;
    public event Action<int>? TrayMessage;
    public event Action? TaskbarCreated;
    public event Action? SessionUnlocked;
    public event Action? ThemeChanged;

    public HostWindow()
    {
        var p = new HwndSourceParameters("SweaxMorph.Host")
        {
            Width = 0, Height = 0, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW,
        };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
        Win32.WTSRegisterSessionNotification(Handle, 0);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_TRAY:
                TrayMessage?.Invoke((int)((long)lParam & 0xFFFF));
                handled = true;
                break;
            case 0x001A when lParam != IntPtr.Zero && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                ThemeChanged?.Invoke();
                break;
            case Win32.WM_WTSSESSION_CHANGE:
                if ((int)wParam is 0x8 or 0x2) SessionUnlocked?.Invoke();
                break;
            default:
                if (msg == _taskbarCreated) TaskbarCreated?.Invoke();
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Win32.WTSUnRegisterSessionNotification(Handle);
        _src.Dispose();
    }
}

/// <summary>Notification-area icon drawn with plain Shell_NotifyIcon.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly HostWindow _host;
    IntPtr _icon;
    bool _added;
    string _tip;

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon(HostWindow host, string tip)
    {
        _host = host;
        _tip = tip;
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SweaxMorph.exe");
        var small = new IntPtr[1];
        if (Win32.ExtractIconEx(exe, 0, null, small, 1) > 0) _icon = small[0];
        host.TrayMessage += OnMessage;
        host.TaskbarCreated += () => { _added = false; Add(); };
        Add();
    }

    void OnMessage(int msg)
    {
        switch (msg)
        {
            case Win32.NIN_SELECT or Win32.NIN_KEYSELECT: LeftClick?.Invoke(); break;
            case Win32.WM_CONTEXTMENU: RightClick?.Invoke(); break;
        }
    }

    Win32.NOTIFYICONDATA Data() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _host.Handle,
        uID = 1,
        uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP,
        uCallbackMessage = HostWindow.WM_TRAY,
        hIcon = _icon,
        szTip = _tip.Length > 120 ? _tip[..120] : _tip,
        szInfo = "",
        szInfoTitle = "",
        uVersion = Win32.NOTIFYICON_VERSION_4,
    };

    void Add()
    {
        if (_added) return;
        var d = Data();
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref d);
        if (_added) Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref d);
    }

    public void SetTip(string tip)
    {
        _tip = tip;
        var d = Data();
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref d);
    }

    public void Dispose()
    {
        var d = Data();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref d);
        if (_icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
    }
}

/// <summary>
/// Files handed over by later launches (the right-click menu starts one per file, Send to one for all):
/// they arrive over a named pipe and are gathered for a moment, so ten files open one well, not ten.
/// </summary>
public sealed class Ipc : IDisposable
{
    public const string PipeName = "SweaxMorph.Files.7d21";
    readonly CancellationTokenSource _stop = new();
    readonly List<string> _batch = [];
    readonly object _gate = new();
    Timer? _flush;

    /// <summary>Raised on a worker thread with the gathered command and files.</summary>
    public event Action<string, List<string>>? Received;
    string _command = "";

    public void Start() => Task.Run(Loop);

    async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? command = await reader.ReadLineAsync();
                var files = new List<string>();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null) if (line.Length > 0) files.Add(line);
                lock (_gate)
                {
                    _command = command ?? "";
                    _batch.AddRange(files.Where(f => !_batch.Contains(f, StringComparer.OrdinalIgnoreCase)));
                    _flush?.Dispose();
                    _flush = new Timer(_ => Flush(), null, 280, Timeout.Infinite);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Paths.Log("pipe: " + ex.Message); await Task.Delay(200); }
        }
    }

    void Flush()
    {
        List<string> files;
        string command;
        lock (_gate)
        {
            files = [.. _batch];
            command = _command;
            _batch.Clear();
        }
        Received?.Invoke(command, files);
    }

    /// <summary>From a second launch: hand the files to the running SweaxMorph. False when none is listening.</summary>
    public static bool Send(string command, IEnumerable<string> files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            w.WriteLine(command);
            foreach (var f in files) w.WriteLine(f);
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _flush?.Dispose();
    }
}

/// <summary>Right-click menu, Send to, Start menu and start-up entries — all per user, no admin needed.</summary>
public static class Shell
{
    const string Verb = @"Software\Classes\*\shell\SweaxMorph";
    const string FolderVerb = @"Software\Classes\Directory\shell\SweaxMorph";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SweaxMorph.exe");

    public static void ContextMenu(bool on)
    {
        try
        {
            foreach (var key in new[] { Verb, FolderVerb })
            {
                if (!on) { Registry.CurrentUser.DeleteSubKeyTree(key, false); continue; }
                using var k = Registry.CurrentUser.CreateSubKey(key);
                k.SetValue("MUIVerb", Loc.T("Menu.Verb"));
                k.SetValue("Icon", $"\"{Exe}\",0");
                k.SetValue("MultiSelectModel", "Player");
                using var cmd = k.CreateSubKey("command");
                cmd.SetValue("", $"\"{Exe}\" --convert \"%1\"");
            }
            SendTo(on);
        }
        catch (Exception ex) { Paths.Log("context menu: " + ex.Message); }
    }

    public static bool ContextMenuInstalled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Verb + @"\command");
        return k?.GetValue("") is string s && s.Contains(Exe, StringComparison.OrdinalIgnoreCase);
    }

    static void SendTo(bool on)
    {
        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "SweaxMorph.lnk");
        if (!on) { try { File.Delete(lnk); } catch { } return; }
        Shortcut(lnk, "--convert", Loc.T("Menu.Verb"));
    }

    public static void StartMenu() => Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SweaxMorph.lnk"), "", "SweaxMorph — drop any file, pick a format");

    static void Shortcut(string lnk, string args, string description)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = Exe;
            sc.Arguments = args;
            sc.WorkingDirectory = Path.GetDirectoryName(Exe);
            sc.Description = description;
            sc.IconLocation = Exe + ",0";
            sc.Save();
        }
        catch (Exception ex) { Paths.Log("shortcut: " + ex.Message); }
    }

    public static void AutoStart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (on) k.SetValue("SweaxMorph", $"\"{Exe}\" --background");
            else k.DeleteValue("SweaxMorph", false);
        }
        catch (Exception ex) { Paths.Log("autostart: " + ex.Message); }
    }

    public static bool AutoStartOn()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("SweaxMorph") is string s && s.Contains(Exe, StringComparison.OrdinalIgnoreCase);
    }

    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SweaxMorph");

    /// <summary>Copies itself to %LOCALAPPDATA%\Programs\SweaxMorph on first run; returns the installed exe, or null.</summary>
    public static string? RelocateIfNeeded()
    {
        try
        {
            string here = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            if (string.Equals(here, Home, StringComparison.OrdinalIgnoreCase)) return null;
            if (Directory.Exists(Path.Combine(here, "sweaxmorph-data"))) return null; // portable
            if (here.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return null; // dev build
            Directory.CreateDirectory(Home);
            foreach (var f in Directory.EnumerateFiles(here))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(Home, name), true);
            }
            return Path.Combine(Home, Path.GetFileName(Environment.ProcessPath!));
        }
        catch (Exception ex)
        {
            Paths.Log("install: " + ex.Message);
            return null;
        }
    }
}
