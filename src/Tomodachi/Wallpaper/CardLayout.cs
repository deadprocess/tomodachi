using System.Windows;

namespace Tomodachi.Wallpaper;

/// <summary>Maße der schrägen Karten – gemeinsam für das Overlay und die Größe der Vorschaubilder.</summary>
public static class CardLayout
{
    /// <summary>Neigung der Karten; negativ = oben nach rechts gekippt, wie in der Vorlage.</summary>
    public const double Skew = -8;
    public const double CounterSkew = -Skew;
    public const double HeightPerWidth = 1.95;
    public const double Gap = 10;

    public static readonly Thickness CardMargin = new(Gap / 2, 0, Gap / 2, 0);

    /// <summary>Sichtbares Seitenverhältnis einer Karte inklusive der schrägen Überstände, mit etwas Reserve.</summary>
    public static readonly double ThumbnailAspect = 1 / HeightPerWidth + Math.Tan(Math.Abs(Skew) * Math.PI / 180) + 0.06;

    /// <summary>Kartenbreite für eine Bildschirmgröße; gilt für DIPs wie für Pixel, weil nur Verhältnisse eingehen.</summary>
    public static double CardWidth(double screenWidth, double screenHeight) =>
        Math.Min(screenWidth / 7, screenHeight * 0.36 / HeightPerWidth);

    /// <summary>Wie weit eine Karte durch die Schräge oben bzw. unten seitlich übersteht.</summary>
    public static double SkewOverhang(double cardHeight) =>
        cardHeight * Math.Tan(Math.Abs(Skew) * Math.PI / 180) / 2;

    /// <summary>
    /// Höhe, in der die Vorschaubilder dekodiert werden: so hoch wie die größte Karte auf allen Monitoren,
    /// plus Reserve für die vergrößerte Auswahl.
    /// </summary>
    public static int ThumbnailPixelHeight(IEnumerable<(int Width, int Height)> screens) =>
        (int)Math.Ceiling(screens.Max(s => CardWidth(s.Width, s.Height)) * HeightPerWidth * 1.1);
}
