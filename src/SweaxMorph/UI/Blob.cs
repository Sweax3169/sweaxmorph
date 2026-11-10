using System.Windows;
using System.Windows.Media;

namespace SweaxMorph.UI;

public enum Mood { Idle, Hungry, Full, Working, Happy, Sad }

/// <summary>
/// Morph, the gel creature that eats files. A soft body made of springy points, kawaii eyes that follow the
/// pointer, a mouth that opens when files come near, liquid that rises while it works, and sparkles when done.
/// Drawn by hand every frame (while visible) on the shared <see cref="Springs"/> clock.
/// </summary>
public sealed class Blob : FrameworkElement
{
    const int N = 18;

    struct Sp
    {
        public double X, V, T;
        public Sp(double v) { X = T = v; V = 0; }
        public void Step(double dt, double k, double z)
        {
            double c = 2 * z * Math.Sqrt(k), h = dt / 2;
            for (int i = 0; i < 2; i++) { V += ((T - X) * k - V * c) * h; X += V * h; }
        }
        public void Kick(double v) => V += v;
    }

    sealed class Particle
    {
        public int Type; // 0 sparkle, 1 ripple, 2 bubble, 3 crumb
        public double X, Y, Vx, Vy, Life, Max, Size;
        public Color Color;
    }

    readonly double[] _d = new double[N], _dv = new double[N];
    readonly Random _rnd = new(7);
    readonly List<Particle> _particles = [];
    readonly Point[] _pts = new Point[N];
    readonly Func<double, bool> _stepper;
    // Morph is drawn into this drawing, which the element shows; redrawing it every frame needs no layout pass
    // (InvalidateVisual would run one each frame, and with it a UI Automation report of the whole window)
    readonly DrawingGroup _drawing = new();
    bool _running, _dozing;
    double _t, _nextBlink = 2.5, _blinkUntil = -1, _sparkleClock, _bubbleClock, _eyesAwake = 1;

    Sp _squash = new(0), _lift = new(0), _eyeOpen = new(1), _happy = new(0), _mouthOpen = new(0), _mouthCurve = new(0.6),
       _mouthWide = new(0.22), _reach = new(0), _fill = new(0), _lookX = new(0), _lookY = new(0), _sweat = new(0), _hover = new(0);
    double _reachAngle;
    Color _c1 = Hex(Theme.Palette.GelTop), _c2 = Hex(Theme.Palette.GelBottom), _t1 = Hex(Theme.Palette.GelTop), _t2 = Hex(Theme.Palette.GelBottom),
          _liquid = Hex(Theme.Palette.Light.Accent), _liquidTarget = Hex(Theme.Palette.Light.Accent);
    // which of the palette's own gels Morph wears (none while it wears a file's colour), so a new palette can take over
    enum Gel { Own, Glass, File }
    Gel _gel = Gel.Own;
    Mood _mood = Mood.Idle;

    /// <summary>Body radius at rest, in DIPs.</summary>
    public double Radius { get; set; } = 70;
    /// <summary>Dark ink for the face (the same in both themes: Morph's face is printed on its gel).</summary>
    public Color Ink { get; set; } = Hex(Theme.Palette.Face);
    /// <summary>For recordings: render a given time instead of the clock.</summary>
    public bool Frozen { get; set; }
    /// <summary>No breathing wobble: a perfectly calm Morph (the app icon).</summary>
    public bool Still { get; set; }
    /// <summary>Bigger features read better at small sizes.</summary>
    public double FaceScale { get; set; } = 1;
    public bool Shadow { get; set; } = true;

    public Blob()
    {
        _stepper = Step;
        IsHitTestVisible = true;
        IsVisibleChanged += (_, _) => Wake();
        Loaded += (_, _) => { Theme.Changed += OnTheme; Wake(); };
        Unloaded += (_, _) => Theme.Changed -= OnTheme;
    }

