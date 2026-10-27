using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Tomodachi.Core;

internal static class Elevation
{
    private const int ErrorCancelled = 1223;

    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>
    /// Für <c>--runas</c>: ein beliebiges Ziel mit Adminrechten starten. Dieser Prozess läuft mit Userrechten,
    /// deshalb zeigt Windows die UAC-Abfrage. 0 = gestartet, 1 = abgebrochen oder fehlgeschlagen.
    /// </summary>
    public static int RunAsWithPrompt(string target)
    {
        Interop.Native.AllowSetForegroundWindow(Interop.Native.ASFW_ANY);
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            Log.Info($"runas: {target} gestartet");
            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            Log.Info($"runas: {target} abgebrochen (UAC)");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Error($"runas: {target} fehlgeschlagen", ex);
            return 1;
        }
    }

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
