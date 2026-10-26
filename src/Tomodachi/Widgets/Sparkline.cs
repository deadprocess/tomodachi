using System.Windows;
using System.Windows.Media;

namespace Tomodachi.Widgets;

/// <summary>Kleine Verlaufskurve: Linie plus halbtransparente Fläche darunter. Werte von 0 bis <see cref="Maximum"/>.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Wie viele Punkte die volle Breite ausmachen. Neue Werte laufen von rechts ein.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[] Values
    {
        get => (double[])GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        if (values.Length < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var step = ActualWidth / Math.Max(1, Capacity - 1);
        var startX = ActualWidth - (values.Length - 1) * step;
        var max = Maximum <= 0 ? 1 : Maximum;
        Point At(int i) => new(startX + i * step, ActualHeight - Math.Clamp(values[i] / max, 0, 1) * (ActualHeight - 1));

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(0), false, false);
            for (var i = 1; i < values.Length; i++) g.LineTo(At(i), true, true);
        }
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(startX, ActualHeight), true, true);
            for (var i = 0; i < values.Length; i++) g.LineTo(At(i), false, false);
            g.LineTo(new Point(ActualWidth, ActualHeight), false, false);
        }
        line.Freeze();
        area.Freeze();

        var fill = Stroke.Clone();
        fill.Opacity = 0.18;
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round }, line);
    }
}
