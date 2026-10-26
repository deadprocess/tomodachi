using Tomodachi.Launcher;

namespace Tomodachi.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("firefox", "Firefox", FuzzyMatcher.Exact)]
    [InlineData("fire", "Firefox", FuzzyMatcher.Prefix - 3)]
    [InlineData("code", "Visual Studio Code", FuzzyMatcher.WordPrefix - 20)]
    [InlineData("vsc", "Visual Studio Code", FuzzyMatcher.Acronym)]
    [InlineData("udio", "Visual Studio Code", FuzzyMatcher.Substring - 9)]
    public void Tiers(string query, string candidate, int expected) =>
        Assert.Equal(expected, FuzzyMatcher.Score(query, candidate));

    [Theory]
    [InlineData("vscode", "Visual Studio Code")]
    [InlineData("ffx", "Firefox")]
    public void Subsequence_Matches(string query, string candidate)
    {
        var score = FuzzyMatcher.Score(query, candidate);
        Assert.NotNull(score);
        Assert.InRange(score.Value, FuzzyMatcher.Subsequence - 99, FuzzyMatcher.Subsequence);
    }

    [Theory]
    [InlineData("xyz", "Firefox")]
    [InlineData("a", "Firefox")]       // einzelnes Zeichen nur als Prefix/Substring
    [InlineData("aeiou", "Visual Studio Code")] // zu zerfasert
    public void NoMatch(string query, string candidate) => Assert.Null(FuzzyMatcher.Score(query, candidate));

    [Theory]
    [InlineData("uber", "Über Windows")]
    [InlineData("GROSS", "Großschreibung")]
    [InlineData("  fire  ", "Firefox")]
    public void Normalization(string query, string candidate) => Assert.NotNull(FuzzyMatcher.Score(query, candidate));

    [Fact]
    public void EmptyQuery_MatchesEverything() => Assert.Equal(0, FuzzyMatcher.Score("", "Firefox"));
}

public class AppEntryTests
{
    [Theory]
    [InlineData("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "Windows Notepad")]
    [InlineData("{6D809377-6AF0-444B-8957-A3773F02200E}\\Mozilla Firefox\\firefox.exe", "firefox")]
    [InlineData("MSEdge", "MS Edge")]
    [InlineData("Microsoft.Windows.Explorer", "Explorer")]
    public void DeriveAlias(string id, string expected) => Assert.Equal(expected, AppEntry.DeriveAlias(id));

    [Fact]
    public void AliasEqualToName_IsDropped() =>
        Assert.Null(AppEntry.Create("{GUID}\\Firefox\\firefox.exe", "Firefox").Alias);
}

public class LauncherSearchTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly AppEntry[] Apps =
    [
        AppEntry.Create("{GUID}\\Microsoft VS Code\\Code.exe", "Visual Studio Code"),
        AppEntry.Create("{GUID}\\Microsoft Visual Studio\\devenv.exe", "Visual Studio 2022"),
        AppEntry.Create("{GUID}\\VideoLAN\\vlc.exe", "VLC media player"),
        AppEntry.Create("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "Editor"),
        AppEntry.Create("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Rechner"),
        AppEntry.Create("{GUID}\\Mozilla Firefox\\firefox.exe", "Firefox"),
        AppEntry.Create("{GUID}\\Mozilla Firefox\\private_browsing.exe", "Firefox Private Browsing"),
    ];

    private static IReadOnlyList<string> Names(string query, LaunchHistory? history = null, int limit = 8) =>
        LauncherSearch.Search(query, Apps, history ?? LaunchHistory.InMemory(), Now, limit).Select(a => a.Name).ToList();

    [Fact]
    public void Vsc_FindsVsCodeFirst() => Assert.Equal("Visual Studio Code", Names("vsc")[0]);

    [Fact]
    public void Notepad_FindsGermanEditorViaAlias() => Assert.Equal("Editor", Names("notepad")[0]);

    [Fact]
    public void Calculator_FindsRechnerViaAlias() => Assert.Equal("Rechner", Names("calc")[0]);

    [Fact]
    public void ShorterNameWinsTie() => Assert.Equal(["Firefox", "Firefox Private Browsing"], Names("firefox").Take(2));

    [Fact]
    public void NonMatches_AreFiltered() => Assert.DoesNotContain("Rechner", Names("vis"));

    [Fact]
    public void Frecency_ReordersWithinTier()
    {
        var history = LaunchHistory.InMemory();
        for (var i = 0; i < 5; i++) history.RecordLaunch(Apps[1].Id, Now); // Visual Studio 2022

        Assert.Equal("Visual Studio 2022", Names("visual", history)[0]);
    }

    [Fact]
    public void Frecency_DoesNotBeatExactMatchFromOneOldLaunch()
    {
        var history = LaunchHistory.InMemory();
        history.RecordLaunch(Apps[6].Id, Now.AddDays(-30)); // Firefox Private Browsing, vor einem Monat

        Assert.Equal("Firefox", Names("firefox", history)[0]);
    }

    [Fact]
    public void EmptyQuery_RecentFirstThenAlphabetical()
    {
        var history = LaunchHistory.InMemory();
        history.RecordLaunch(Apps[4].Id, Now); // Rechner

        var names = Names("", history);
        Assert.Equal("Rechner", names[0]);
        Assert.Equal("Editor", names[1]);
    }

    [Fact]
    public void Limit_IsRespected() => Assert.Equal(3, Names("", limit: 3).Count);

    [Fact]
    public void FrecencyDecays()
    {
        var history = LaunchHistory.InMemory();
        history.RecordLaunch("x", Now);
        var fresh = history.Frecency("x", Now);
        var old = history.Frecency("x", Now.AddDays(14));
        Assert.Equal(fresh / 2, old, precision: 6);
        Assert.InRange(fresh, 1, LaunchHistory.MaxBoost);
    }
}
