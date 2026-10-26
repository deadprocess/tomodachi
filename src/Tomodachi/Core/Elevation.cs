using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Tomodachi.Core;

internal static class Elevation
{
    private const int ErrorCancelled = 1223;

    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Startet diese Exe mit UAC-Abfrage neu und wartet auf das Ergebnis.</summary>
    public static int RelaunchElevated(params string[] args)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            Log.Info("UAC-Abfrage abgebrochen");
            return 1;
        }
    }
}
