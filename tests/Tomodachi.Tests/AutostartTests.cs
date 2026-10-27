using Tomodachi.Core;

namespace Tomodachi.Tests;

/// <summary>Die Aufgabe startet die Exe über cmd, das vorher die Runtime-Injektions-Variablen leert.</summary>
public class AutostartTests
{
    private const string Exe = @"C:\Program Files\tomodachi\tomodachi.exe";

    [Fact]
    public void DescribeProblems_ListsEachPathOnce()
    {
        string[] problems =
        [
            @"C:\Program Files\t\a.dll (HELL\user, owner)",
            @"C:\Program Files\t\a.dll (HELL\user: FullControl)",
            @"C:\Program Files\t\a.dll (HELL\user: FullControl)",
            @"C:\Program Files (x86)\t\b.dll (HELL\user: FullControl)",
        ];

        var text = Autostart.DescribeProblems(problems, n => $"+{n}");

        Assert.Equal(string.Join("\n",
            @"C:\Program Files\t", "    a.dll",
            @"C:\Program Files (x86)\t", "    b.dll"), text);
    }

    [Fact]
    public void DescribeProblems_ShowsTheFolderOnceAndCountsTheRest()
    {
        var problems = Enumerable.Range(1, 8).Select(i => $@"C:\t\f{i}.dll (HELL\user, owner)").ToList();

        var lines = Autostart.DescribeProblems(problems, n => $"+{n}", max: 5).Split('\n');

        Assert.Equal([@"C:\t", "    f1.dll", "    f2.dll", "    f3.dll", "    f4.dll", "    f5.dll", "+3"], lines);
    }

    [Fact]
    public void FirstLines_TruncatesLongOutput()
    {
        Assert.Equal("a\nb\n…", Autostart.FirstLines("a\r\nb\r\nc\r\n", 2));
        Assert.Equal("a", Autostart.FirstLines("a\r\n", 2));
    }

    [Theory]
    [InlineData("CORECLR_ENABLE_PROFILING")]
    [InlineData("CORECLR_PROFILER")]
    [InlineData("CORECLR_PROFILER_PATH")]
    [InlineData("COR_PROFILER")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_ADDITIONAL_DEPS")]
    public void StarterArguments_ClearEachInjectionVariable(string variable)
    {
        Assert.Contains($"set \"{variable}=\"", Autostart.StarterArguments(Exe));
    }

    [Fact]
    public void StarterArguments_StartTheExeLast()
    {
        var args = Autostart.StarterArguments(Exe);

        // /d gegen AutoRun, /s nimmt den Rest wörtlich; die Exe wird zuletzt und in Quotes gestartet
        Assert.StartsWith("/d /s /c \"", args);
        Assert.Contains($"start \"\" \"{Exe}\" --from-task", args);
        Assert.True(args.LastIndexOf("start \"\"", StringComparison.Ordinal) > args.LastIndexOf("set \"", StringComparison.Ordinal));
    }
}