    /// <summary>A new palette: Morph's own gel and its face ink follow it at once (a file's colour stays).</summary>
    void OnTheme()
    {
        Ink = Hex(Theme.Palette.Face);
        if (_gel == Gel.Own) TintDefault();
        else if (_gel == Gel.Glass) Glass();
        Wake();
        Redraw();
    }

    public Mood Mood
    {
        get => _mood;
        set
        {
            if (_mood == value) return;
            _mood = value;
            switch (value)
            {
                case Mood.Idle: Face(0, 0.6, 0.22, 1); break;
                case Mood.Hungry: Face(0.9, 0.25, 0.30, 1.15); _squash.Kick(-1.2); break;
                case Mood.Full: Face(0, 0.95, 0.26, 1); break;
                case Mood.Working: Face(0.28, 0, 0.15, 0.78); break;
                case Mood.Happy: Face(0.75, 1, 0.34, 1); Jump(1.0); Sparkles(14); break;
                case Mood.Sad: Face(0, -0.75, 0.2, 0.9); Shake(); break;
            }
            _happy.T = value == Mood.Happy ? 1 : 0;
            _sweat.T = value == Mood.Sad ? 1 : 0;
            Wake();
        }
    }

    void Face(double open, double curve, double wide, double eyes)
    {
        _mouthOpen.T = open; _mouthCurve.T = curve; _mouthWide.T = wide;
        _eyesAwake = eyes;
        _eyeOpen.T = _dozing ? 0.06 : eyes;
    }

    /// <summary>Waiting with nothing to do: the eyes close and, once the gel has settled, nothing is redrawn.</summary>
    public bool Dozing
    {
        get => _dozing;
        set
        {
            if (_dozing == value) return;
            _dozing = value;
            _eyeOpen.T = value ? 0.06 : _eyesAwake;
            if (!value) _nextBlink = _t + 1.5;
            Wake();
        }
    }

    /// <summary>The colours of the gel. Morph takes on the colour of the files it holds.</summary>
    public void Tint(Color top, Color bottom) { _t1 = top; _t2 = bottom; _gel = Gel.File; Wake(); }
    public void Tint(string hue) => Tint(Mix(Hex(hue), Colors.White, 0.42), Hex(hue));
    public void TintDefault() { Tint(Hex(Theme.Palette.GelTop), Hex(Theme.Palette.GelBottom)); _gel = Gel.Own; }
    /// <summary>Clear, milky gel while working, so the liquid inside shows in its own colour.</summary>
    public void Glass() { Tint(Hex(Theme.Palette.GlassTop), Hex(Theme.Palette.GlassBottom)); _gel = Gel.Glass; }
    /// <summary>Jumps straight to the colours it is heading for (a still picture, a palette swatch).</summary>
    public void SnapColours() { _c1 = _t1; _c2 = _t2; _liquid = _liquidTarget; Redraw(); }

    /// <summary>How full of liquid (0..1), and its colour (the target format's).</summary>
    public void SetFill(double level, string? hue = null)
    {
        _fill.T = Math.Clamp(level, 0, 1);
        if (hue != null) _liquidTarget = Hex(hue);
        Wake();
    }

    public void ClearFill() { _fill.T = 0; Wake(); }

    /// <summary>Where Morph looks, in this element's coordinates (null: straight ahead).</summary>
    public void LookAt(Point? p)
    {
        if (p is { } q)
        {
            var c = Center;
            double dx = q.X - c.X, dy = q.Y - c.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double k = Math.Min(1, len / 220) / Math.Max(1, len);
            _lookX.T = dx * k; _lookY.T = dy * k;
        }
        else { _lookX.T = 0; _lookY.T = 0; }
        Wake();
    }

    /// <summary>Stretches toward a point (files approaching); 0 strength lets go.</summary>
    public void Reach(Point p, double strength)
    {
        var c = Center;
        _reachAngle = Math.Atan2(p.Y - c.Y, p.X - c.X);
        _reach.T = Math.Clamp(strength, 0, 1);
        Wake();
    }

