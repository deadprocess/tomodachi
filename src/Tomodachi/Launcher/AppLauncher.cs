using System.ComponentModel;
using System.Diagnostics;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Launcher;

internal static class AppLauncher
{
    /// <summary>
    /// Startet eine App über <c>shell:AppsFolder</c>, ihre Verknüpfung oder ihre URL (<see cref="AppEntry.LaunchTarget"/>).
    /// Läuft die Shell elevated, geht der Start über explorer.exe:
    /// Der neue Explorer-Prozess reicht den Aufruf an die laufende Explorer-Instanz des Users weiter,
    /// und die App startet <b>ohne</b> Adminrechte. Mit <paramref name="elevated"/> bewusst mit Adminrechten.
    /// </summary>
    public static bool Launch(AppEntry app, bool elevated)
    {
        var target = app.LaunchTarget;
        if (elevated && target.Contains("://", StringComparison.Ordinal))
        {
            // „runas“ gibt es für Protokoll-URLs nicht (Fehler 1155). Was steam:// oder epic:// startet, entscheidet
            // ohnehin der Launcher dahinter; also normal starten statt still zu scheitern.
            Log.Info($"'{app.Name}': {target} ist eine URL, Adminrechte vergibt der Protokoll-Handler. Starte normal");
            elevated = false;
        }
        Native.AllowSetForegroundWindow(Native.ASFW_ANY); // sonst landet die App womöglich hinter dem alten Fenster

        try
        {
            ProcessStartInfo info;
            if (Elevation.IsElevated && !elevated)
            {
                info = new ProcessStartInfo(Paths.Explorer) { UseShellExecute = false };
                info.ArgumentList.Add(target);
            }
            else if (Elevation.IsElevated && !TrustedForSilentElevation(app))
            {
                // Aus dem elevated Prozess fragt „runas“ nicht mehr nach. Für Ziele, die auch ein Programm ohne Adminrechte
                // ändern könnte, soll Windows aber fragen: also ein tomodachi mit Userrechten starten, das „runas“ ausführt.
                DesktopUser.Start(Environment.ProcessPath!, StartupOptions.RunAsArgument, target);
                Log.Info($"Starte '{app.Name}' als Admin mit UAC-Abfrage ({app.Id})");
                return true;
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

    /// <summary>Ohne UAC-Abfrage nur, was ein Nicht-Admin nicht austauschen kann (siehe <see cref="LaunchTarget"/>).</summary>
    private static bool TrustedForSilentElevation(AppEntry app)
    {
        var file = LaunchTarget.ResolveFile(app);
        if (file is null)
        {
            Log.Info($"'{app.Name}': Ziel {app.LaunchTarget} ist keine prüfbare Datei, Admin-Start nur mit UAC-Abfrage");
            return false;
        }
        if (LaunchTarget.IsTrustedForElevation(file)) return true;
        Log.Info($"'{app.Name}': {file} dürfen auch Nicht-Admins ändern, Admin-Start nur mit UAC-Abfrage");
        return false;
    }
}
