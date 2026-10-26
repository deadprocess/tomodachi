using System.Globalization;
using Tomodachi.Localization;

namespace Tomodachi.Widgets;

/// <summary>Fester Ringpuffer für Verlaufskurven (älteste zuerst).</summary>
internal sealed class History(int capacity)
{
    private readonly Queue<double> _values = new(capacity);

    public void Add(double value)
    {
        if (_values.Count == capacity) _values.Dequeue();
        _values.Enqueue(value);
    }

    public double[] ToArray() => [.. _values];
}

internal sealed record CalendarDay(int Day, bool InMonth, bool IsToday, bool IsWeekend);

internal static class WidgetMath
{
    /// <summary>
    /// Monatsraster ab Montag, immer 6 Wochen × 7 Tage (42 Felder), damit das Kalender-Widget nicht in der Höhe springt.
    /// Tage aus Vor- und Folgemonat sind als <c>InMonth = false</c> markiert.
    /// </summary>
    public static IReadOnlyList<CalendarDay> MonthGrid(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // Montag = 0
        var start = first.AddDays(-offset);

        var days = new List<CalendarDay>(42);
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            days.Add(new CalendarDay(date.Day, date.Month == today.Month, date == today,
                date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        }
        return days;
    }

    /// <summary>1536 → „1,5 KB/s“, 0 → „0 B/s“. Zahlen im Format der aktuellen Kultur.</summary>
    public static string Rate(double bytesPerSecond, CultureInfo? culture = null) => Size(bytesPerSecond, culture) + "/s";

    public static string Size(double bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        var format = unit == 0 || bytes >= 100 ? "0" : "0.0";
        return $"{bytes.ToString(format, culture)} {units[unit]}";
    }

    /// <summary>Verbindungsgeschwindigkeit: 1_000_000_000 → „1 Gbit/s“.</summary>
    public static string LinkSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gbit/s",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:0} Mbit/s",
        > 0 => $"{bitsPerSecond / 1000.0:0} kbit/s",
        _ => "",
    };

    /// <summary>Skala für Netzwerk-Kurven: Maximum der Werte plus Luft, aber nie unter 64 KB/s, sonst zappelt Leerlauf-Rauschen.</summary>
    public static double AutoScale(IEnumerable<double> values) => Math.Max(64 * 1024, values.DefaultIfEmpty(0).Max() * 1.2);

    public static string Duration(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    /// <summary>Laufzeit grob, zwei Stellen: „2 Tagen 3 Std“ / „2 days, 3 h“, „3 Std 12 Min“, „12 Min“.</summary>
    public static string Uptime(TimeSpan time, CultureInfo? culture = null)
    {
        culture ??= Loc.Culture;
        var key = time switch
        {
            { Days: 1 } => "Uptime_OneDay",
            { Days: > 1 } => "Uptime_Days",
            { Hours: > 0 } => "Uptime_Hours",
            _ => "Uptime_Minutes",
        };
        return time.Days > 0
            ? Loc.Format(culture, key, time.Days, time.Hours)
            : Loc.Format(culture, key, time.Hours > 0 ? time.Hours : time.Minutes, time.Minutes);
    }
}
