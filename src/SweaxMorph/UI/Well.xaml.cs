using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SweaxMorph.Core;
using SweaxMorph.Native;

namespace SweaxMorph.UI;

/// <summary>
/// The drop well: Morph in the middle, target formats blooming around it. Files dragged near open its mouth;
/// dropping on Morph swallows them (then pick a petal), dropping straight on a petal converts at once.
/// </summary>
public partial class Well : Window
{
    enum State { Idle, Picking, Working, Done, Failed }

    public enum Opening { Hotkey, Drag, Files }

    static readonly Point Heart = new(300, 258);   // Morph's centre inside the window
    const double RingRadius = 172;
    const int RingSlots = 9;

    State _state;
    List<string> _files = [];
    List<string>? _dragFiles;
    readonly List<Petal> _petals = [];
    readonly List<Petal> _garden = [];
    Petal? _hot;
    CancellationTokenSource? _cts;
    bool _moreOpen, _modal, _draggingOut, _hiding, _awake;
    // out of focus the Well rests: the haze fades so clicks reach the windows below, until a drag comes near
    bool _resting, _near, _dozing, _pressed;
    Point _pressAt, _lastCursor;
    double _stillFor;
    long _openedAt;
    readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(200) };
    string? _lastTarget;
    Engine? _fixEngine;
    double _t;
    readonly Func<double, bool> _tick;
    readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(120) };
    IntPtr _hwnd;
    // the window in front right after opening; when it changes, or a button goes down elsewhere, you have gone elsewhere
    IntPtr _frontAtOpen;
    bool _hadFocus, _pressedOutside;
    readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(7) };
    Settings Settings => App.Instance.Settings;

    /// <summary>Raised with a short status for the tray tooltip (progress while hidden).</summary>
    public event Action<string?>? Status;

    // ── bench seams: a pretend pointer, no focus taken, a look at the window when it is made ──
    internal static Func<Win32.POINT>? BenchCursor;
    internal static bool BenchQuiet;
    internal static Action<Well>? BenchCreated;
    /// <summary>The bench never gives the Well focus; with this it behaves as if it had it (no resting).</summary>
    internal static bool BenchActive;
    /// <summary>The bench's Well sits at the very bottom of the z-order, under the desktop: on screen for WPF and the compositor, seen by no one.</summary>
    internal static bool BenchBottom;
    /// <summary>The bench times the steps of an opening.</summary>
    internal static Action<string>? BenchStep;
    /// <summary>
    /// How the Well's window is see-through. Layered (the default): WPF copies each frame back from the graphics card and
    /// hands it to Windows, which also makes clear pixels let clicks through. The alternative, the compositor blending
    /// WPF's own transparent pixels (as SweaxClip's panel does), was measured here for Morph and lost: with the cloud,
    /// the zoom and the petals changing large areas every frame, it drew opening and dragging at 100–125 frames a
    /// second against 185–235 for the layered window, on top or not (2026-11-10, 240 Hz). It stays for the bench
    /// (--dwm) with its window shape code, in case a future Windows or GPU turns the result around.
    /// </summary>
    internal static bool Layered = true;

    public Well()
    {
        InitializeComponent();
        if (Layered) AllowsTransparency = true;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (!AllowsTransparency) Layers.SeeThrough(_hwnd);
            BenchCreated?.Invoke(this);
        };
        // the cloud is a handle: drag it to move Morph (and, with "where I left it", to choose where it opens)
        Haze.MouseLeftButtonDown += (_, e) => { e.Handled = true; MoveByHand(); };
        _tick = Tick;
        Morph.MouseLeftButtonUp += Morph_Click;
        Stage.MouseMove += (_, e) => Pointer(e.GetPosition(Stage), false);
        Stage.MouseLeave += (_, _) => SetHot(null);
        Stage.MouseLeftButtonUp += Stage_Click;
        _leave.Tick += (_, _) => { _leave.Stop(); if (!PointerInside()) EndPreview(); };
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); if (!IsMouseOver && !IsActive) HideWell(); };
        Deactivated += (_, _) =>
        {
            if (_modal || _draggingOut || !IsVisible) return;
            if (_state == State.Working) { HideWell(); return; } // keeps converting; Morph pops back when done
            // you went to pick a file in Explorer: Morph waits on top for it instead of leaving
            Rest(true);
            if (_state is State.Done or State.Failed) _autoHide.Start();
        };
        Activated += (_, _) => Rest(false);
        _watch.Tick += (_, _) => { if (Stirring()) Rouse(); };
        PreviewKeyDown += OnKey;
        TextInput += OnTextInput;
        QBest.Content = Loc.T("Quality.Best");
        QBalanced.Content = Loc.T("Quality.Balanced");
        QSmall.Content = Loc.T("Quality.Small");
        Combine.Content = Loc.T("Pick.Combine");
        StopButton.Content = Loc.T("Work.Cancel");
        SettingsButton.ToolTip = Loc.T("Tray.Settings");
        CloseButton.ToolTip = Loc.T("Well.Close");
        AgainButton.Content = Loc.T("Done.Again");
        SearchHint.Text = Loc.T("Pick.Search");
        Search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (_garden.FirstOrDefault(p => p.Visibility == Visibility.Visible) is { } first) Choose(first);
        };
        SetState(State.Idle);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new QuietPeer(this, "SweaxMorph");

    // ── showing and hiding ──────────────────────────────────────────────────

    /// <summary>
    /// Shows the Well where it was asked for. Already showing somewhere else (waiting on another monitor, say), it
    /// comes over instead of staying where it was.
    /// </summary>
    public void Open(Opening how, IReadOnlyList<string>? files = null)
    {
        long t0 = Stopwatch.GetTimestamp();
        if (_warming != null) { EndWarm(true); Hide(); } // a real opening in the middle of the warm-up
        _autoHide.Stop();
        _openedAt = Environment.TickCount64;
        _near = _pressed = _pressedOutside = _hadFocus = false;
        // opened in the middle of a drag (Space in Explorer) it has no focus: it rests, and the haze follows the drag
        Rest(how == Opening.Drag && Win32.IsKeyDown(Win32.VK_LBUTTON));
        bool wasVisible = IsVisible && !_hiding;
        _hiding = false;
        if (_state is State.Done or State.Failed && files == null && how != Opening.Drag) Reset();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var target = Target(how);
        BenchStep?.Invoke("start");
        if (!wasVisible || !Covers(target))
        {
            Place(hwnd, target);
            BenchStep?.Invoke("place");
            Root.Opacity = 0;
            RootScale.ScaleX = RootScale.ScaleY = 0.82;
            FitRegion(true);
            BenchStep?.Invoke("shape");
            if (!wasVisible) Show();
            BenchStep?.Invoke("show");
            Springs.To(Root, OpacityProperty, 1, 520, 0.9);
            Springs.To(RootScale, ScaleTransform.ScaleXProperty, 1, 380, 0.55);
            Springs.To(RootScale, ScaleTransform.ScaleYProperty, 1, 380, 0.55);
            Morph.Jump(0.7);
        }
        Wake();
        if (files is { Count: > 0 }) Hold(files.ToList());
        BenchStep?.Invoke("wake");
        IntPtr before = Win32.GetForegroundWindow();
        bool front = true;
        if (how != Opening.Drag && !BenchQuiet)
        {
            front = Win32.ForceForeground(hwnd);
            Activate();
            Focus();
        }
        _frontAtOpen = Win32.GetForegroundWindow();
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        if (!BenchQuiet)
        {
            if (!front) Paths.Log($"opened without focus over {Layers.Describe(before)}");
            if (ms > 120) Paths.Log($"slow open: {ms:0} ms ({how}) over {Layers.Describe(before)}");
            _ = StayOnTop();
        }
    }

    // ── warming up ──────────────────────────────────────────────────────────

    EventHandler? _warming;

    /// <summary>
    /// The first opening after start used to build the window, load the fonts and compile the code while you waited
    /// (~150 ms here). Done once at start instead, cloaked: Windows composes nothing of a cloaked window, but WPF
    /// draws it exactly as on screen, so every surface is ready.
    /// </summary>
    internal void Warm(Action done)
    {
        if (IsVisible) { done(); return; }
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Cloak(hwnd, true);
        Place(hwnd, Pointer());
        Root.Opacity = 1;
        FitRegion(true);
        Show();
        long until = Environment.TickCount64 + 300;
        _warming = (_, _) =>
        {
            if (Environment.TickCount64 < until) return;
            EndWarm(false);
            done();
        };
        CompositionTarget.Rendering += _warming;
    }

    /// <summary>Ends the warm-up; <paramref name="stay"/> when a real opening arrived in the middle of it.</summary>
    void EndWarm(bool stay)
    {
        if (_warming == null) return;
        CompositionTarget.Rendering -= _warming;
        _warming = null;
        if (!stay) Hide();
        Cloak(_hwnd, false);
        if (!stay) Reset();
    }

    static void Cloak(IntPtr hwnd, bool on)
    {
        int v = on ? 1 : 0;
        Win32.DwmSetWindowAttribute(hwnd, 13, ref v, sizeof(int)); // DWMWA_CLOAK
    }

    /// <summary>The pointer, or the pretend one of the bench.</summary>
    static Win32.POINT Pointer()
    {
        if (BenchCursor != null) return BenchCursor();
        Win32.GetCursorPos(out var pt);
        return pt;
    }

    /// <summary>Where Morph's centre should be for this kind of opening, in screen pixels.</summary>
    Win32.POINT Target(Opening how)
    {
        var pt = Pointer();
        // carried files and a right-click always meet Morph at the pointer; the shortcut follows the setting
        if (how != Opening.Hotkey) return pt;
        switch (Settings.Placement)
        {
            case "center": return MiddleOf(pt);
            case "remember":
                if (Settings.WellX is int x && Settings.WellY is int y)
                {
                    var spot = new Win32.POINT { X = x, Y = y };
                    if (Win32.MonitorFromPoint(spot, Win32.MONITOR_DEFAULTTONULL) != IntPtr.Zero) return spot; // that monitor is still there
                }
                return MiddleOf(pt);
            default: return pt;
        }
    }

    static Win32.POINT MiddleOf(Win32.POINT pt)
    {
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST), ref mi);
        var w = mi.rcWork;
        return new Win32.POINT { X = (w.Left + w.Right) / 2, Y = (w.Top + w.Bottom) / 2 };
    }

    /// <summary>The Well already stands at <paramref name="target"/>: the pointer is on it, or Morph is near the spot.</summary>
    bool Covers(Win32.POINT target)
    {
        if (_hwnd == IntPtr.Zero || !Win32.GetWindowRect(_hwnd, out var r)) return false;
        var pt = Pointer();
        if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom) return true;
        double k = Win32.GetDpiForWindow(_hwnd) / 96.0;
        double hx = r.Left + Heart.X * k - target.X, hy = r.Top + Heart.Y * k - target.Y;
        return hx * hx + hy * hy < 90 * 90 * k * k;
    }

    /// <summary>
    /// For the shortcut: is the Well where you are? Then the shortcut puts it away. Waiting on another monitor, or far
    /// from the pointer while you were elsewhere, it is not — the shortcut brings it over instead of hiding it.
    /// </summary>
    public bool IsHere()
    {
        if (!Showing || _hwnd == IntPtr.Zero) return false;
        var pt = Pointer();
        if (Win32.GetWindowRect(_hwnd, out var r) && pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom) return true;
        if (Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST) != Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST)) return false;
        // on this monitor: at its own spot (centre / where you left it), or in use right now
        return Settings.Placement != "cursor" ? Covers(Target(Opening.Hotkey)) : IsActive;
    }

    /// <summary>Another always-on-top window that came later (a game, a video, an overlay) may cover Morph: raise it once and say what it was.</summary>
    async Task StayOnTop()
    {
        await Task.Delay(90);
        if (!IsVisible || _hwnd == IntPtr.Zero || PresentationSource.FromVisual(Morph) == null) return;
        var c = Morph.PointToScreen(new Point(Morph.ActualWidth / 2, Morph.ActualHeight / 2));
        IntPtr hit = Win32.GetAncestor(Win32.WindowFromPoint(new Win32.POINT { X = (int)c.X, Y = (int)c.Y }), Win32.GA_ROOT);
        if (hit == IntPtr.Zero || hit == _hwnd || Layers.IsOurs(hit)) return;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Paths.Log($"Morph was covered by {Layers.Describe(hit)}; raised it");
    }

    /// <summary>The cloud dragged by hand: the Well moves, and the spot is kept for "where I left it".</summary>
    void MoveByHand()
    {
        if (_hwnd == IntPtr.Zero || !Win32.IsKeyDown(Win32.VK_LBUTTON)) return;
        Win32.GetWindowRect(_hwnd, out var before);
        bool wasModal = _modal;
        _modal = true;
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally { _modal = wasModal; }
        Win32.GetWindowRect(_hwnd, out var after);
        if (Math.Abs(after.Left - before.Left) + Math.Abs(after.Top - before.Top) < 4) return;
        double k = Win32.GetDpiForWindow(_hwnd) / 96.0;
        Settings.WellX = (int)Math.Round(after.Left + Heart.X * k);
        Settings.WellY = (int)Math.Round(after.Top + Heart.Y * k);
        Settings.Save();
    }

    public void HideWell()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        _autoHide.Stop();
        Springs.To(Root, OpacityProperty, 0, 900, 1);
        Springs.To(RootScale, ScaleTransform.ScaleXProperty, 0.9, 700, 1);
        Springs.To(RootScale, ScaleTransform.ScaleYProperty, 0.9, 700, 1);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (!_hiding) return;
            _hiding = false;
            Hide();
            _resting = _near = false;
            Rouse();
            if (_moreOpen) CloseMore(false);
            if (_state is State.Picking or State.Idle) Reset();
        };
        t.Start();
    }

    public bool Showing => IsVisible && !_hiding;

    // ── the window's shape: what catches the mouse ──────────────────────────

    readonly List<int> _shape = [], _shapeNew = [];
    long _shapeAt;
    // the bench counts how often the window is reshaped and what it costs
    internal static int BenchRegionSets;
    internal static double BenchRegionMs;

    /// <summary>
    /// A window that is not layered takes every click on its rectangle, transparent or not. So the window is cut to
    /// what is drawn: the cloud while it shows (an ellipse), and always Morph, the petals, the words and the buttons
    /// (rectangles around what each of them draws, so nothing that moves is ever cut). Resting, with the cloud faded,
    /// only those catch the mouse and the windows below can be clicked — as with the old layered window.
    /// Reshaping a window is not free, so it happens only when something reaches past the shape (at once) or after
    /// the shape could shrink for a while (at most every 400 ms); the opening and closing zoom, which only ever makes
    /// the Well smaller than its full size, is measured at full size and never reshapes it.
    /// </summary>
    void FitRegion(bool force = false)
    {
        if (_hwnd == IntPtr.Zero || AllowsTransparency) return; // a layered window lets clicks through its clear pixels by itself
        long t0 = Stopwatch.GetTimestamp();
        double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _shapeNew.Clear();
        _shapeNew.Add((int)Math.Round(k * 100));
        bool cloud = !_resting || _near || Haze.Opacity >= 0.04;
        if (!Root.IsDescendantOf(this))
        {
            // before the first layout the parts have no place yet: the whole cloud and the two buttons
            Add(1, new Rect(-12, -2, 624, 714), k);
            Add(0, new Rect(490, 28, 84, 44), k);
        }
        else
        {
            // in the Well's own coordinates (the window's, at full size: Root sits at its corner)
            if (cloud) Add(1, new Rect(-12, -2, 624, 714), k);
            Part(Morph, 6, k);
            foreach (var p in _petals) if (p.Opacity > 0.01) Part(p, 3, k);
            Part(Count, 12, k);
            Part(SettingsButton, 3, k);
            Part(CloseButton, 3, k);
            if (Info.Opacity > 0.01)
                foreach (var child in Info.Children) if (child is FrameworkElement fe) Part(fe, 3, k);
            if (MoreSheet.IsVisible && MoreSheet.Opacity > 0.01)
                Add(0, MoreSheet.TransformToAncestor(Root).TransformBounds(new Rect(MoreSheet.RenderSize)), k);
        }
        if (!force)
        {
            if (_shapeNew.SequenceEqual(_shape)) return;
            if (Inside(_shapeNew, _shape) && Environment.TickCount64 - _shapeAt < 400) return;
        }
        _shape.Clear();
        _shape.AddRange(_shapeNew);
        _shapeAt = Environment.TickCount64;
        IntPtr all = Win32.CreateRectRgn(0, 0, 0, 0);
        for (int i = 1; i + 4 < _shape.Count; i += 5)
        {
            IntPtr one = _shape[i] == 1 ? Win32.CreateEllipticRgn(_shape[i + 1], _shape[i + 2], _shape[i + 3], _shape[i + 4])
                                        : Win32.CreateRectRgn(_shape[i + 1], _shape[i + 2], _shape[i + 3], _shape[i + 4]);
            Win32.CombineRgn(all, all, one, Win32.RGN_OR);
            Win32.DeleteObject(one);
        }
        // no repaint asked: WPF draws its own frames, and a forced repaint would redraw the whole window
        if (Win32.SetWindowRgn(_hwnd, all, false) == 0) Win32.DeleteObject(all); // on success Windows owns the region
        BenchRegionSets++;
        BenchRegionMs += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>Every piece of <paramref name="next"/> lies inside a piece of <paramref name="now"/> (a rectangle inside the cloud counts).</summary>
    static bool Inside(List<int> next, List<int> now)
    {
        if (now.Count == 0 || next[0] != now[0]) return false;
        for (int i = 1; i + 4 < next.Count; i += 5)
        {
            bool inside = false;
            for (int j = 1; j + 4 < now.Count && !inside; j += 5)
            {
                if (now[j] == 1 && next[i] == 0)
                {
                    // a rectangle inside an ellipse: its four corners are
                    double cx = (now[j + 1] + now[j + 3]) / 2.0, cy = (now[j + 2] + now[j + 4]) / 2.0;
                    double rx = (now[j + 3] - now[j + 1]) / 2.0, ry = (now[j + 4] - now[j + 2]) / 2.0;
                    bool In(int x, int y) => (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1;
                    inside = In(next[i + 1], next[i + 2]) && In(next[i + 3], next[i + 2]) && In(next[i + 1], next[i + 4]) && In(next[i + 3], next[i + 4]);
                }
                else if (now[j] == next[i] || now[j] == 0)
                    inside = now[j + 1] <= next[i + 1] && now[j + 2] <= next[i + 2] && now[j + 3] >= next[i + 3] && now[j + 4] >= next[i + 4];
            }
            if (!inside) return false;
        }
        return true;
    }

    void Part(FrameworkElement e, double pad, double k)
    {
        if (!e.IsVisible || e.Opacity < 0.01 || !e.IsDescendantOf(Root)) return;
        var b = VisualTreeHelper.GetDescendantBounds(e);
        if (b.IsEmpty) return;
        var r = e.TransformToAncestor(Root).TransformBounds(b);
        r.Inflate(pad, pad);
        Add(0, r, k);
    }

    /// <summary>A rectangle (0) or an ellipse (1) in device pixels, rounded outward to 8 px so small motions don't reshape the window.</summary>
    void Add(int kind, Rect r, double k)
    {
        _shapeNew.Add(kind);
        _shapeNew.Add((int)Math.Floor(r.Left * k / 8) * 8);
        _shapeNew.Add((int)Math.Floor(r.Top * k / 8) * 8);
        _shapeNew.Add((int)Math.Ceiling(r.Right * k / 8) * 8);
        _shapeNew.Add((int)Math.Ceiling(r.Bottom * k / 8) * 8);
    }

    /// <summary>For the bench: as if you had clicked somewhere else.</summary>
    internal void BenchElsewhere() => _pressedOutside = true;

    // ── resting while you are elsewhere ─────────────────────────────────────

    /// <summary>
    /// Out of focus the Well stays on top, waiting for the file you went to pick, but its haze fades so the
    /// windows under it can be clicked; only Morph, the petals and the words still catch the mouse.
    /// </summary>
    void Rest(bool on)
    {
        _resting = on;
        if (!on) Rouse();
        UpdateHaze();
    }

    /// <summary>The haze is there while the Well has focus, and while a drag comes near so the whole cloud catches it.</summary>
    void UpdateHaze()
    {
        bool show = !_resting || _near;
        Springs.To(Haze, OpacityProperty, show ? 1 : 0, show ? 320 : 220, 1);
    }

    /// <summary>Per frame while resting: notice a drag on its way in, and doze off when nothing happens.</summary>
    void Watch(Point p)
    {
        bool down = Win32.IsKeyDown(Win32.VK_LBUTTON);
        if (down && !_pressed) _pressAt = p;
        _pressed = down;
        // a press that moves is a drag (a plain click on a file under the cloud must not bring it back)
        bool near = down && (p - _pressAt).Length > 8 && (p - Heart).Length < 340;
        if (near != _near) { _near = near; UpdateHaze(); }
    }

    /// <summary>The pointer moved near the Well, or a button went down anywhere: time to wake.</summary>
    bool Stirring()
    {
        if (!Win32.GetCursorPos(out var pt) || PresentationSource.FromVisual(this) == null) return false;
        var p = PointFromScreen(new Point(pt.X, pt.Y));
        bool moved = (p - _lastCursor).Length > 2;
        _lastCursor = p;
        return Win32.IsKeyDown(Win32.VK_LBUTTON) || (moved && (p - Heart).Length < 520);
    }

    /// <summary>Nothing has stirred for a while: Morph closes its eyes and the Well stops drawing frames.</summary>
    void Doze()
    {
        if (_dozing) return;
        _dozing = true;
        Morph.Dozing = true;
        Stirring();
        _watch.Start();
    }

    void Rouse()
    {
        _stillFor = 0;
        if (!_dozing) return;
        _dozing = false;
        _watch.Stop();
        Morph.Dozing = false;
        if (IsVisible) Wake();
    }

    public bool Busy => _state == State.Working;

    // ── recordings drive the same code paths by hand ────────────────────────

    internal bool Demo { get; set; }
    internal void DemoShow()
    {
        Demo = true;
        Left = -32000; Top = -32000;
        Show();
        Root.Opacity = 1;
        Wake();
    }
    internal void DemoCarry(List<string> files, Point p) => Carry(files, p);
    internal void DemoLand(List<string> files, Point p) => Land(files, p);
    internal void DemoPoint(Point p) { Pointer(p, false); if (_hot == null) Morph.LookAt(Stage.TranslatePoint(p, Morph)); }
    internal void DemoLeave() => EndPreview();
    internal Point PetalPoint(string target) => _petals.FirstOrDefault(x => x.Target == target) is { } x
        ? new Point(x.Origin.X + Math.Cos(x.Angle) * x.Ring, x.Origin.Y + Math.Sin(x.Angle) * x.Ring) : Heart;
    internal void DemoChoose(string target) { if (_petals.FirstOrDefault(x => x.Target == target) is { } p) Choose(p); }
    internal void DemoMore(string search) => OpenMore(search);
    internal void DemoHold(List<string> files) => Hold(files);
    internal void DemoBounce() { Morph.Jump(0.45); Morph.Sparkles(5); }
    internal bool Working => _state == State.Working;
    internal static Point Center => Heart;
    internal FrameworkElement Surface => Root;

    /// <summary>
    /// Puts Morph's centre on <paramref name="target"/>, keeping Morph, the words and the buttons on that monitor.
    /// Moving to a monitor with another scale, the window first lands there so WPF takes that monitor's DPI; only then
    /// is the final spot worked out, in that monitor's pixels.
    /// </summary>
    void Place(IntPtr hwnd, Win32.POINT target)
    {
        var mon = Win32.MonitorFromPoint(target, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(mon, ref mi);
        uint want = Win32.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 ? dpi : 96;
        if (Win32.GetDpiForWindow(hwnd) != want)
            Win32.SetWindowPos(hwnd, IntPtr.Zero, mi.rcWork.Left, mi.rcWork.Top, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        double k = (Win32.GetDpiForWindow(hwnd) is uint now and > 0 ? now : want) / 96.0;
        int w = (int)Math.Round(Width * k), h = (int)Math.Round(Height * k);
        int x = target.X - (int)(Heart.X * k), y = target.Y - (int)(Heart.Y * k);
        var work = mi.rcWork;
        // the haze may run off the edge; Morph, the text and the settings and close buttons may not
        x = Math.Clamp(x, work.Left - (int)(40 * k), work.Right - w + (int)(28 * k));
        y = Math.Clamp(y, work.Top - (int)(30 * k), work.Bottom - h + (int)(20 * k));
        Win32.SetWindowPos(hwnd, BenchBottom ? Win32.HWND_BOTTOM : Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
    }

    void Wake()
    {
        if (_awake) return;
        _awake = true;
        Springs.Run(_tick);
    }

    /// <summary>Per frame while visible: petals float, Morph follows the pointer anywhere on screen, the search wave flows.</summary>
    bool Tick(double dt)
    {
        if (!IsVisible || _dozing) { _awake = false; return false; }
        _t += dt;
        for (int i = 0; i < _petals.Count; i++)
        {
            _petals[i].Bob = Math.Sin(_t * 1.9 + i * 0.8) * 2.6;
            _petals[i].Place();
        }
        if (!_draggingOut && !Demo && Win32.GetCursorPos(out var pt) && PresentationSource.FromVisual(this) != null)
        {
            var p = PointFromScreen(new Point(pt.X, pt.Y));
            if (_hot == null) Morph.LookAt(TranslatePoint(p, Morph));
            // resting follows where you are. Focus alone can't tell: Windows may show the Well without giving it focus,
            // and then no Deactivated ever comes. So: it had focus and lost it, or the window in front changed, or a
            // button went down somewhere else. Opened without focus and left alone, it keeps waiting with its cloud.
            if (_state is State.Idle or State.Picking && !_modal && Environment.TickCount64 - _openedAt > 600)
            {
                if (IsActive) _hadFocus = true;
                if (!_pressedOutside && (Win32.IsKeyDown(Win32.VK_LBUTTON) || Win32.IsKeyDown(0x02)))
                {
                    IntPtr under = Win32.GetAncestor(Win32.WindowFromPoint(pt), Win32.GA_ROOT);
                    if (under != _hwnd && !Layers.IsOurs(under)) _pressedOutside = true;
                }
                IntPtr front = Win32.GetForegroundWindow();
                // and if nothing at all happens for a while (you walked away), it rests too, and dozes off after that
                bool elsewhere = _hadFocus || _pressedOutside || (front != _frontAtOpen && front != _hwnd && !Layers.IsOurs(front))
                    || (!BenchActive && Environment.TickCount64 - _openedAt > 20_000 && Win32.IdleMs() > 20_000);
                bool rest = !(IsActive || BenchActive) && elsewhere;
                if (rest != _resting) Rest(rest);
            }
            if (_resting)
            {
                Watch(p);
                _stillFor = Stirring() ? 0 : _stillFor + dt;
                if (_stillFor > 8 && !_near && _state != State.Working) Doze();
            }
        }
        if (_moreOpen) Wave.Time = _t;
        FitRegion();
        return true;
    }

    // ── states ──────────────────────────────────────────────────────────────

    void Reset()
    {
        _cts?.Cancel();
        _files = [];
        _dragFiles = null;
        Morph.TintDefault();
        Morph.ClearFill();
        Morph.Mood = Mood.Idle;
        ClearPetals();
        SetState(State.Idle);
    }

    void SetState(State s)
    {
        _state = s;
        Options.Visibility = s == State.Picking ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = s == State.Working ? Visibility.Visible : Visibility.Collapsed;
        Results.Visibility = s is State.Done ? Visibility.Visible : Visibility.Collapsed;
        DoneActions.Visibility = s is State.Done or State.Failed ? Visibility.Visible : Visibility.Collapsed;
        FixButton.Visibility = s == State.Failed && _fixEngine != null ? Visibility.Visible : Visibility.Collapsed;
        AgainButton.Visibility = s is State.Done or State.Failed && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Count.Visibility = s is State.Picking or State.Working && _files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _files.Count.ToString();
        switch (s)
        {
            case State.Idle:
                TitleText.Text = Loc.T("Drop.Title");
                SubText.Text = Loc.T("Drop.Hint", Hotkey.Parse(Settings.Hotkey).ToString());
                break;
            case State.Picking:
                TitleText.Text = Loc.T("Pick.Title", Subject());
                SubText.Text = _petals.Count == 0 ? Loc.T("Pick.NoTargets") : Summary();
                SyncOptions();
                break;
        }
        Info.Opacity = _moreOpen ? 0 : 1;
    }

    string Subject() => _files.Count == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(_files[0])) : Loc.T("Drop.Many", _files.Count);

    string Summary()
    {
        long size = _files.Sum(Job.SizeOf);
        var kinds = _files.Select(f => Formats.Of(f)).Where(f => f != null).Select(f => f!.Kind).Distinct().ToList();
        string what = kinds.Count == 1 ? Formats.KindName(kinds[0]) : Loc.T("Drop.Mixed", _files.Count, kinds.Count);
        return $"{what} · {Loc.Size(size)}";
    }

    void SyncOptions()
    {
        QBest.IsChecked = Settings.Quality == Quality.Best;
        QBalanced.IsChecked = Settings.Quality == Quality.Balanced;
        QSmall.IsChecked = Settings.Quality == Quality.Small;
        bool canCombine = _files.Count > 1 && Planner.CombineTargets(Ids(_files)).Any();
        Combine.Visibility = OptionsGap.Visibility = canCombine ? Visibility.Visible : Visibility.Collapsed;
        if (!canCombine) Combine.IsChecked = false;
    }

    static List<string> Ids(IEnumerable<string> files) => files.Select(f => Formats.Of(f)?.Id).Where(i => i != null).Select(i => i!).ToList();

    /// <summary>Morph swallows the files and the petals bloom.</summary>
    void Hold(List<string> files)
    {
        _cts?.Cancel();
        _files = files;
        _dragFiles = null;
        Combine.IsChecked = false;
        Morph.Swallow();
        Morph.ClearFill();
        Morph.Mood = Mood.Full;
        TintFor(files);
        ShowPetals(Targets(files, false));
        SetState(State.Picking);
    }

    void TintFor(IEnumerable<string> files)
    {
        var kinds = files.Select(f => Formats.Of(f)?.Kind).Where(k => k != null).Distinct().ToList();
        if (kinds.Count == 1) Morph.Tint(Formats.Hue(kinds[0]!.Value));
        else Morph.TintDefault();
    }

    // ── targets ─────────────────────────────────────────────────────────────

    sealed record Pick(string Id, string Label, string Hue, Route? Route, string? Glyph = null, string? Note = null);

    static readonly Dictionary<Kind, string[]> Favourites = new()
    {
        [Kind.Image] = ["png", "jpg", "webp", "pdf", "avif", "ico", "gif", "txt"],
        [Kind.Video] = ["mp4", "mp3", "gif", "webm", "mov", "mkv", "m4a", "wav", "jpg"],
        [Kind.Audio] = ["mp3", "wav", "flac", "m4a", "ogg", "opus", "mp4", "m4r"],
        [Kind.Document] = ["pdf", "docx", "txt", "md", "html", "odt", "rtf", "epub"],
        [Kind.Sheet] = ["xlsx", "csv", "pdf", "json", "ods", "xls", "html", "md"],
        [Kind.Slides] = ["pdf", "pptx", "png", "mp4", "jpg", "odp", "ppt"],
        [Kind.Pdf] = ["docx", "png", "jpg", "txt", "epub", "html"],
        [Kind.Ebook] = ["epub", "pdf", "docx", "mobi", "azw3", "txt", "html"],
        [Kind.Data] = ["json", "yaml", "csv", "xlsx", "xml", "toml"],
        [Kind.Archive] = ["folder", "zip", "7z", "tgz"],
        [Kind.Subtitle] = ["srt", "vtt", "ass", "lrc", "ttml"],
        [Kind.Font] = ["woff2", "woff", "ttf", "otf"],
        [Kind.Folder] = ["zip", "7z", "tgz"],
    };

    /// <summary>Everything these files can become together, best first. Specials (shrink, unpack) lead.</summary>
    List<Pick> Targets(IReadOnlyList<string> files, bool all)
    {
        var ids = Ids(files);
        if (ids.Count == 0) return [];
        var distinct = ids.Distinct().ToList();
        var picks = new List<Pick>();

        if (Combine.IsChecked == true && files.Count > 1)
        {
            foreach (var t in Planner.CombineTargets(distinct))
                if (Formats.Find(t) is { } f) picks.Add(new Pick(t, f.Label, Formats.Hue(f.Kind), null));
            return Order(picks, distinct);
        }

        Dictionary<string, Route>? common = null;
        foreach (var id in distinct)
        {
            var t = Planner.Targets(id);
            if (common == null) common = new(t);
            else foreach (var k in common.Keys.ToList()) if (!t.ContainsKey(k)) common.Remove(k);
        }
        foreach (var (id, route) in common!)
        {
            var f = Formats.Get(id);
            if (id == "folder") picks.Add(new Pick(id, Loc.T("Pick.Extract"), Formats.Hue(Kind.Archive), route, ""));
            else picks.Add(new Pick(id, f.Label, Formats.Hue(f.Kind), route));
        }
        picks = Order(picks, distinct);
        if (distinct.Count == 1 && Planner.Shrink(distinct[0]) is { } shrink)
            picks.Insert(0, new Pick(distinct[0], Loc.T("Pick.Shrink"), "#2EC4B6", new Route(Formats.Get(distinct[0]), [shrink], shrink.Edge.Cost), "", Loc.T("Pick.ShrinkHint")));
        return picks;
    }

    List<Pick> Order(List<Pick> picks, List<string> from)
    {
        var kind = Formats.Get(from[0]).Kind;
        var fav = Favourites.TryGetValue(kind, out var f) ? f : [];
        return picks
            .OrderBy(p => p.Id == "folder" ? -1 : Array.IndexOf(fav, p.Id) is int i && i >= 0 ? i : 100)
            .ThenByDescending(p => Settings.Used.GetValueOrDefault(p.Id))
            .ThenBy(p => p.Route?.Cost ?? 0)
            .ThenBy(p => Formats.Get(p.Id).Kind == kind ? 0 : 1)
            .ToList();
    }

    static string? BadgeFor(Route? r) => r?.Missing is { } e ? e.Installable ? "↓" : "lock" : null;

    void ShowPetals(List<Pick> picks)
    {
        var ring = picks.Take(picks.Count > RingSlots ? RingSlots - 1 : RingSlots).ToList();
        bool more = picks.Count > ring.Count;
        // the same ring is already out (the files were previewed while dragging): keep it, don't re-bloom
        var ids = ring.Select(p => p.Id + p.Label).Append(more ? "more" : "").ToList();
        var current = _petals.Select(p => p.Target == "more" ? "more" : p.Target + p.Label).ToList();
        if (!more) current.Add("");
        if (_petals.Count > 0 && ids.SequenceEqual(current) && _petals.All(p => p.Pop > 0.5)) return;
        ClearPetals();
        int n = ring.Count + (more ? 1 : 0);
        for (int i = 0; i < n; i++)
        {
            bool isMore = more && i == n - 1;
            var pick = isMore ? null : ring[i];
            var petal = new Petal
            {
                Target = isMore ? "more" : pick!.Id,
                Label = isMore ? Loc.T("Pick.More") : pick!.Label,
                Hue = isMore ? Theme.MoreHue : Tint(pick!.Hue, i),
                Glyph = isMore ? "" : pick!.Glyph,
                Badge = isMore ? null : BadgeFor(pick!.Route),
                Route = pick?.Route,
                Origin = Heart,
                Angle = -Math.PI / 2 + 2 * Math.PI * i / n,
                Ring = RingRadius,
                Tag = pick,
            };
            petal.MouseEnter += (_, _) => SetHot(petal);
            petal.MouseLeave += (_, _) => { if (_hot == petal) SetHot(null); };
            Petals.Children.Add(petal);
            _petals.Add(petal);
            petal.Place();
            Springs.To(petal, Petal.PopProperty, 1, 260, 0.55, from: 0, delay: 0.03 * i);
        }
        Wake();
    }

    /// <summary>Neighbouring petals of the same colour get slightly different shades, so the ring has rhythm.</summary>
    static string Tint(string hue, int i)
    {
        var c = Blob.Mix(Blob.Hex(hue), Colors.White, i % 3 == 1 ? 0.14 : i % 3 == 2 ? 0.06 : 0);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    void ClearPetals()
    {
        _hot = null;
        foreach (var p in _petals.ToList())
        {
            var petal = p;
            Springs.To(petal, Petal.PopProperty, 0, 500, 1);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            t.Tick += (_, _) => { t.Stop(); Petals.Children.Remove(petal); };
            t.Start();
        }
        _petals.Clear();
    }

    void SetHot(Petal? p)
    {
        if (_hot == p) return;
        if (_hot != null) Springs.To(_hot, Petal.HoverProperty, 0, 300, 0.6);
        _hot = p;
        if (p != null)
        {
            Springs.To(p, Petal.HoverProperty, 1, 420, 0.45);
            Morph.LookAt(TranslatePoint(new Point(Heart.X + Math.Cos(p.Angle) * RingRadius, Heart.Y + Math.Sin(p.Angle) * RingRadius), Morph));
        }
        if (_state == State.Picking && !_moreOpen) SubText.Text = p == null ? Summary() : Describe(p);
    }

    string Describe(Petal p)
    {
        if (p.Target == "more") return Loc.T("Pick.Search");
        var pick = p.Tag as Pick;
        if (pick?.Note != null) return pick.Note;
        var f = Formats.Get(p.Target);
        string engine = p.Route is { } r ? string.Join(" → ", r.Hops.Select(h => h.Converter.Name).Distinct()) : "";
        string needs = p.Route?.Missing is { } e ? " · " + (e.Installable ? Loc.T("Pick.Download", e.Name, Loc.Size(e.ApproxBytes)) : Loc.T("Pick.Needs", e.Name)) : "";
        return $"{f.Description}{(engine.Length > 0 ? " · " + engine : "")}{needs}";
    }

    // ── pointer and drag & drop ─────────────────────────────────────────────

    Petal? PetalAt(Point p) => _petals.Where(x => x.Pop > 0.6).FirstOrDefault(x =>
    {
        var c = new Point(x.Origin.X + Math.Cos(x.Angle) * x.Ring, x.Origin.Y + Math.Sin(x.Angle) * x.Ring);
        return (p - c).Length < Petal.Size / 2 + 4;
    });

    void Pointer(Point p, bool dragging)
    {
        var petal = PetalAt(p);
        SetHot(petal);
        double d = (p - Heart).Length;
        if (dragging)
        {
            double reach = petal == null ? Math.Clamp(1 - (d - 70) / 170, 0, 1) : 0;
            Morph.Reach(TranslatePoint(p, Morph), reach);
            Morph.Mood = petal == null && d < 230 ? Mood.Hungry : _state == State.Picking ? Mood.Full : Mood.Idle;
            TitleText.Text = petal != null ? Loc.T("Pick.Title", petal.Label) : d < 230 ? Loc.T("Drop.Over") : _state == State.Picking ? Loc.T("Pick.Title", Subject()) : Loc.T("Drop.Title");
            SubText.Text = petal != null ? Describe(petal) : Loc.T("Drop.Petal");
        }
        else Morph.Hover(d < 80 && _state is State.Idle or State.Picking);
    }

    static List<string>? Dropped(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 ? files.ToList() : null;

    protected override void OnDragEnter(DragEventArgs e) => Dragging(e);
    protected override void OnDragOver(DragEventArgs e) => Dragging(e);

    void Dragging(DragEventArgs e)
    {
        e.Handled = true;
        _leave.Stop();
        Rouse();
        var files = Dropped(e);
        if (files == null || _draggingOut || _state == State.Working) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        if (_moreOpen) return;
        Carry(files, e.GetPosition(Stage));
    }

    /// <summary>Files are being carried over the well at <paramref name="p"/>.</summary>
    void Carry(List<string> files, Point p)
    {
        if (_dragFiles == null || !_dragFiles.SequenceEqual(files))
        {
            _dragFiles = files;
            if (!files.SequenceEqual(_files))
            {
                TintFor(files);
                ShowPetals(Targets(files, false));
            }
        }
        Pointer(p, true);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        e.Handled = true;
        _leave.Start();
    }

    bool PointerInside()
    {
        if (!Win32.GetCursorPos(out var pt)) return false;
        var p = PointFromScreen(new Point(pt.X, pt.Y));
        return p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight;
    }

    /// <summary>The files went away without being dropped: back to what Morph held before.</summary>
    void EndPreview()
    {
        if (_dragFiles == null) return;
        _dragFiles = null;
        Morph.Reach(Heart, 0);
        if (_state == State.Picking)
        {
            Morph.Mood = Mood.Full;
            TintFor(_files);
            ShowPetals(Targets(_files, false));
            SetState(State.Picking);
        }
        else
        {
            Morph.Mood = Mood.Idle;
            Morph.TintDefault();
            ClearPetals();
            SetState(_state);
        }
    }

    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        _leave.Stop();
        var files = Dropped(e);
        if (files == null || _draggingOut || _state == State.Working) return;
        Win32.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        Land(files, e.GetPosition(Stage));
    }

    /// <summary>Files let go at <paramref name="p"/>: on a petal they convert at once, anywhere else Morph eats them.</summary>
    void Land(List<string> files, Point p)
    {
        var petal = PetalAt(p);
        Morph.Reach(Heart, 0);
        _dragFiles = null;
        if (petal != null && petal.Target != "more")
        {
            _files = files;
            Morph.Swallow();
            TintFor(files);
            Choose(petal);
        }
        else
        {
            Hold(files);
            if (petal?.Target == "more") OpenMore(null);
        }
    }

    // ── clicks and keys ─────────────────────────────────────────────────────

    void Morph_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_moreOpen) { CloseMore(true); return; }
        switch (_state)
        {
            case State.Idle: Browse(); break;
            case State.Done or State.Failed: Reset(); Morph.Jump(0.5); break;
            default: Morph.Poke(-Math.PI / 2 + (_rnd.NextDouble() - 0.5), 1.4); Morph.Jump(0.35); break;
        }
    }

    readonly Random _rnd = new();

    void Stage_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (PetalAt(e.GetPosition(Stage)) is { } petal) { e.Handled = true; Choose(petal); }
    }

    void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "SweaxMorph" };
        _modal = true;
        try { if (dlg.ShowDialog(this) == true) Hold(dlg.FileNames.ToList()); }
        finally { _modal = false; Activate(); }
    }

    bool Choose(Petal petal)
    {
        if (_state == State.Working) return false;
        switch (petal.Target)
        {
            case "more": OpenMore(null); return true;
            default:
                if (_files.Count == 0) return false;
                if (_moreOpen) CloseMore(false);
                Start(petal.Target, petal.Route);
                return true;
        }
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_moreOpen) CloseMore(true);
            else if (_state == State.Working) HideWell();
            else HideWell();
        }
    }

    void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        // just start typing a format's name
        if (_state == State.Picking && !_moreOpen && e.Text.Length == 1 && char.IsLetterOrDigit(e.Text[0])) OpenMore(e.Text);
    }

    void Quality_Click(object sender, RoutedEventArgs e)
    {
        Settings.Quality = sender == QBest ? Quality.Best : sender == QSmall ? Quality.Small : Quality.Balanced;
        Settings.Save();
        SyncOptions();
    }

    void Combine_Click(object sender, RoutedEventArgs e)
    {
        ShowPetals(Targets(_files, false));
        SubText.Text = Summary();
    }

    // ── the garden: every format ────────────────────────────────────────────

    void OpenMore(string? search)
    {
        if (_files.Count == 0) return;
        _moreOpen = true;
        foreach (var p in _petals) Springs.To(p, Petal.PopProperty, 0, 420, 0.9);
        Springs.To(MorphScale, ScaleTransform.ScaleXProperty, 0.42, 300, 0.7);
        Springs.To(MorphScale, ScaleTransform.ScaleYProperty, 0.42, 300, 0.7);
        Springs.To(MorphMove, TranslateTransform.YProperty, -178, 260, 0.75);
        Springs.To(Info, OpacityProperty, 0, 500, 1);
        Info.IsHitTestVisible = false;
        Count.Visibility = Visibility.Collapsed;
        BuildGarden();
        MoreSheet.Visibility = Visibility.Visible;
        MoreSheet.Opacity = 0;
        Springs.To(MoreSheet, OpacityProperty, 1, 300, 0.9, delay: 0.06);
        Search.Text = search ?? "";
        Search.CaretIndex = Search.Text.Length;
        Search.Focus();
        Wake();
    }

    void CloseMore(bool bringBack)
    {
        _moreOpen = false;
        MoreSheet.Visibility = Visibility.Collapsed;
        Springs.To(MorphScale, ScaleTransform.ScaleXProperty, 1, 300, 0.6);
        Springs.To(MorphScale, ScaleTransform.ScaleYProperty, 1, 300, 0.6);
        Springs.To(MorphMove, TranslateTransform.YProperty, 0, 260, 0.7);
        Info.IsHitTestVisible = true;
        Springs.To(Info, OpacityProperty, 1, 300, 0.9);
        if (bringBack && _state == State.Picking)
            for (int i = 0; i < _petals.Count; i++) Springs.To(_petals[i], Petal.PopProperty, 1, 260, 0.55, delay: 0.03 * i);
        Focus();
    }

    void BuildGarden()
    {
        Garden.Children.Clear();
        _garden.Clear();
        var picks = Targets(_files, true);
        var groups = picks.GroupBy(p => p.Id == "folder" ? Kind.Archive : Formats.Get(p.Id).Kind);
        foreach (var group in groups)
        {
            var header = new TextBlock
            {
                Text = Formats.KindName(group.Key).ToUpper(Loc.Turkish ? new System.Globalization.CultureInfo("tr-TR") : System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11.5, FontWeight = FontWeights.Black, Foreground = Theme.Brush("Ink3"), Margin = new Thickness(8, 10, 0, 2),
            };
            var wrap = new WrapPanel();
            int i = 0;
            foreach (var pick in group)
            {
                var petal = new Petal
                {
                    Target = pick.Id, Label = pick.Label, Hue = Tint(pick.Hue, i++), Glyph = pick.Glyph, Badge = BadgeFor(pick.Route), Route = pick.Route,
                    Origin = new Point(Petal.Size / 2, Petal.Size / 2), Angle = -Math.PI / 2, Ring = 0, Pop = 1, Tag = pick,
                    LayoutTransform = new ScaleTransform(0.8, 0.8), Margin = new Thickness(2),
                    ToolTip = $"{Formats.Get(pick.Id).Label} — {pick.Note ?? Formats.Get(pick.Id).Description}",
                };
                petal.MouseEnter += (_, _) => Springs.To(petal, Petal.HoverProperty, 1, 420, 0.45);
                petal.MouseLeave += (_, _) => Springs.To(petal, Petal.HoverProperty, 0, 300, 0.6);
                petal.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(petal); };
                petal.Place();
                wrap.Children.Add(petal);
                _garden.Add(petal);
            }
            Garden.Children.Add(header);
            Garden.Children.Add(wrap);
        }
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = Search.Text.Trim();
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var culture = Loc.Turkish ? new System.Globalization.CultureInfo("tr-TR") : System.Globalization.CultureInfo.InvariantCulture;
        foreach (var p in _garden)
        {
            var f = Formats.Get(p.Target);
            bool hit = q.Length == 0
                || culture.CompareInfo.IndexOf(p.Label, q, System.Globalization.CompareOptions.IgnoreCase) >= 0
                || f.Extensions.Any(x => x.Contains(q, StringComparison.OrdinalIgnoreCase))
                || culture.CompareInfo.IndexOf(f.Description, q, System.Globalization.CompareOptions.IgnoreCase) >= 0
                || culture.CompareInfo.IndexOf(Formats.KindName(f.Kind), q, System.Globalization.CompareOptions.IgnoreCase) >= 0;
            p.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
        }
        // hide empty sections
        for (int i = 0; i + 1 < Garden.Children.Count; i += 2)
        {
            bool any = ((WrapPanel)Garden.Children[i + 1]).Children.Cast<UIElement>().Any(c => c.Visibility == Visibility.Visible);
            Garden.Children[i].Visibility = Garden.Children[i + 1].Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ── converting ──────────────────────────────────────────────────────────

    async void Start(string target, Route? route)
    {
        _lastTarget = target;
        _fixEngine = null;
        var files = _files.ToList();
        var fmt = Formats.Get(target);
        string hue = Formats.Hue(fmt.Kind == Kind.Folder ? Kind.Archive : fmt.Kind);
        foreach (var p in _petals) Springs.To(p, Petal.PopProperty, 0, 380, 0.8);
        Morph.Mood = Mood.Working;
        Morph.Glass();
        Morph.SetFill(0.04, hue);
        SetState(State.Working);
        string label = fmt.Label.Length > 0 ? fmt.Label : Loc.T("Folder");
        TitleText.Text = $"{Loc.T("Work.Title")} → {label}";
        SubText.Text = "";
        Status?.Invoke($"{Loc.T("Work.Title")} → {label}");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var job = new Job { Files = files, Target = target, Options = new Options { Quality = Settings.Quality, Combine = Combine.IsChecked == true && files.Count > 1 }, Settings = Settings };
        double shown = 0;
        long lastUi = 0;
        job.Progress += (v, stage) =>
        {
            long now = Environment.TickCount64;
            if (now - lastUi < 60 && v < 1) return;
            lastUi = now;
            Dispatcher.BeginInvoke(() =>
            {
                if (ct.IsCancellationRequested || _state != State.Working) return;
                shown = Math.Max(shown, v);
                Morph.SetFill(0.04 + 0.92 * shown);
                TitleText.Text = $"{Loc.T("Work.Title")} → {label}  {Math.Round(shown * 100)}%";
                SubText.Text = stage;
                Status?.Invoke($"{label} {Math.Round(shown * 100)}%");
            });
        };

        var sw = Stopwatch.StartNew();
        try
        {
            await Task.Run(() => job.Run(ct), ct);
        }
        catch (OperationCanceledException)
        {
            Status?.Invoke(null);
            if (_files.SequenceEqual(files)) { Morph.ClearFill(); Hold(files); }
            return;
        }
        catch (Exception ex)
        {
            Paths.Log("job: " + ex);
            job.Failures.Add(new Failure(files[0], Loc.T("Err.General", ex.Message)));
        }
        Status?.Invoke(null);
        if (ct.IsCancellationRequested) return;
        Settings.Count(target);
        Paths.Log($"done {target}: {job.Outputs.Count} ok, {job.Failures.Count} failed in {sw.ElapsedMilliseconds} ms");
        Finished(job, fmt, hue, route);
    }

    void Finished(Job job, Format fmt, string hue, Route? route)
    {
        Morph.SetFill(1);
        Results.Items.Clear();
        foreach (var o in job.Outputs) Results.Items.Add(ResultRow(o));
        if (job.Outputs.Count > 0)
        {
            SetState(State.Done);
            Morph.Mood = Mood.Happy;
            // Morph turns into the new format
            var drain = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            drain.Tick += (_, _) => { drain.Stop(); Morph.Tint(hue); Morph.ClearFill(); };
            drain.Start();
            TitleText.Text = job.Failures.Count > 0 ? Loc.T("Done.Partial", job.Outputs.Count, job.Failures.Count)
                : job.Outputs.Count == 1 ? Loc.T("Done.One") : Loc.T("Done.Many", job.Outputs.Count);
            SubText.Text = job.Failures.Count > 0 ? job.Failures[0].Message : Loc.T("Done.Drag");
            if (Settings.OpenFolderWhenDone) Reveal(job.Outputs[0].Path);
        }
        else
        {
            _fixEngine = route?.Missing;
            if (job.Failures.Count == 0) job.Failures.Add(new Failure(_files.FirstOrDefault() ?? "", Loc.T("Err.Empty")));
            SetState(State.Failed);
            Morph.ClearFill();
            Morph.Tint("#9AA4C7");
            Morph.Mood = Mood.Sad;
            TitleText.Text = Loc.T("Done.Failed");
            SubText.Text = job.Failures[0].Message;
            if (_fixEngine != null) FixButton.Content = Loc.T("Done.Install", _fixEngine.Name);
        }

        if (!IsVisible)
        {
            // it finished while hidden: pop back up to say so, then leave quietly
            Open(Opening.Drag);
            _autoHide.Start();
        }
    }

    FrameworkElement ResultRow(Output o)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        Icons.Load(o.Path, src => icon.Source = src); // the shell can take a while to find an icon: never on the UI thread
        var name = new TextBlock { Text = System.IO.Path.GetFileName(o.Path), FontSize = 14, FontWeight = FontWeights.ExtraBold, TextTrimming = TextTrimming.CharacterEllipsis };
        string delta = o.Before > 0 && o.After > 0 ? Math.Round((o.After - o.Before) * 100.0 / o.Before) is var pct && pct != 0 ? $"  ·  {(pct > 0 ? "+" : "−")}{Math.Abs(pct)}%" : "" : "";
        var sizes = new TextBlock { Text = $"{Loc.Size(o.Before)} → {Loc.Size(o.After)}{delta}", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Theme.Brush("Ink2"), Margin = new Thickness(0, 1, 0, 0) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, sizes } };
        Grid.SetColumn(text, 1);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 2);
        Button Mini(string glyph, string tip, Action act)
        {
            var b = new Button { Style = (Style)FindResource("Mini"), Tag = glyph, ToolTip = tip };
            b.Click += (_, _) => act();
            return b;
        }
        actions.Children.Add(Mini("", Loc.T("Done.Open"), () => OpenPath(o.Path)));
        actions.Children.Add(Mini("", Loc.T("Done.Show"), () => Reveal(o.Path)));
        Button? copy = null;
        copy = Mini("", Loc.T("Done.Copy"), () =>
        {
            var list = new StringCollection { o.Path };
            try { Clipboard.SetFileDropList(list); copy!.Tag = ""; copy.ToolTip = Loc.T("Done.Copied"); } catch { }
        });
        actions.Children.Add(copy);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(actions);

        // drag the result straight into a chat, a mail or another folder
        Point? down = null;
        grid.PreviewMouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button) down = e.GetPosition(grid); };
        grid.PreviewMouseLeftButtonUp += (_, _) => down = null;
        grid.MouseMove += (_, e) =>
        {
            if (down is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
            if ((e.GetPosition(grid) - d).Length < 6) return;
            down = null;
            _draggingOut = true;
            try { DragDrop.DoDragDrop(grid, new DataObject(DataFormats.FileDrop, new[] { o.Path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            finally { _draggingOut = false; }
        };
        return grid;
    }

    internal static ImageSource? ShellIcon(string path)
    {
        var info = new Win32.SHFILEINFO();
        IntPtr r = Win32.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(), Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON);
        if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { Win32.DestroyIcon(info.hIcon); }
    }

    // starting another program can take a moment: off the UI thread, so Morph never stalls
    static void OpenPath(string path) => Task.Run(() =>
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Paths.Log("open: " + ex.Message); }
    });

    static void Reveal(string path) => Task.Run(() =>
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch (Exception ex) { Paths.Log("reveal: " + ex.Message); }
    });

    void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        HideWell();
        App.Instance.ShowSettings();
    }

    void Close_Click(object sender, RoutedEventArgs e) => HideWell();

    void Again_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) { Reset(); return; }
        Morph.Jump(0.4);
        Hold(_files.ToList());
    }

    async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (_fixEngine is not { } engine) return;
        if (!engine.Installable)
        {
            OpenPath(engine.Url);
            return;
        }
        FixButton.IsEnabled = false;
        Morph.Mood = Mood.Working;
        Morph.TintDefault();
        var p = new Progress<double>(v => { Morph.SetFill(v, "#2EC4B6"); SubText.Text = Loc.T("Stage.Download", engine.Name) + $"  {Math.Round(v * 100)}%"; });
        bool ok = await Engines.Install(engine, p, CancellationToken.None);
        FixButton.IsEnabled = true;
        Morph.ClearFill();
        if (ok && _lastTarget != null)
        {
            Planner.Rebuild();
            Start(_lastTarget, null);
        }
        else
        {
            Morph.Mood = Mood.Sad;
            SubText.Text = Loc.T("Err.Download", engine.Name);
        }
    }
}
