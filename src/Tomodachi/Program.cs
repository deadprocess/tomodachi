using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Safety;

namespace Tomodachi;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);

        if (options.WatchdogPid is int pid)
        {
            Log.Init("watchdog");
            return Watchdog.Run(pid);
        }

        if (options.RunAsTarget is string runAs)
        {
            Log.Init("cli");
            return Elevation.RunAsWithPrompt(runAs);
        }

        if (options.Install || options.Uninstall)
        {
            Log.Init("cli");
            Localization.Loc.Apply(ConfigStore.Load().Language); // für die Bestätigungsmeldung
            return Autostart.RunCli(options.Install);
        }

        (bool Wanted, string Command)[] signals =
        [
            (options.Quit, ShellCommands.Quit),
            (options.ToggleWidgets, ShellCommands.ToggleWidgets),
            (options.ToggleWindows, ShellCommands.ToggleWindows),
            (options.TogglePower, ShellCommands.TogglePower),
            (options.ToggleSettings, ShellCommands.ToggleSettings),
            (options.ToggleWallpaper, ShellCommands.ToggleWallpaper),
        ];
        if (options.Restore || signals.Any(s => s.Wanted))
        {
            Log.Init("cli");
            if (options.Restore) SystemRestore.RestoreAll("--restore", force: true);
            // Das Wallpaper-Overlay braucht den Vordergrund. Den darf die laufende Instanz nur nehmen, wenn wir ihn freigeben.
            if (options.ToggleWallpaper) Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            foreach (var (_, command) in signals.Where(s => s.Wanted))
            {
                if (!SingleInstance.Signal(command)) Log.Info($"--{command}: keine laufende Instanz");
            }
            return 0;
        }

        Log.Init("tomodachi");
        var instance = options.Restart ? SingleInstance.WaitAndAcquire(TimeSpan.FromSeconds(15)) : SingleInstance.TryAcquire();
        if (instance is null)
        {
            Log.Info("Läuft bereits, beende zweite Instanz");
            return 1;
        }

        try
        {
            if (!options.Safe && !options.FromTask && !Elevation.IsElevated && Autostart.IsInstalledForThisExe())
            {
                instance.Dispose(); // Platz machen für die elevated Instanz
                if (Autostart.RunTask())
                {
                    Log.Info("Starte elevated über die Aufgabenplanung neu");
                    return 0;
                }
                instance = SingleInstance.TryAcquire();
                if (instance is null) return 1;
            }

            return RunShell(options, instance);
        }
        finally
        {
            instance?.Dispose();
        }
    }

    private static int RunShell(StartupOptions options, SingleInstance instance)
    {
        Log.Info($"Start {Environment.ProcessPath} (PID {Environment.ProcessId}, " +
                 $"{(Elevation.IsElevated ? "elevated" : "ohne Adminrechte")}){(options.Safe ? " im Safe-Modus" : "")}");
        if (!Elevation.IsElevated && !options.Safe)
            Log.Warn("Ohne Adminrechte sieht der Hook keine Tasten, solange ein Admin-Fenster den Fokus hat. Abhilfe: tomodachi --install");
        if (Elevation.IsElevated && Autostart.WritableByNonAdmins().Count > 0)
            Log.Warn("Läuft elevated aus einem Ordner, den auch Programme ohne Adminrechte ändern dürfen. Abhilfe: nach Program Files verschieben, Autostart neu einrichten");

        if (!RestoreJournal.IsEmpty)
        {
            Log.Warn("Letzter Lauf hat Änderungen hinterlassen, räume zuerst auf");
            SystemRestore.RestoreAll("leftover");
        }

        var app = new App(options, instance);
        app.InitializeComponent();
        CrashHandler.Install(app);
        var exitCode = app.Run();

        Log.Info($"Ende (Code {exitCode})");
        return exitCode;
    }
}
