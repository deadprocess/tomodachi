using System.Globalization;
using System.IO;
using Tomodachi.Localization;
using Tomodachi.Shell;
using Tomodachi.Widgets;

namespace Tomodachi.Tests;

public class PowerMenuTests
{
    private static readonly DateTime T0 = new(2026, 10, 26, 21, 30, 0, DateTimeKind.Utc);
    private readonly ConfirmGate _gate = new(TimeSpan.FromSeconds(3));

    [Theory]
    [InlineData(PowerAction.Shutdown)]
    [InlineData(PowerAction.Restart)]
    [InlineData(PowerAction.SignOut)]
    public void SessionEnding_NeedsSecondClick(PowerAction action)
    {
        Assert.False(_gate.Press(action, T0));
        Assert.Equal(action, _gate.Armed);
        Assert.True(_gate.Press(action, T0.AddSeconds(1)));
        Assert.Null(_gate.Armed);
    }

    [Theory]
    [InlineData(PowerAction.Lock)]
    [InlineData(PowerAction.Sleep)]
    public void LockAndSleep_RunImmediately(PowerAction action)
    {
        Assert.True(_gate.Press(action, T0));
    }

    [Fact]
    public void SecondClick_TooLate_OnlyArmsAgain()
    {
        Assert.False(_gate.Press(PowerAction.Shutdown, T0));
        Assert.False(_gate.Press(PowerAction.Shutdown, T0.AddSeconds(4)));
        Assert.True(_gate.Press(PowerAction.Shutdown, T0.AddSeconds(5)));
    }

    [Fact]
    public void OtherAction_InBetween_DoesNotConfirm()
    {
        // Neustart scharf, dann Herunterfahren: das ist ein neuer erster Klick, kein Bestätigen
        Assert.False(_gate.Press(PowerAction.Restart, T0));
        Assert.False(_gate.Press(PowerAction.Shutdown, T0.AddSeconds(1)));
        Assert.Equal(PowerAction.Shutdown, _gate.Armed);
    }

    [Fact]
    public void Disarm_ForgetsFirstClick()
    {
        Assert.False(_gate.Press(PowerAction.Shutdown, T0));
        _gate.Disarm();
        Assert.False(_gate.Press(PowerAction.Shutdown, T0.AddSeconds(1)));
    }

    [Theory]
    [InlineData("de-DE", 0, 12, "12 Min")]
    [InlineData("de-DE", 3, 5, "3 Std 5 Min")]
    [InlineData("de-DE", 26, 40, "1 Tag 2 Std")]
    [InlineData("de-DE", 75, 0, "3 Tagen 3 Std")]
    [InlineData("en-US", 0, 12, "12 min")]
    [InlineData("en-US", 3, 5, "3 h 5 min")]
    [InlineData("en-US", 26, 40, "1 day, 2 h")]
    [InlineData("en-US", 75, 0, "3 days, 3 h")]
    public void Uptime_IsCoarse_InBothLanguages(string culture, int hours, int minutes, string expected)
    {
        Assert.Equal(expected, WidgetMath.Uptime(new TimeSpan(hours, minutes, 30), CultureInfo.GetCultureInfo(culture)));
    }
}

public class LocalizationTests
{
    [Theory]
    [InlineData("", "de-DE", "de-DE")]   // wie Windows: deutsch
    [InlineData("", "fr-FR", "en-US")]   // wie Windows: alles außer Deutsch → Englisch
    [InlineData("en", "de-DE", "en-US")] // fest gewählt schlägt Windows
    [InlineData("DE", "en-US", "de-DE")]
    public void Resolve_PicksGermanOrEnglish(string setting, string windows, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(windows)));

    [Fact]
    public void EveryKey_ExistsInBothLanguages()
    {
        var neutral = Keys("Strings.resx");
        var german = Keys("Strings.de.resx");
        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Order(), german.Order());
    }

    [Theory]
    [InlineData("de-DE", 10, "2 7   O K T   2 0 2 6")]
    [InlineData("en-US", 10, "2 7   O C T   2 0 2 6")]
    [InlineData("de-DE", 3, "2 7   M Ä R Z   2 0 2 6")] // Windows kürzt „März“ nicht weiter, wie auch Juni, Juli, Sept
    [InlineData("de-DE", 5, "2 7   M A I   2 0 2 6")]
    public void WallpaperDate_FollowsUiLanguage(string culture, int month, string expected) =>
        Assert.Equal(expected, Tomodachi.Wallpaper.WallpaperOverlay.FormatDate(new DateTime(2026, month, 27), CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Texts_DifferPerLanguage()
    {
        Assert.Equal("Fenster", Loc.Get("Tab_Windows", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("Windows", Loc.Get("Tab_Windows", CultureInfo.GetCultureInfo("en-US")));
    }

    private static List<string> Keys(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Tomodachi", "Localization", file);
        return System.Xml.Linq.XDocument.Load(path).Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToList();
    }
}
