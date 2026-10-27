using Tomodachi.Bar;

namespace Tomodachi.Tests;

public class WindowBarTests
{
    [Theory]
    [InlineData(1600, 3, 200, true)]    // viel Platz links der Notch: volle Breite
    [InlineData(1200, 10, 116, true)]   // eng: schmaler, Titel bleibt
    [InlineData(1200, 20, 44, false)]   // sehr eng: nur Icons
    [InlineData(1200, 0, 200, true)]
    public void TabWidth_ShrinksWithMoreWindows(double available, int count, double width, bool showTitle)
    {
        Assert.Equal((width, showTitle), BarLayout.TabWidth(available, count));
    }

    [Fact]
    public void Order_KeepsKnownWindowsInPlace_AndAppendsNewOnes()
    {
        // Windows meldet nach einem Fokuswechsel eine neue Z-Reihenfolge (3, 1, 2) – die Leiste bleibt bei 1, 2, 3
        Assert.Equal([1, 2, 3], BarLayout.Order([1, 2, 3], [3, 1, 2]));
        Assert.Equal([1, 3, 4], BarLayout.Order([1, 2, 3], [4, 3, 1])); // 2 zu, 4 neu und hinten dran
        Assert.Equal([5, 6], BarLayout.Order([], [5, 6]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]     // Mitte noch über Slot 0
    [InlineData(100, 1)]    // Mitte genau auf der Grenze: rückt eins weiter
    [InlineData(250, 1)]
    [InlineData(310, 2)]
    [InlineData(-80, 0)]    // links raus
    [InlineData(5000, 3)]   // rechts raus: bleibt der letzte
    public void DropIndex_FollowsTheTabCenter(double left, int index)
    {
        Assert.Equal(index, BarLayout.DropIndex(left, 200, 4));
    }
}