    public void Hover(bool on) { _hover.T = on ? 1 : 0; if (on) Poke(-Math.PI / 2, 0.6); Wake(); }

    /// <summary>A gulp: squash, wobble, a ripple and a few crumbs.</summary>
    public void Swallow()
    {
        _squash.Kick(9);
        _mouthOpen.X = 1; _mouthOpen.V = -6;
        for (int i = 0; i < N; i++) _dv[i] += (_rnd.NextDouble() - 0.3) * 2.2;
        var c = Center;
        _particles.Add(new Particle { Type = 1, X = c.X, Y = c.Y, Life = 0, Max = 0.7, Size = Radius, Color = _t2 });
        Wake();
    }

    public void Poke(double angle, double strength)
    {
        for (int i = 0; i < N; i++)
        {
            double a = 2 * Math.PI * i / N - Math.PI / 2;
            double near = Math.Max(0, Math.Cos(a - angle));
            _dv[i] -= strength * near * near * 1.6;
        }
        Wake();
    }

    public void Jump(double strength) { _lift.Kick(-strength * 320); _squash.Kick(5 * strength); Wake(); }

    void Shake()
    {
        for (int i = 0; i < N; i++) _dv[i] += (i % 2 == 0 ? 1 : -1) * 0.9;
        Wake();
    }

    public void Sparkles(int count)
    {
        var c = Center;
        for (int i = 0; i < count; i++)
        {
            double a = _rnd.NextDouble() * Math.PI * 2, sp = 90 + _rnd.NextDouble() * 140;
            _particles.Add(new Particle
            {
                Type = 0, X = c.X + Math.Cos(a) * Radius * 0.8, Y = c.Y + Math.Sin(a) * Radius * 0.8,
                Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp - 60, Life = 0, Max = 0.7 + _rnd.NextDouble() * 0.5,
                Size = 5 + _rnd.NextDouble() * 6, Color = i % 3 == 0 ? Colors.White : Mix(_t2, Colors.White, 0.3),
            });
        }
        Wake();
    }

    Point Center => new(ActualWidth / 2, ActualHeight / 2 + (Shadow ? Radius * 0.11 : 0));

    void Wake()
    {
        if (_running || !IsVisible || Frozen) return;
        _running = true;
        Springs.Run(_stepper);
    }

