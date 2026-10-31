using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SweaxMorph.UI;

/// <summary>
/// A critically-tuned damped spring as a WPF easing function, so storyboards and code share the panel's feel.
/// <see cref="Damping"/> below 1 overshoots a little; the motion is settled by the end of the duration.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public double Damping { get; set; } = 0.74;
    public double Frequency { get; set; } = 11;

    public SpringEase() => EasingMode = EasingMode.EaseIn;

    protected override double EaseInCore(double t)
    {
        double z = Damping, w = Frequency;
        if (z >= 1) return 1 - Math.Exp(-w * t) * (1 + w * t);
        double wd = w * Math.Sqrt(1 - z * z);
        return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();

    public static readonly SpringEase Soft = Frozen(0.86, 10), Lively = Frozen(0.68, 12);

    static SpringEase Frozen(double damping, double frequency)
    {
        var e = new SpringEase { Damping = damping, Frequency = frequency };
        e.Freeze();
        return e;
    }
}

/// <summary>
/// Every moving value in the panel runs on these springs, from one frame loop. In demo mode the loop is
/// stepped by hand, so a recording shows exactly what the app does, frame by frame.
/// </summary>
public static class Springs
{
    sealed class Anim
    {
        public required DependencyObject Obj;
        public required DependencyProperty Prop;
        public double X, V, T, K, Z, Delay;
    }

    static readonly List<Anim> _anims = [];
    static readonly List<Func<double, bool>> _steppers = [];
    static bool _ticking;
    static TimeSpan _last;

    /// <summary>When set, nothing moves until <see cref="Step"/> is called.</summary>
    public static bool ManualClock { get; set; }

    /// <summary>Springs <paramref name="prop"/> of <paramref name="obj"/> to <paramref name="target"/>.</summary>
    public static void To(DependencyObject obj, DependencyProperty prop, double target, double stiffness = 300, double damping = 0.82, double? from = null, double delay = 0)
    {
        var a = _anims.Find(x => ReferenceEquals(x.Obj, obj) && x.Prop == prop);
        if (a == null)
        {
            a = new Anim { Obj = obj, Prop = prop, X = from ?? (double)obj.GetValue(prop) };
            _anims.Add(a);
        }
        else if (from is double f) { a.X = f; a.V = 0; }
        if (from is double start) obj.SetValue(prop, start);
        a.T = target; a.K = stiffness; a.Z = damping; a.Delay = delay;
        Kick();
    }

    /// <summary>Sets a value at once and stops any spring on it.</summary>
    public static void Set(DependencyObject obj, DependencyProperty prop, double value)
    {
        _anims.RemoveAll(x => ReferenceEquals(x.Obj, obj) && x.Prop == prop);
        obj.SetValue(prop, value);
    }

    /// <summary>Runs a custom stepper (returns false once it has settled) on the same clock.</summary>
    public static void Run(Func<double, bool> step)
    {
        if (!_steppers.Contains(step)) _steppers.Add(step);
        Kick();
    }

    public static bool Busy => _anims.Count > 0 || _steppers.Count > 0;

    /// <summary>Advances everything by <paramref name="dt"/> seconds. Returns false when all is still.</summary>
    public static bool Step(double dt)
    {
        for (int i = _anims.Count - 1; i >= 0; i--)
        {
            var a = _anims[i];
            if (a.Delay > 0) { a.Delay -= dt; continue; }
            double c = 2 * a.Z * Math.Sqrt(a.K);
            for (int s = 0; s < 2; s++)
            {
                double h = dt / 2;
                a.V += ((a.T - a.X) * a.K - a.V * c) * h;
                a.X += a.V * h;
            }
            bool done = Math.Abs(a.T - a.X) < 0.0008 && Math.Abs(a.V) < 0.01;
            if (done) a.X = a.T;
            a.Obj.SetValue(a.Prop, a.X);
            if (done) _anims.RemoveAt(i);
        }
        for (int i = _steppers.Count - 1; i >= 0; i--)
            if (!_steppers[i](dt)) _steppers.RemoveAt(i);
        return Busy;
    }

