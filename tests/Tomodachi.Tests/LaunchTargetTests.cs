using System.IO;
using Tomodachi.Core;
using Tomodachi.Launcher;

namespace Tomodachi.Tests;

/// <summary>Gegen die echte Shell und echte Rechte: System32 gehört Admins, %TEMP% dem User.</summary>
public class LaunchTargetTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void AppsFolderEntry_ResolvesToExe()
    {
        // Der „Eingabeaufforderung“-Eintrag: FOLDERID_System + cmd.exe, auf jedem Windows vorhanden
        var app = AppEntry.Create(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\cmd.exe", "Eingabeaufforderung");

        var file = LaunchTarget.ResolveFile(app);

        Assert.NotNull(file);
        Assert.Equal(Cmd, file, ignoreCase: true);
        Assert.True(LaunchTarget.IsTrustedForElevation(file));
    }

    [Fact]
    public void Url_IsNotAFile()
    {
        var app = AppEntry.Create("steam:377160", "Fallout 4") with { Target = "steam://rungameid/377160" };

        Assert.Null(LaunchTarget.ResolveFile(app));
    }

    [Fact]
    public void UrlFile_IsNotAFile()
    {
        var folder = Directory.CreateTempSubdirectory("tomodachi-test-");
        try
        {
            var url = Path.Combine(folder.FullName, "Epic.url");
            File.WriteAllText(url, "[InternetShortcut]\r\nURL=com.epicgames.launcher://apps/x\r\n");
            var app = AppEntry.Create(url, "Epic") with { Target = url };

            Assert.Null(LaunchTarget.ResolveFile(app));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnknownAppsFolderEntry_IsNotAFile()
    {
        var app = AppEntry.Create("tomodachi.test.does-not-exist", "Nichts");

        Assert.Null(LaunchTarget.ResolveFile(app));
    }

    [Fact]
    public void ExeInUserFolder_IsNotTrusted()
    {
        var folder = Directory.CreateTempSubdirectory("tomodachi-test-");
        try
        {
            var exe = Path.Combine(folder.FullName, "evil.exe");
            File.WriteAllBytes(exe, []);

            Assert.False(LaunchTarget.IsTrustedForElevation(exe));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExeInSystemFolder_IsTrusted()
    {
        Assert.True(LaunchTarget.IsTrustedForElevation(Cmd));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void Quote_FollowsWindowsRules(string arg, string expected)
    {
        Assert.Equal(expected, DesktopUser.Quote(arg));
    }

    [Fact]
    public void RunAsArgument_IsParsed()
    {
        var options = StartupOptions.Parse(["--runas", @"C:\Tools\x.exe"]);

        Assert.Equal(@"C:\Tools\x.exe", options.RunAsTarget);
    }
}