    /// <summary>Advances the creature by <paramref name="dt"/> seconds (also used to render recordings).</summary>
    public bool Step(double dt)
    {
        if (!IsVisible && !Frozen) { _running = false; return false; }
        _t += dt;
        _squash.Step(dt, 260, 0.32);
        _lift.Step(dt, 140, 0.45);
        _eyeOpen.Step(dt, 900, 0.7);
        _happy.Step(dt, 200, 0.8);
        _mouthOpen.Step(dt, 340, 0.55);
        _mouthCurve.Step(dt, 240, 0.7);
        _mouthWide.Step(dt, 240, 0.7);
        _reach.Step(dt, 120, 0.6);
        _fill.Step(dt, 40, 0.9);
        _lookX.Step(dt, 160, 0.75);
        _lookY.Step(dt, 160, 0.75);
        _sweat.Step(dt, 120, 0.8);
        _hover.Step(dt, 200, 0.6);

        // the gel: each point springs back to round, pulling on its neighbours
        for (int i = 0; i < N; i++)
        {
            int a = (i + N - 1) % N, b = (i + 1) % N;
            double force = -_d[i] * 180 + (_d[a] + _d[b] - 2 * _d[i]) * 260 - _dv[i] * 7.5;
            _dv[i] += force * dt;
        }
        for (int i = 0; i < N; i++) _d[i] += _dv[i] * dt;

        double k = 1 - Math.Exp(-dt * 5);
        _c1 = Lerp(_c1, _t1, k); _c2 = Lerp(_c2, _t2, k); _liquid = Lerp(_liquid, _liquidTarget, k);

        // blinking, but never with happy or squinting eyes
        if (_t > _nextBlink && !_dozing && _mood is not Mood.Happy and not Mood.Working)
        {
            _blinkUntil = _t + 0.11;
            _nextBlink = _t + 2.2 + _rnd.NextDouble() * 3.8;
            if (_rnd.NextDouble() < 0.18) _nextBlink = _t + 0.25; // a double blink now and then
        }
        double eyes = _t < _blinkUntil ? 0.05 : _eyeOpen.T;
        if (Math.Abs(_eyeOpen.X - eyes) > 0.001 && _t < _blinkUntil) _eyeOpen.X += (eyes - _eyeOpen.X) * Math.Min(1, dt * 40);

        // bubbles in the liquid while working, sparkles keep coming while happy
        if (_fill.X > 0.04 && (_bubbleClock += dt) > 0.16)
        {
            _bubbleClock = 0;
            var c = Center;
            _particles.Add(new Particle { Type = 2, X = c.X + (_rnd.NextDouble() - 0.5) * Radius * 1.1, Y = c.Y + Radius * 0.85, Vy = -40 - _rnd.NextDouble() * 50, Max = 1.6, Size = 2 + _rnd.NextDouble() * 4, Color = Colors.White });
        }
        if (_mood == Mood.Happy && (_sparkleClock += dt) > 0.5) { _sparkleClock = 0; Sparkles(2); }

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life += dt;
            if (p.Life >= p.Max) { _particles.RemoveAt(i); continue; }
            p.X += p.Vx * dt; p.Y += p.Vy * dt;
            if (p.Type == 0) { p.Vx *= Math.Exp(-dt * 3); p.Vy = p.Vy * Math.Exp(-dt * 3) + 120 * dt; }
            if (p.Type == 2) p.X += Math.Sin(_t * 6 + p.Size) * 10 * dt;
        }

