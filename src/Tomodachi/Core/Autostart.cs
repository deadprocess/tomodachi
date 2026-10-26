using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace Tomodachi.Core;

/// <summary>
/// Paket 1.4: eine Aufgabe in der Aufgabenplanung, die tomodachi bei der Anmeldung mit höchsten Rechten startet.
/// Über <c>schtasks /Run</c> kann auch ein normaler Start ohne UAC-Abfrage auf die elevated Variante umsteigen.
/// Elevated sieht der Tastatur-Hook auch Tasten, während ein Admin-Fenster den Fokus hat (UIPI).
/// </summary>
internal static class Autostart
{
    private const string TaskName = "tomodachi";
    public const string FromTaskArgument = "--from-task";

    static Autostart() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Für <c>--install</c> / <c>--uninstall</c>. Holt sich bei Bedarf selbst Adminrechte.</summary>
    public static int RunCli(bool install)
    {
        if (!Elevation.IsElevated) return Elevation.RelaunchElevated(install ? "--install" : "--uninstall");

        var (exitCode, output) = install ? Install() : Schtasks("/Delete", "/TN", TaskName, "/F");
        var action = install ? "Autostart mit Adminrechten eingerichtet" : "Autostart entfernt";
        if (exitCode == 0)
        {
            Log.Info($"{action}: {output}");
            System.Windows.MessageBox.Show(Localization.Loc.Get(install ? "Autostart_Installed" : "Autostart_Removed"), "tomodachi");
        }
        else
        {
            Log.Error($"schtasks fehlgeschlagen ({exitCode}): {output}");
            System.Windows.MessageBox.Show($"{Localization.Loc.Format("Autostart_Failed", exitCode)}\n{output}", "tomodachi");
        }
        return exitCode;
    }

    /// <summary>True, wenn die Aufgabe existiert und auf genau diese Exe zeigt.</summary>
    public static bool IsInstalledForThisExe()
    {
        var (exitCode, output) = Schtasks("/Query", "/TN", TaskName, "/XML");
        if (exitCode != 0) return false;

        try
        {
            var command = XDocument.Parse(output).Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            return string.Equals(command?.Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Aufgaben-XML unlesbar", ex);
            return false;
        }
    }

    public static bool RunTask()
    {
        var (exitCode, output) = Schtasks("/Run", "/TN", TaskName);
        if (exitCode != 0) Log.Warn($"schtasks /Run fehlgeschlagen ({exitCode}): {output}");
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) Install()
    {
        var exe = Environment.ProcessPath!;
        var user = WindowsIdentity.GetCurrent().Name;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>tomodachi Shell: Start bei Anmeldung mit höchsten Rechten</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{SecurityElement.Escape(exe)}"</Command>
                  <Arguments>{FromTaskArgument}</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
        // Ohne Priority 4 läuft die Aufgabe mit „below normal“ (7), und die UI würde ruckeln

        var file = Path.Combine(Path.GetTempPath(), $"tomodachi-task-{Environment.ProcessId}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            return Schtasks("/Create", "/TN", TaskName, "/XML", file, "/F");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static (int ExitCode, string Output) Schtasks(params string[] args)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, (stdout.Result + stderr).Trim());
    }
}
