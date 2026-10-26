using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Launcher;

internal static class AppLauncher
{
    private static readonly string Explorer =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>
    /// Startet eine App über <c>shell:AppsFolder</c>, ihre Verknüpfung oder ihre URL (<see cref="AppEntry.LaunchTarget"/>).
    /// Läuft die Shell elevated, geht der Start über explorer.exe:
    /// Der neue Explorer-Prozess reicht den Aufruf an die laufende Explorer-Instanz des Users weiter,
    /// und die App startet <b>ohne</b> Adminrechte. Mit <paramref name="elevated"/> bewusst mit Adminrechten.
    /// </summary>
    public static bool Launch(AppEntry app, bool elevated)
    {
        var target = app.LaunchTarget;
        Native.AllowSetForegroundWindow(Native.ASFW_ANY); // sonst landet die App womöglich hinter dem alten Fenster

        try
        {
            ProcessStartInfo info;
            if (Elevation.IsElevated && !elevated)
            {
                info = new ProcessStartInfo(Explorer) { UseShellExecute = false };
                info.ArgumentList.Add(target);
            }
            else
            {
                // „runas“ auch, wenn tomodachi schon elevated läuft: dann ohne UAC-Abfrage, aber garantiert mit Adminrechten
                info = new ProcessStartInfo(target)
                {
                    UseShellExecute = true,
                    Verb = elevated ? "runas" : "",
                };
            }

            Process.Start(info)?.Dispose();
            Log.Info($"Starte '{app.Name}'{(elevated ? " als Admin" : "")} ({app.Id})");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Info($"Start von '{app.Name}' als Admin abgebrochen (UAC)");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Start von '{app.Name}' fehlgeschlagen", ex);
            return false;
        }
    }
}