        Redraw();
        bool still = true;
        for (int i = 0; i < N; i++) if (Math.Abs(_dv[i]) >= 0.002) { still = false; break; }
        bool settled = still && _particles.Count == 0 && Math.Abs(_squash.V) < 0.01 && Math.Abs(_lift.V) < 0.05
            && Math.Abs(_eyeOpen.X - _eyeOpen.T) < 0.004 && Math.Abs(_eyeOpen.V) < 0.01;
        // keep breathing while on screen: Morph is alive (a dozing Morph holds still)
        return (IsVisible && !_dozing) || !settled;
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        Redraw();
        dc.DrawDrawing(_drawing);
    }

    void Redraw()
    {
        if (ActualWidth <= 0) return;
        using var dc = _drawing.Open();
        Paint(dc);
    }

    void Paint(DrawingContext dc)
    {
        double R = Radius * (1 + _hover.X * 0.04);
        double sq = Math.Clamp(_squash.X * 0.04, -0.22, 0.28);
        double bob = Math.Sin(_t * 1.7) * 3 * (_mood == Mood.Working ? 0.3 : 1);
        var c0 = Center;
        double lift = Math.Min(0, _lift.X * 0.01) * 60;
        var c = new Point(c0.X, c0.Y + bob + lift + R * sq * 0.9);

        // shadow on the floor, smaller when Morph is in the air
        double air = Math.Clamp(-lift / 60, 0, 1);
        if (Shadow) dc.DrawEllipse(ShadowBrush((byte)(70 * (1 - air * 0.6))), null, new Point(c0.X, c0.Y + R * 1.02), R * (0.95 + sq) * (1 - air * 0.3), R * 0.17 * (1 - air * 0.3));

        // body outline
        for (int i = 0; i < N; i++)
        {
            double a = 2 * Math.PI * i / N - Math.PI / 2;
            double breathe = Still ? 0 : 0.022 * Math.Sin(_t * 1.6 + i * 0.9) + 0.012 * Math.Sin(_t * 2.9 + i * 2.3);
            double toward = Math.Max(0, Math.Cos(a - _reachAngle));
            double r = R * (1 + breathe + _d[i] * 0.12 + _reach.X * 0.32 * toward * toward * toward);
            double down = Math.Max(0, Math.Sin(a));
            r *= 1 - 0.07 * Math.Pow(down, 4); // sits on a slightly flat bottom
            _pts[i] = new Point(c.X + Math.Cos(a) * r * (1 + sq), c.Y + Math.Sin(a) * r * (1 - sq));
        }
        var body = Smooth(_pts);

        if (_bodyBrush == null || _bodyC1 != _c1 || _bodyC2 != _c2)
        {
            _bodyC1 = _c1; _bodyC2 = _c2;
            _bodyBrush = Freeze(new LinearGradientBrush(_c1, _c2, new Point(0.2, 0), new Point(0.8, 1)));
            var rim = new RadialGradientBrush { GradientOrigin = new Point(0.42, 0.36), Center = new Point(0.45, 0.42), RadiusX = 0.62, RadiusY = 0.62 };
            rim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.62));
            rim.GradientStops.Add(new GradientStop(Color.FromArgb(70, Dark(_c2).R, Dark(_c2).G, Dark(_c2).B), 1));
            _rimBrush = Freeze(rim);
        }
        dc.DrawGeometry(_bodyBrush, null, body);

        // liquid rising inside
        if (_fill.X > 0.003)
        {
            dc.PushClip(body);
            double top = c.Y - R * (1 - sq), bottom = c.Y + R * (1 - sq);
            double level = bottom - (bottom - top + 6) * Math.Clamp(_fill.X, 0, 1);
            var wave = new StreamGeometry();
            using (var g = wave.Open())
            {
                double left = c.X - R * 1.6, right = c.X + R * 1.6;
                g.BeginFigure(new Point(left, bottom + 20), true, true);
                for (double x = left; x <= right; x += 6)
                    g.LineTo(new Point(x, level + Math.Sin(x * 0.07 + _t * 4.2) * 3.2 + Math.Sin(x * 0.031 - _t * 2.3) * 2.4), true, false);
                g.LineTo(new Point(right, bottom + 20), true, false);
            }
            wave.Freeze();
            if (_liquidBrush == null || _liquidShown != _liquid)
            {
                _liquidShown = _liquid;
                _liquidBrush = Freeze(new LinearGradientBrush(Mix(_liquid, Colors.White, 0.25), _liquid, 90) { Opacity = 0.92 });
            }
            dc.DrawGeometry(_liquidBrush, null, wave);
            foreach (var p in _particles)
                if (p.Type == 2 && p.Y > level) dc.DrawEllipse(null, BubblePen, new Point(p.X, p.Y), p.Size, p.Size);
            dc.Pop();
        }

        // depth: a darker rim and a soft glow from the top left
        dc.DrawGeometry(_rimBrush, null, body);
        dc.DrawGeometry(GlowBrush, null, body);

        // gloss
        dc.PushTransform(new RotateTransform(-28, c.X - R * 0.42, c.Y - R * 0.5));
        dc.DrawEllipse(GlossBrush, null, new Point(c.X - R * 0.42, c.Y - R * 0.5), R * 0.27, R * 0.13);
        dc.Pop();
        dc.DrawEllipse(SpeckBrush, null, new Point(c.X - R * 0.14, c.Y - R * 0.72), R * 0.045, R * 0.04);

        DrawFace(dc, c, R, sq);

        foreach (var p in _particles)
        {
            double life = p.Life / p.Max;
            if (p.Type == 0) Star(dc, new Point(p.X, p.Y), p.Size * (1 - life * 0.6), Color.FromArgb((byte)(255 * (1 - life)), p.Color.R, p.Color.G, p.Color.B), p.Life * 4);
            else if (p.Type == 1)
            {
                double r = p.Size * (1 + life * 0.9);
                dc.DrawEllipse(null, Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb((byte)(160 * (1 - life)), p.Color.R, p.Color.G, p.Color.B))), 3 * (1 - life) + 0.5)), new Point(p.X, p.Y + bob), r * 1.08, r);
            }
        }
    }

    void DrawFace(DrawingContext dc, Point c, double R, double sq)
    {
        if (_inkBrush == null || _inkShown != Ink) { _inkShown = Ink; _inkBrush = Freeze(new SolidColorBrush(Ink)); }
        var ink = _inkBrush;
        R *= FaceScale;
        double lx = _lookX.X * R * 0.16, ly = _lookY.X * R * 0.12;
        var fc = new Point(c.X + lx * 0.6, c.Y + ly * 0.6 + R * sq * 0.3);
        double happy = Math.Clamp(_happy.X, 0, 1);

        // eyes
        double ex = R * 0.31 * (1 + sq * 0.6), ey = -R * 0.08;
        double rx = R * 0.092, ry = R * 0.118 * Math.Clamp(_eyeOpen.X, 0.04, 1.3);
        foreach (int side in new[] { -1, 1 })
        {
            var e = new Point(fc.X + side * ex + lx * 0.5, fc.Y + ey + ly * 0.5);
            if (happy > 0.5)
            {
                // ^ ^
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(e.X - rx * 1.15, e.Y + ry * 0.25), false, false);
                    g.QuadraticBezierTo(new Point(e.X, e.Y - ry * 1.35), new Point(e.X + rx * 1.15, e.Y + ry * 0.25), true, true);
                }
                arc.Freeze();
                dc.DrawGeometry(null, Freeze(new Pen(ink, rx * 0.62) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), arc);
            }
            else
            {
                dc.DrawEllipse(ink, null, e, rx, ry);
                if (ry > rx * 0.5)
                {
                    dc.DrawEllipse(EyeShine, null, new Point(e.X + rx * 0.34 + lx * 0.15, e.Y - ry * 0.36), rx * 0.38, rx * 0.38);
                    dc.DrawEllipse(EyeShine, null, new Point(e.X - rx * 0.32, e.Y + ry * 0.38), rx * 0.17, rx * 0.17);
                }
            }
        }

        // cheeks
        dc.DrawEllipse(Blush, null, new Point(fc.X - R * 0.5, fc.Y + R * 0.1), R * 0.16, R * 0.1);
        dc.DrawEllipse(Blush, null, new Point(fc.X + R * 0.5, fc.Y + R * 0.1), R * 0.16, R * 0.1);

        // mouth: a curve that can open into a little oval (with a tongue)
        double open = Math.Clamp(_mouthOpen.X, 0, 1.2), curve = _mouthCurve.X, wide = R * Math.Clamp(_mouthWide.X, 0.08, 0.45);
        var m = new Point(fc.X, fc.Y + R * 0.16);
        if (open < 0.1)
        {
            var g0 = new StreamGeometry();
            using (var g = g0.Open())
            {
                g.BeginFigure(new Point(m.X - wide / 2, m.Y - curve * R * 0.03), false, false);
                g.QuadraticBezierTo(new Point(m.X, m.Y + curve * R * 0.11), new Point(m.X + wide / 2, m.Y - curve * R * 0.03), true, true);
            }
            g0.Freeze();
            dc.DrawGeometry(null, Freeze(new Pen(ink, R * 0.045) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), g0);
        }
        else
        {
            double h = R * 0.2 * open;
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                double top = m.Y - h * 0.35 - Math.Max(0, curve) * R * 0.01;
                g.BeginFigure(new Point(m.X - wide / 2, top), true, true);
                g.QuadraticBezierTo(new Point(m.X, top + Math.Min(0, -curve) * h * 0.3 - h * 0.08), new Point(m.X + wide / 2, top), true, true);
                g.BezierTo(new Point(m.X + wide / 2, top + h * (0.9 + curve * 0.2)), new Point(m.X - wide / 2, top + h * (0.9 + curve * 0.2)), new Point(m.X - wide / 2, top), true, true);
            }
            shape.Freeze();
            dc.DrawGeometry(ink, null, shape);
            dc.PushClip(shape);
            dc.DrawEllipse(Tongue, null, new Point(m.X, m.Y - h * 0.35 + h * 0.95), wide * 0.32, h * 0.38);
            dc.Pop();
        }

        // a worried drop of sweat
        if (_sweat.X > 0.05)
        {
            var d = new Point(c.X + R * 0.62, c.Y - R * 0.38 + Math.Sin(_t * 3) * 2);
            var drop = new StreamGeometry();
            using (var g = drop.Open())
            {
                double s = R * 0.09 * _sweat.X;
                g.BeginFigure(new Point(d.X, d.Y - s * 1.8), true, true);
                g.BezierTo(new Point(d.X + s * 1.2, d.Y - s * 0.2), new Point(d.X + s, d.Y + s), new Point(d.X, d.Y + s), true, true);
                g.BezierTo(new Point(d.X - s, d.Y + s), new Point(d.X - s * 1.2, d.Y - s * 0.2), new Point(d.X, d.Y - s * 1.8), true, true);
            }
            drop.Freeze();
            dc.DrawGeometry(Sweat, null, drop);
        }
    }

    static void Star(DrawingContext dc, Point p, double r, Color color, double spin)
    {
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            for (int i = 0; i < 8; i++)
            {
                double a = spin + i * Math.PI / 4, rr = i % 2 == 0 ? r : r * 0.32;
                var q = new Point(p.X + Math.Cos(a) * rr, p.Y + Math.Sin(a) * rr);
                if (i == 0) s.BeginFigure(q, true, true); else s.LineTo(q, true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(Freeze(new SolidColorBrush(color)), null, g);
    }

    // ── brushes: the constant ones are made once, the ones that follow the colours only when those change ──

    Brush? _bodyBrush, _rimBrush, _liquidBrush, _inkBrush;
    Color _bodyC1, _bodyC2, _liquidShown, _inkShown;
    readonly Brush?[] _shadows = new Brush?[256];

    Brush ShadowBrush(byte alpha) => _shadows[alpha] ??= Freeze(new RadialGradientBrush(Color.FromArgb(alpha, 20, 10, 40), Color.FromArgb(0, 20, 10, 40)));

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    static readonly Brush GlowBrush = Freeze(new RadialGradientBrush(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) { Center = new Point(0.34, 0.28), GradientOrigin = new Point(0.34, 0.28), RadiusX = 0.42, RadiusY = 0.36 });
    static readonly Brush GlossBrush = Freeze(new RadialGradientBrush(Color.FromArgb(215, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)));
    static readonly Brush SpeckBrush = Freeze(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)));
    static readonly Brush EyeShine = Freeze(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)));
    static readonly Brush Blush = Freeze(new RadialGradientBrush(Color.FromArgb(150, 255, 110, 160), Color.FromArgb(0, 255, 110, 160)));
    static readonly Brush Tongue = Freeze(new SolidColorBrush(Hex("#FF7FA6")));
    static readonly Brush Sweat = Freeze(new LinearGradientBrush(Hex("#D8F1FF"), Hex("#7CC4FF"), 90));
    static readonly Pen BubblePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))), 1.2));

    /// <summary>A closed curve through the points (Catmull-Rom as cubic Béziers).</summary>
    internal static StreamGeometry Smooth(IReadOnlyList<Point> p)
    {
        var geo = new StreamGeometry();
        int n = p.Count;
        using (var g = geo.Open())
        {
            g.BeginFigure(p[0], true, true);
            for (int i = 0; i < n; i++)
            {
                Point p0 = p[(i + n - 1) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
                g.BezierTo(new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6), new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6), p2, true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    internal static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);
    internal static Color Mix(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Color Lerp(Color a, Color b, double t) => Mix(a, b, t);
    static Color Dark(Color c) => Mix(c, Color.FromRgb(40, 20, 70), 0.45);
}
