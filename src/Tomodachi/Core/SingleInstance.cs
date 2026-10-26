using System.Security.AccessControl;
using System.Security.Principal;

namespace Tomodachi.Core;

/// <summary>Befehle, die andere Prozesse an die laufende Shell schicken können (<c>tomodachi --quit</c>, <c>--widgets</c>, <c>--windows</c>, <c>--power</c>, <c>--settings</c>, <c>--wallpaper</c>).</summary>
internal static class ShellCommands
{
    public const string Quit = "quit";
    public const string ToggleWidgets = "widgets";
    public const string ToggleWindows = "windows";
    public const string TogglePower = "power";
    public const string ToggleSettings = "settings";
    public const string ToggleWallpaper = "wallpaper";
}

/// <summary>
/// Stellt sicher, dass nur eine Shell läuft, und nimmt Befehle anderer Prozesse entgegen (benannte Events).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\tomodachi.instance";

    /// <summary>
    /// Läuft die Shell elevated, gibt ihre Default-DACL nur Admins und SYSTEM Zugriff, und ein normaler
    /// <c>tomodachi --quit</c> bekäme „Zugriff verweigert“. Deshalb erhält der aktuelle User explizit Zugriff.
    /// Ein Integritätslabel ist nicht nötig: Objekte ohne Label gelten als „Medium“ (es zu setzen bräuchte SeSecurityPrivilege).
    /// </summary>
    private static string EventSddl =>
        $"D:(A;;0x1F0003;;;SY)(A;;0x1F0003;;;BA)(A;;0x1F0003;;;{WindowsIdentity.GetCurrent().User})";

    private readonly Mutex _mutex;
    private readonly List<(EventWaitHandle Event, RegisteredWaitHandle Registration)> _listeners = [];
    private bool _disposed;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Für <c>--restart</c>: Die alte Instanz räumt noch auf (Taskleiste zurück), dann wird der Mutex frei.</summary>
    public static SingleInstance? WaitAndAcquire(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var instance = TryAcquire();
            if (instance is not null || DateTime.UtcNow > deadline) return instance;
            Thread.Sleep(200);
        }
    }

    public static SingleInstance? TryAcquire()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew) return new SingleInstance(mutex);
            mutex.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex gehört einer elevated Instanz: läuft also schon
        }
        return null;
    }

    /// <summary>False, wenn keine Shell läuft.</summary>
    public static bool Signal(string command)
    {
        if (!EventWaitHandle.TryOpenExisting(EventName(command), out var commandEvent)) return false;
        using (commandEvent) commandEvent.Set();
        return true;
    }

    /// <summary><paramref name="onCommand"/> läuft auf einem Threadpool-Thread, bei jedem Signal erneut.</summary>
    public void Listen(string command, Action onCommand)
    {
        var commandEvent = CreateEvent(EventName(command));
        var registration = ThreadPool.RegisterWaitForSingleObject(
            commandEvent, (_, _) => onCommand(), null, Timeout.Infinite, executeOnlyOnce: false);
        _listeners.Add((commandEvent, registration));
    }

    private static string EventName(string command) => $@"Local\tomodachi.{command}";

    private static EventWaitHandle CreateEvent(string name)
    {
        try
        {
            var security = new EventWaitHandleSecurity();
            security.SetSecurityDescriptorSddlForm(EventSddl);
            return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
        }
        catch (Exception ex)
        {
            Log.Warn($"Event {name} mit ACL nicht anlegbar, nehme Standard-Rechte (aus nicht-elevated Prozessen dann nicht erreichbar)", ex);
            return new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
    }

    /// <summary>Darf mehrfach aufgerufen werden, muss aber auf dem Thread laufen, der den Mutex hält.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (commandEvent, registration) in _listeners)
        {
            registration.Unregister(null);
            commandEvent.Dispose();
        }
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
