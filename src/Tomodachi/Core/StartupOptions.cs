namespace Tomodachi.Core;

internal sealed record StartupOptions
{
    /// <summary>Nur UI, keine Eingriffe ins System (kein Hook, Taskleiste bleibt).</summary>
    public bool Safe { get; init; }

    /// <summary>Laufende Instanz sauber beenden.</summary>
    public bool Quit { get; init; }

    /// <summary>Notch der laufenden Instanz im Widgets-Reiter auf- oder zuklappen.</summary>
    public bool ToggleWidgets { get; init; }

    /// <summary>Notch der laufenden Instanz im Fenster-Reiter auf- oder zuklappen.</summary>
    public bool ToggleWindows { get; init; }

    /// <summary>Notch der laufenden Instanz im Power-Reiter auf- oder zuklappen.</summary>
    public bool TogglePower { get; init; }

    /// <summary>Notch der laufenden Instanz im Einstellungs-Reiter auf- oder zuklappen.</summary>
    public bool ToggleSettings { get; init; }

    /// <summary>Vom Einstellungs-Reiter gestartet: warten, bis die alte Instanz beendet ist, dann normal starten.</summary>
    public bool Restart { get; init; }
    public const string RestartArgument = "--restart";

    /// <summary>Wallpaper-Picker der laufenden Instanz öffnen oder schließen (wie Win+Strg+W).</summary>
    public bool ToggleWallpaper { get; init; }

    /// <summary>Alle Systemänderungen zurücknehmen, auch ohne Journal-Eintrag.</summary>
    public bool Restore { get; init; }

    /// <summary>Interner Modus: diesen Prozess überwachen und nach einem Absturz aufräumen.</summary>
    public int? WatchdogPid { get; init; }

    /// <summary>Autostart-Aufgabe mit höchsten Rechten anlegen bzw. entfernen.</summary>
    public bool Install { get; init; }
    public bool Uninstall { get; init; }

    /// <summary>Von der Aufgabenplanung gestartet: nie erneut über die Aufgabe neu starten (Endlosschleife).</summary>
    public bool FromTask { get; init; }

    /// <summary>
    /// Interner Modus mit Userrechten: das Ziel per „runas“ starten, damit Windows die UAC-Abfrage zeigt
    /// (<see cref="Launcher.AppLauncher"/>, wenn tomodachi selbst elevated läuft).
    /// </summary>
    public string? RunAsTarget { get; init; }
    public const string RunAsArgument = "--runas";
    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--safe": options = options with { Safe = true }; break;
                case "--quit": options = options with { Quit = true }; break;
                case "--widgets": options = options with { ToggleWidgets = true }; break;
                case "--windows": options = options with { ToggleWindows = true }; break;
                case "--power": options = options with { TogglePower = true }; break;
                case "--settings": options = options with { ToggleSettings = true }; break;
                case RestartArgument: options = options with { Restart = true }; break;
                case "--wallpaper": options = options with { ToggleWallpaper = true }; break;
                case "--restore": options = options with { Restore = true }; break;
                case "--install": options = options with { Install = true }; break;
                case "--uninstall": options = options with { Uninstall = true }; break;
                case Autostart.FromTaskArgument: options = options with { FromTask = true }; break;
                case "--watchdog" when i + 1 < args.Length && int.TryParse(args[i + 1], out var pid):
                    options = options with { WatchdogPid = pid };
                    i++;
                    break;
                case RunAsArgument when i + 1 < args.Length:
                    options = options with { RunAsTarget = args[i + 1] };
                    i++;
                    break;
            }
        }
        return options;
    }
}
