using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SweaxMorph.Core;

namespace SweaxMorph.UI;

/// <summary>
/// A gel droplet carrying one target format. Petals bloom out of Morph in a ring; hovering swells one,
/// dropping files on it (or clicking it) starts the conversion.
/// </summary>
public sealed class Petal : FrameworkElement
{
    public static readonly DependencyProperty PopProperty = DependencyProperty.Register(nameof(Pop), typeof(double), typeof(Petal),
        new FrameworkPropertyMetadata(0.0, (d, _) => ((Petal)d).Place()));
    public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(nameof(Hover), typeof(double), typeof(Petal),
        new FrameworkPropertyMetadata(0.0, (d, _) => ((Petal)d).Place()));

    public double Pop { get => (double)GetValue(PopProperty); set => SetValue(PopProperty, value); }
    public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }

    /// <summary>The format id, or a special action: "more", "shrink", "back".</summary>
    public required string Target { get; init; }
    public required string Label { get; init; }
    public required string Hue { get; init; }
    /// <summary>Segoe Fluent Icons glyph instead of a label.</summary>
    public string? Glyph { get; init; }
    /// <summary>Small corner mark: "↓" (an engine downloads first) or "lock" (needs an app).</summary>
    public string? Badge { get; init; }
    public Route? Route { get; init; }

    public Point Origin { get; set; }
    public double Angle { get; set; }
    public double Ring { get; set; } = 150;
    public double Bob { get; set; }
    public const double Size = 64;

    readonly TranslateTransform _move = new();
    readonly ScaleTransform _scale = new();

    public Petal()
    {
        Width = Size;
        Height = Size;
        RenderTransform = new TransformGroup { Children = { _scale, _move } };
        RenderTransformOrigin = new Point(0.5, 0.5);
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    /// <summary>Sits on the ring: flies out from the centre as it pops, swells a little when hovered.</summary>
    public void Place()
    {
        double p = Pop;
        double ease = 1 - Math.Pow(1 - Math.Clamp(p, 0, 1.2), 3);
        double r = Ring * (0.25 + 0.75 * ease) + Hover * 6;
        _move.X = Origin.X + Math.Cos(Angle) * r - Size / 2;
        _move.Y = Origin.Y + Math.Sin(Angle) * r - Size / 2 + Bob * Math.Clamp(p, 0, 1);
        double s = Math.Max(0, p) * (1 + Hover * 0.16);
        _scale.ScaleX = s;
        _scale.ScaleY = s;
        Opacity = Math.Clamp(p * 1.6, 0, 1);
        IsHitTestVisible = p > 0.6;
    }

    static readonly Typeface Face = new(Theme.Rounded, FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
    static readonly Typeface Icons = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    protected override void OnRender(DrawingContext dc)
    {
        var hue = Blob.Hex(Hue);
        var c = new Point(Size / 2, Size / 2);
        double r = Size / 2 - 3;

        // a soft drop shadow, then the droplet with its own little gloss
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb((byte)(60 + 40 * Hover), hue.R, hue.G, hue.B), Color.FromArgb(0, hue.R, hue.G, hue.B)), null, new Point(c.X, c.Y + 5), r * 1.12, r * 1.02);
        var body = new LinearGradientBrush(Blob.Mix(hue, Colors.White, 0.38), hue, new Point(0.25, 0), new Point(0.75, 1));
        dc.DrawEllipse(body, null, c, r, r);
        var rim = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.45, 0.42), RadiusX = 0.6, RadiusY = 0.6 };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.6));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(60, 40, 16, 70), 1));
        dc.DrawEllipse(rim, null, c, r, r);
        dc.PushTransform(new RotateTransform(-30, c.X - r * 0.38, c.Y - r * 0.48));
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(200, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)), null, new Point(c.X - r * 0.38, c.Y - r * 0.48), r * 0.32, r * 0.15);
        dc.Pop();

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ink = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        var shade = new SolidColorBrush(Color.FromArgb(70, 40, 14, 60));
        if (Glyph != null)
        {
            var g = new FormattedText(Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Icons, 22, ink, dpi);
            dc.DrawText(new FormattedText(Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Icons, 22, shade, dpi), new Point(c.X - g.Width / 2, c.Y - g.Height / 2 + 1.5));
            dc.DrawText(g, new Point(c.X - g.Width / 2, c.Y - g.Height / 2));
        }
        else
        {
            double size = 16;
            FormattedText t;
            while (true)
            {
                t = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, ink, dpi);
                if (t.Width <= r * 1.62 || size <= 9) break;
                size -= 0.5;
            }
            var s = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, shade, dpi);
            dc.DrawText(s, new Point(c.X - t.Width / 2, c.Y - t.Height / 2 + 1.5));
            dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
        }

        if (Badge != null)
        {
            var b = new Point(c.X + r * 0.72, c.Y - r * 0.72);
            dc.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(hue), 2), b, 9.5, 9.5);
            string glyph = Badge == "lock" ? "" : "";
            var g = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Icons, 10, new SolidColorBrush(hue), dpi);
            dc.DrawText(g, new Point(b.X - g.Width / 2, b.Y - g.Height / 2));
        }
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p)
    {
        var d = p.HitPoint - new Point(Size / 2, Size / 2);
        return d.Length <= Size / 2 ? new PointHitTestResult(this, p.HitPoint) : null;
    }
}
