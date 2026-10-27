using Tomodachi.Core;

namespace Tomodachi.Tests;

/// <summary>Die Aufgabe startet die Exe über cmd, das vorher die Runtime-Injektions-Variablen leert.</summary>
public class AutostartTests
{
    private const string Exe = @"C:\Program Files\tomodachi\tomodachi.exe";

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
