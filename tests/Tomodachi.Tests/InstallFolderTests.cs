using System.IO;
using Tomodachi.Core;

namespace Tomodachi.Tests;

/// <summary>Gegen echte Rechte im Dateisystem: %TEMP% darf der User ändern, System32\drivers\etc nur TrustedInstaller und Admins.</summary>
public class InstallFolderTests
{
    [Fact]
    public void UserTempFolder_IsWritable()
    {
        var folder = Directory.CreateTempSubdirectory("tomodachi-test-");
        try
        {
            var exe = Path.Combine(folder.FullName, "tomodachi.exe");
            File.WriteAllBytes(exe, []);

            var problems = InstallFolder.FindWritable(exe);

            Assert.Contains(problems, p => p.StartsWith(exe + " ", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(problems, p => p.StartsWith(folder.FullName + " ", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void SystemFolder_IsSafe()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "tomodachi.exe");

        Assert.Empty(InstallFolder.FindWritable(exe));
    }

    [Fact]
    public void IsUnderProgramFiles_MatchesProgramFilesAndSubfolders()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.True(InstallFolder.IsUnderProgramFiles(programFiles));
        Assert.True(InstallFolder.IsUnderProgramFiles(programFiles + @"\"));
        Assert.True(InstallFolder.IsUnderProgramFiles(Path.Combine(programFiles, "tomodachi-1.6.0")));
        Assert.True(InstallFolder.IsUnderProgramFiles(Path.Combine(programFiles, "tomodachi", "dist", "tomodachi-1.6.1")));
        Assert.True(InstallFolder.IsUnderProgramFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "tomodachi")));
    }

    [Fact]
    public void IsUnderProgramFiles_RejectsLookalikesAndOtherFolders()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.False(InstallFolder.IsUnderProgramFiles(programFiles + "-fake"));
        Assert.False(InstallFolder.IsUnderProgramFiles(Path.GetTempPath()));
        Assert.False(InstallFolder.IsUnderProgramFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "tomodachi")));
    }
}
