using System.Diagnostics;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using SweaxMorph.Native;

namespace SweaxMorph.UI;

/// <summary>
/// How the Well sits among other windows (learned in SweaxClip): Windows keeps its own surfaces — Start, Search, Task
/// View, the notification centre — in z-order bands above every app window; administrator windows keep their keys from
/// ordinary apps; and an always-on-top window that came later can cover the Well.
/// </summary>
internal static class Layers
{
    /// <summary>The window's z-order band: 1 for ordinary windows, higher for Windows' own surfaces.</summary>
    public static uint Band(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 1;
        try { return Win32.GetWindowBand(hwnd, out uint band) ? band : 1; }
        catch (EntryPointNotFoundException) { return 1; }
    }

    public static bool IsOurs(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>"explorer / CabinetWClass / band 1" — process, window class and band, for the log (never titles).</summary>
    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "nothing";
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string name = "?";
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { }
        var cls = new StringBuilder(128);
        Win32.GetClassName(hwnd, cls, cls.Capacity);
        return $"{name} / {cls} / band {Band(hwnd)}{(IsAdminAboveUs(hwnd) ? " / administrator" : "")}";
    }

    static int? _mine;

    /// <summary>The window belongs to a process with more rights than SweaxMorph (an administrator window).</summary>
    public static bool IsAdminAboveUs(IntPtr hwnd)
    {
        _mine ??= Win32.IntegrityOf((uint)Environment.ProcessId);
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;
        int it = Win32.IntegrityOf(pid);
        return it == -1 ? _mine < 0x3000 : it > _mine;
    }

    /// <summary>
    /// See-through without a layered window: the desktop compositor blends WPF's own transparent pixels. A layered
    /// window (AllowsTransparency) has every frame copied back from the graphics card and handed over again, which at
    /// a high refresh rate, or with the card busy with a game or a live wallpaper, slows the animation to a crawl.
    /// </summary>
    public static void SeeThrough(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
        var all = new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Win32.DwmExtendFrameIntoClientArea(hwnd, ref all);
        int square = 1; // DWMWCP_DONOTROUND: the Well draws its own soft edge
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no border line
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref none, sizeof(int));
    }
}
