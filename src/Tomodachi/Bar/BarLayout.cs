namespace Tomodachi.Bar;

/// <summary>Rechenregeln der Fensterleiste, ohne UI und damit testbar.</summary>
internal static class BarLayout
{
    public const double MaxTabWidth = 200;
    public const double MinTabWidthWithTitle = 96;
    public const double IconOnlyWidth = 44;
    public const double TabGap = 4;

    /// <summary>
    /// Breite pro Tab: höchstens 200, bei vielen Fenstern schmaler. Unter 96 lohnt der Titel nicht mehr,
    /// dann nur noch das Icon (44).
    /// </summary>
    public static (double Width, bool ShowTitle) TabWidth(double available, int count)
    {
        if (count <= 0) return (MaxTabWidth, true);
        var width = available / count - TabGap;
        if (width >= MaxTabWidth) return (MaxTabWidth, true);
        if (width >= MinTabWidthWithTitle) return (Math.Floor(width), true);
        return (IconOnlyWidth, false);
    }

    /// <summary>
    /// Stabile Reihenfolge wie in der Taskleiste: Bekannte Fenster behalten ihren Platz, geschlossene fallen weg,
    /// neue kommen hinten dran. Die Z-Reihenfolge von Windows wechselt bei jedem Fokuswechsel, darauf soll die Leiste
    /// nicht herumspringen.
    /// </summary>
    public static List<nint> Order(IReadOnlyList<nint> previous, IReadOnlyList<nint> current)
    {
        var alive = current.ToHashSet();
        var ordered = previous.Where(alive.Contains).ToList();
        var known = ordered.ToHashSet();
        ordered.AddRange(current.Where(known.Add));
        return ordered;
    }
}