    static void Kick()
    {
        if (_ticking || ManualClock) return;
        _ticking = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += Tick;
    }

    static void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0.001, 1 / 30.0);
        _last = now;
        if (!Step(dt) || ManualClock)
        {
            CompositionTarget.Rendering -= Tick;
            _ticking = false;
        }
    }
}

/// <summary>
/// How far a row is open (0 closed, 1 open). Inherited, so one spring on the row drives every
/// <see cref="Reveal"/>, <see cref="Grow"/> and tint inside it. <c>Follow</c> binds it to a flag.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty OpenProperty = DependencyProperty.RegisterAttached(
        "Open", typeof(double), typeof(Motion),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static double GetOpen(DependencyObject d) => (double)d.GetValue(OpenProperty);
    public static void SetOpen(DependencyObject d, double v) => d.SetValue(OpenProperty, v);

    public static readonly DependencyProperty FollowProperty = DependencyProperty.RegisterAttached(
        "Follow", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) =>
        {
            bool open = (bool)e.NewValue;
            if (open) Springs.To(d, OpenProperty, 1, 230, 0.76);
            else Springs.To(d, OpenProperty, 0, 340, 1);
        }));

    public static bool GetFollow(DependencyObject d) => (bool)d.GetValue(FollowProperty);
    public static void SetFollow(DependencyObject d, bool v) => d.SetValue(FollowProperty, v);
}

/// <summary>a + b·x, with "a b" as the parameter — maps a row's openness to opacities and scales.</summary>
public sealed class Affine : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var p = ((string)parameter).Split(' ');
        double x = value is double d ? d : 0;
        return double.Parse(p[0], CultureInfo.InvariantCulture) + double.Parse(p[1], CultureInfo.InvariantCulture) * x;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Shows the first <see cref="Closed"/> pixels of its content when closed and all of it when open.</summary>
public sealed class Reveal : Decorator
{
    public static readonly DependencyProperty OpenProperty = Motion.OpenProperty.AddOwner(typeof(Reveal),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ClosedProperty = DependencyProperty.Register(nameof(Closed), typeof(double), typeof(Reveal),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Open { get => (double)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }
    public double Closed { get => (double)GetValue(ClosedProperty); set => SetValue(ClosedProperty, value); }

    double _full;

    public Reveal() => ClipToBounds = true;

    protected override Size MeasureOverride(Size available)
    {
        if (Child == null) return default;
        Child.Measure(new Size(available.Width, double.PositiveInfinity));
        _full = Child.DesiredSize.Height;
        double closed = Math.Min(Closed, _full);
        double h = closed + (_full - closed) * Math.Max(0, Open);
        return new Size(Child.DesiredSize.Width, Math.Max(0, Math.Min(h, Math.Max(_full, closed) + 12)));
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, final.Width, Math.Max(_full, final.Height)));
        return final;
    }
}

