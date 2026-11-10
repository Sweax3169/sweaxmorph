using System.Windows;
using System.Windows.Media;

namespace SweaxMorph.UI;

/// <summary>
/// The little wave under the format search. It is redrawn into its own drawing as it flows; a Path with new data
/// every frame would run a layout pass each time.
/// </summary>
public sealed class WaveLine : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(WaveLine),
        new PropertyMetadata(null, (d, _) => { var w = (WaveLine)d; w._pen = null; w.Draw(); }));

    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    readonly DrawingGroup _drawing = new();
    Pen? _pen;
    double _time;

    /// <summary>Seconds on the Well's clock; setting it moves the wave.</summary>
    public double Time { get => _time; set { _time = value; Draw(); } }

    protected override void OnRender(DrawingContext dc)
    {
        Draw();
        dc.DrawDrawing(_drawing);
    }

    void Draw()
    {
        using var dc = _drawing.Open();
        if (Stroke is not { } stroke) return;
        if (_pen == null)
        {
            _pen = new Pen(stroke, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _pen.Freeze();
        }
        double w = Math.Max(10, ActualWidth > 0 ? ActualWidth : 480);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 4), false, false);
            for (double x = 4; x <= w; x += 4) c.LineTo(new Point(x, 4 + Math.Sin(x * 0.09 - _time * 5) * 2.2), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, _pen, g);
    }
}
