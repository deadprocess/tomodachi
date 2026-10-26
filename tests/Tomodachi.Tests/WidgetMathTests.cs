using System.Globalization;
using Tomodachi.Widgets;

namespace Tomodachi.Tests;

public class WidgetMathTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void MonthGrid_October2026_StartsMondayBeforeFirst()
    {
        // 1. Oktober 2026 ist ein Donnerstag, das Raster beginnt also mit Montag, 28. September
        var grid = WidgetMath.MonthGrid(new DateOnly(2026, 10, 26));
        Assert.Equal(42, grid.Count);
        Assert.Equal(28, grid[0].Day);
        Assert.False(grid[0].InMonth);
        Assert.Equal(1, grid[3].Day);
        Assert.True(grid[3].InMonth);
    }

    [Fact]
    public void MonthGrid_MarksTodayAndWeekend()
    {
        var grid = WidgetMath.MonthGrid(new DateOnly(2026, 10, 26));
        var today = Assert.Single(grid, d => d.IsToday);
        Assert.Equal(26, today.Day);
        Assert.True(grid[5].IsWeekend);  // Samstag
        Assert.True(grid[6].IsWeekend);  // Sonntag
        Assert.False(grid[4].IsWeekend); // Freitag
    }

    [Fact]
    public void MonthGrid_MonthStartingOnMonday_HasNoLeadingDays()
    {
        var grid = WidgetMath.MonthGrid(new DateOnly(2026, 6, 10)); // 1. Juni 2026 ist ein Montag
        Assert.Equal(1, grid[0].Day);
        Assert.True(grid[0].InMonth);
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(1536, "1,5 KB/s")]
    [InlineData(150 * 1024, "150 KB/s")]
    [InlineData(12.5 * 1024 * 1024, "12,5 MB/s")]
    public void Rate(double bytes, string expected) => Assert.Equal(expected, WidgetMath.Rate(bytes, German));

    [Theory]
    [InlineData(1_000_000_000L, "1 Gbit/s")]
    [InlineData(2_500_000_000L, "2,5 Gbit/s")]
    [InlineData(100_000_000L, "100 Mbit/s")]
    [InlineData(0L, "")]
    public void LinkSpeed(long bits, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = German;
        try
        {
            Assert.Equal(expected, WidgetMath.LinkSpeed(bits));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AutoScale_HasFloorAgainstIdleNoise() => Assert.Equal(64 * 1024, WidgetMath.AutoScale([10, 200]));

    [Fact]
    public void AutoScale_AddsHeadroom() => Assert.Equal(1.2 * 1_000_000, WidgetMath.AutoScale([1_000_000, 5]), precision: 3);

    [Fact]
    public void History_KeepsNewestUpToCapacity()
    {
        var history = new History(3);
        foreach (var v in new double[] { 1, 2, 3, 4 }) history.Add(v);
        Assert.Equal([2, 3, 4], history.ToArray());
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Duration(int seconds, string expected) => Assert.Equal(expected, WidgetMath.Duration(TimeSpan.FromSeconds(seconds)));
}
