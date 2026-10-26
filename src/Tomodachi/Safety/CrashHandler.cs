using System.Windows;
using Tomodachi.Core;

namespace Tomodachi.Safety;

internal static class CrashHandler
{
    public static void Install(Application app)
    {
        // Prozess stirbt danach trotzdem. Wir sorgen nur dafür, dass Windows benutzbar bleibt.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unbehandelte Exception", e.ExceptionObject as Exception);
            SystemRestore.RestoreAll("crash");
        };

        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unbehandelte Exception im UI-Thread", e.Exception);
            SystemRestore.RestoreAll("ui crash");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("Unbeobachtete Task-Exception", e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemRestore.RestoreAll("process exit");
    }
}