/// <summary>Resizes its content between <see cref="From"/> and <see cref="To"/> as the row opens (images zoom rather than crop).</summary>
public sealed class Grow : Decorator
{
    public static readonly DependencyProperty OpenProperty = Motion.OpenProperty.AddOwner(typeof(Grow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(nameof(From), typeof(double), typeof(Grow),
        new FrameworkPropertyMetadata(40.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(nameof(To), typeof(double), typeof(Grow),
        new FrameworkPropertyMetadata(160.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Open { get => (double)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }
    public double From { get => (double)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public double To { get => (double)GetValue(ToProperty); set => SetValue(ToProperty, value); }

    double Height0 => Math.Max(0, From + (To - From) * Open);

    protected override Size MeasureOverride(Size available)
    {
        double h = Height0;
        Child?.Measure(new Size(available.Width, h));
        return new Size(Child?.DesiredSize.Width ?? 0, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, final.Width, Height0));
        return new Size(final.Width, Height0);
    }
}

/// <summary>Shows <see cref="Amount"/> of its content's width (a label sliding out beside an icon).</summary>
public sealed class RevealX : Decorator
{
    public static readonly DependencyProperty AmountProperty = DependencyProperty.Register(nameof(Amount), typeof(double), typeof(RevealX),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Amount { get => (double)GetValue(AmountProperty); set => SetValue(AmountProperty, value); }

    /// <summary>Springs <see cref="Amount"/> to 1 or 0.</summary>
    public static readonly DependencyProperty ShownProperty = DependencyProperty.Register(nameof(Shown), typeof(bool), typeof(RevealX),
        new PropertyMetadata(false, (d, e) => Springs.To(d, AmountProperty, (bool)e.NewValue ? 1 : 0, 260, 0.8)));

    public bool Shown { get => (bool)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }

    double _full;

    public RevealX() => ClipToBounds = true;

    protected override Size MeasureOverride(Size available)
    {
        if (Child == null) return default;
        Child.Measure(new Size(double.PositiveInfinity, available.Height));
        _full = Child.DesiredSize.Width;
        return new Size(Math.Max(0, _full * Amount), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, _full, final.Height));
        return final;
    }
}

/// <summary>Follows its content's height on a spring, so the panel glides to the size of what it shows.</summary>
public sealed class SmoothHeight : Decorator
{
    double _shown = -1, _velocity, _target;
    bool _running;

    /// <summary>When false the size jumps.</summary>
    public bool Animate { get; set; } = true;

    public SmoothHeight() => ClipToBounds = true;

    protected override Size MeasureOverride(Size available)
    {
        if (Child == null) return default;
        Child.Measure(available);
        _target = Child.DesiredSize.Height;
        if (_shown < 0 || !Animate || !IsVisible) { _shown = _target; _velocity = 0; }
        else if (Math.Abs(_target - _shown) > 0.5 && !_running)
        {
            _running = true;
            Springs.Run(Step);
        }
        return new Size(Child.DesiredSize.Width, _shown);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, final.Width, Math.Max(final.Height, _target)));
        return final;
    }

    /// <summary>Jumps to the content's height (used before the panel appears).</summary>
    public void Snap() { _shown = -1; InvalidateMeasure(); }

    bool Step(double dt)
    {
        const double k = 380, c = 2 * 0.92 * 19.5;
        for (int i = 0; i < 2; i++)
        {
            double h = dt / 2;
            _velocity += ((_target - _shown) * k - _velocity * c) * h;
            _shown += _velocity * h;
        }
        bool done = Math.Abs(_target - _shown) < 0.4 && Math.Abs(_velocity) < 4;
        if (done) { _shown = _target; _velocity = 0; _running = false; }
        InvalidateMeasure();
        return !done;
    }
}

/// <summary>Paints a TextBlock's text with the search terms picked out.</summary>
public static class Highlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlight), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(Highlight), new PropertyMetadata(null, Changed));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? v) => d.SetValue(TextProperty, v);
    public static string? GetQuery(DependencyObject d) => (string?)d.GetValue(QueryProperty);
    public static void SetQuery(DependencyObject d, string? v) => d.SetValue(QueryProperty, v);

    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        string text = GetText(tb) ?? "";
        string query = GetQuery(tb) ?? "";
        tb.Inlines.Clear();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => t.Length >= 2).ToArray();
        if (terms.Length == 0 || text.Length == 0) { tb.Text = text; return; }

        var marks = new bool[text.Length];
        var cmp = CultureInfo.InvariantCulture.CompareInfo;
        foreach (var term in terms)
        {
            int at = 0;
            while (at < text.Length)
            {
                int i = cmp.IndexOf(text, term, at, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (i < 0) break;
                for (int j = i; j < Math.Min(text.Length, i + term.Length); j++) marks[j] = true;
                at = i + Math.Max(1, term.Length);
            }
        }
        var accent = (Brush)tb.FindResource("Accent");
        int start = 0;
        while (start < text.Length)
        {
            bool on = marks[start];
            int end = start;
            while (end < text.Length && marks[end] == on) end++;
            var run = new Run(text[start..end]);
            if (on) { run.Foreground = accent; run.FontWeight = FontWeights.SemiBold; }
            tb.Inlines.Add(run);
            start = end;
        }
    }
}
