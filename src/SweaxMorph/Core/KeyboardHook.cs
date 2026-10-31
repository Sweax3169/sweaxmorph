using System.IO;
using System.Runtime.InteropServices;
using SweaxMorph.Native;

namespace SweaxMorph.Core;

/// <summary>A global shortcut such as Win+Alt+F. Parsed from "Win+Alt+F", "Ctrl+Shift+M", …</summary>
public readonly record struct Hotkey(bool Win, bool Ctrl, bool Alt, bool Shift, int Key)
{
    public static Hotkey Parse(string? s)
    {
        bool win = false, ctrl = false, alt = false, shift = false;
        int key = 'F';
        foreach (var part in (s ?? "Win+Alt+F").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "win": win = true; break;
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default:
                    if (part.Length == 1 && char.IsLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
                    else if (part.StartsWith('F') && int.TryParse(part[1..], out int f) && f is >= 1 and <= 24) key = 0x6F + f;
                    else if (Named.FirstOrDefault(n => n.Value.Equals(part, StringComparison.OrdinalIgnoreCase)) is { Value: not null } hit) key = hit.Key;
                    break;
            }
        }
        if (!win && !ctrl && !alt) win = true; // never steal a bare key
        return new Hotkey(win, ctrl, alt, shift, key);
    }

    static readonly Dictionary<int, string> Named = new()
    {
        [0x20] = "Space", [0x2D] = "Insert", [0x2C] = "PrtSc", [0x24] = "Home", [0x23] = "End", [0x21] = "PgUp", [0x22] = "PgDn",
    };

    /// <summary>Keys a shortcut can end on: letters, digits, F1–F24 and a few named keys.</summary>
    public static bool IsBindable(int vk) => vk is (>= 0x41 and <= 0x5A) or (>= 0x30 and <= 0x39) or (>= 0x70 and <= 0x87) || Named.ContainsKey(vk);

    /// <summary>The form saved in settings: "Win+Shift+T".</summary>
    public string Spec => ToString().Replace(" ", "");

    public override string ToString()
    {
        var parts = new List<string>();
        if (Win) parts.Add("Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key is >= 0x70 and <= 0x87 ? "F" + (Key - 0x6F) : Named.TryGetValue(Key, out var n) ? n : ((char)Key).ToString());
        return string.Join(" + ", parts);
    }
}

/// <summary>
/// Low-level keyboard hook on its own thread with its own message loop, so a busy UI can never
/// make Windows drop the hook. It swallows the shortcut (it never reaches the app in front) and
/// taps an unassigned key so the Start menu does not open when Win is released.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    const uint WM_REHOOK = Win32.WM_APP + 7;

    readonly Win32.HookProc _proc;
    readonly Thread _thread;
    uint _threadId;
    IntPtr _hook;
    volatile int _swallowKey;
    volatile Action<Hotkey?>? _record;
    readonly ManualResetEventSlim _ready = new();

    public volatile bool Enabled = true;
    public Hotkey Hotkey { get; set; } = Hotkey.Parse("Win+Alt+F");
    /// <summary>Space while the left button carries files in Explorer opens the well under them.</summary>
    public volatile bool DragTrigger = true;

    /// <summary>Raised on the hook thread — handlers must only post work elsewhere.</summary>
    public event Action? Pressed;
    /// <summary>Space was tapped during a drag in Explorer (hook thread).</summary>
    public event Action? DragTapped;

    /// <summary>
    /// Captures the next shortcut pressed anywhere (even ones Windows would take, such as Win + letter) and
    /// reports it instead of acting on it; Esc reports null. The callback runs on the hook thread.
    /// </summary>
    public void Record(Action<Hotkey?> done) => _record = done;
    public void StopRecording() => _record = null;

    public KeyboardHook()
    {
        _proc = Callback;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SweaxMorph.Hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait(2000);
    }

    void Loop()
    {
        _threadId = Win32.GetCurrentThreadId();
        Install();
        _ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_REHOOK) { Uninstall(); Install(); continue; }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        Uninstall();
    }

    void Install()
    {
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Paths.Log("keyboard hook failed: " + Marshal.GetLastWin32Error());
    }

    void Uninstall()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Windows silently removes hooks that ever time out; re-arming now and then keeps us alive for years.</summary>
    public void Rearm() => Win32.PostThreadMessage(_threadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero);

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
            bool up = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
            bool injected = (kb.flags & Win32.LLKHF_INJECTED) != 0;

            if (!injected)
            {
                int vk = (int)kb.vkCode;
                if (down && _record is { } record)
                {
                    if (vk == 0x1B) { _record = null; _swallowKey = vk; record(null); return 1; }
                    if (Hotkey.IsBindable(vk))
                    {
                        var got = Current(vk);
                        _record = null;
                        Swallow(got);
                        record(got);
                        return 1;
                    }
                }
                else if (Enabled && down)
                {
                    if (_swallowKey == vk) return 1; // auto-repeat while held
                    var hk = Hotkey;
                    if (vk == hk.Key && ModifiersMatch(hk)) { Swallow(hk); Pressed?.Invoke(); return 1; }
                    if (DragTrigger && vk == 0x20 && Win32.IsKeyDown(0x01) && ExplorerInFront()) { _swallowKey = vk; DragTapped?.Invoke(); return 1; }
                }
                if (up && _swallowKey == vk)
                {
                    _swallowKey = 0;
                    return 1;
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Eats the key's release too, and taps an unused key so a held Win or Alt doesn't open Start or the menu bar.</summary>
    void Swallow(Hotkey hk)
    {
        _swallowKey = hk.Key;
        if (hk.Win || hk.Alt) Win32.Send(Win32.Key(Win32.VK_MASK, false), Win32.Key(Win32.VK_MASK, true));
    }

    uint _lastPid;
    bool _lastWasExplorer;

    /// <summary>The foreground window belongs to Explorer (a folder window or the desktop).</summary>
    bool ExplorerInFront()
    {
        Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out uint pid);
        if (pid == 0) return false;
        if (pid != _lastPid)
        {
            _lastPid = pid;
            _lastWasExplorer = string.Equals(Path.GetFileName(Win32.GetProcessPath(pid)), "explorer.exe", StringComparison.OrdinalIgnoreCase);
        }
        return _lastWasExplorer;
    }

    static Hotkey Current(int vk) => new(
        Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN), Win32.IsKeyDown(Win32.VK_CONTROL),
        Win32.IsKeyDown(Win32.VK_MENU), Win32.IsKeyDown(Win32.VK_SHIFT), vk);

    static bool ModifiersMatch(Hotkey hk)
    {
        bool win = Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN);
        bool ctrl = Win32.IsKeyDown(Win32.VK_CONTROL);
        bool alt = Win32.IsKeyDown(Win32.VK_MENU);
        bool shift = Win32.IsKeyDown(Win32.VK_SHIFT);
        return win == hk.Win && ctrl == hk.Ctrl && alt == hk.Alt && shift == hk.Shift;
    }

    public void Dispose()
    {
        if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
