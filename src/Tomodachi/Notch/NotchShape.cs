using System.Windows;
using System.Windows.Media;

namespace Tomodachi.Notch;

/// <summary>
/// Notch-Umriss: oben bündig an der Bildschirmkante, mit konkaven „Ohren“ links und rechts oben
/// (wie bei der MacBook-Notch) und abgerundeten unteren Ecken. Die Ohren liegen außerhalb des Körpers,
/// die Gesamtbreite ist also Körperbreite + 2 × <see cref="EarRadius"/>.
/// </summary>
public sealed class NotchShape : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(NotchShape),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(NotchShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EarRadiusProperty = DependencyProperty.Register(
        nameof(EarRadius), typeof(double), typeof(NotchShape),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(NotchShape),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double EarRadius
    {
        get => (double)GetValue(EarRadiusProperty);
        set => SetValue(EarRadiusProperty, value);
    }

    /// <summary>Animierbar. Beim Ausklappen (Paket 2.4) wächst der Radius mit.</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var ear = Math.Min(EarRadius, Math.Min(w / 4, h));
        var corner = Math.Min(CornerRadius, Math.Min((w - 2 * ear) / 2, h - ear));
        if (w <= 0 || h <= 0) return;

        dc.DrawGeometry(Fill, null, Outline(w, h, ear, corner, closed: true));

        // Kontur als offener Pfad: an der Oberkante (Bildschirmrand) soll keine Linie entstehen
        if (Stroke is not null)
            dc.DrawGeometry(null, new Pen(Stroke, 1), Outline(w, h, ear, corner, closed: false));
    }

    private static StreamGeometry Outline(double w, double h, double ear, double corner, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, 0), isFilled: closed, isClosed: closed);
            // linkes Ohr: konkav von der Kante in die Seitenwand
            g.ArcTo(new Point(ear, ear), new Size(ear, ear), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(ear, h - corner), true, true);
            g.ArcTo(new Point(ear + corner, h), new Size(corner, corner), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(w - ear - corner, h), true, true);
            g.ArcTo(new Point(w - ear, h - corner), new Size(corner, corner), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(w - ear, ear), true, true);
            // rechtes Ohr
            g.ArcTo(new Point(w, 0), new Size(ear, ear), 0, false, SweepDirection.Clockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }
}
