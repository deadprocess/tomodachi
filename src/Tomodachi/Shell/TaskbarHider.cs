using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Safety;

namespace Tomodachi.Shell;

/// <summary>
/// Hält die Taskleiste weg, solange die Shell läuft. Muss auf dem UI-Thread erzeugt werden,
/// weil der WinEvent-Hook seine Callbacks über dessen Message-Loop bekommt.
/// <list type="bullet">
/// <item>WinEvent-Hook auf EVENT_OBJECT_SHOW: Explorer blendet die Taskleiste ein (Win+T, Startmenü, Hover …) → sofort wieder weg.</item>
/// <item>Polling jede Sekunde: Explorer-Neustart (neue HWNDs), neue Monitore, verlorenes Auto-Hide.</item>
/// </list>
/// </summary>
internal sealed unsafe class TaskbarHider : IDisposable
{
    private static TaskbarHider? s_instance;
    private static volatile bool s_enforcing;

    private readonly DispatcherTimer _poll;
    private readonly nint _winEventHook;
    private HashSet<nint> _windows = [];

    public TaskbarHider()
    {
        var original = Taskbar.GetState();
        RestoreJournal.Mark(RestoreIds.Taskbar, original.ToString(CultureInfo.InvariantCulture));

        Taskbar.SetState(original | Native.ABS_AUTOHIDE);
        s_instance = this;
        s_enforcing = true;
        Enforce();

        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc = &OnWinEvent;
        _winEventHook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, 0, (nint)proc,
            0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        if (_winEventHook == 0) Log.Warn("WinEvent-Hook fehlgeschlagen, nur Polling aktiv");

        _poll = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Enforce(),
            Dispatcher.CurrentDispatcher);
        _poll.Start();

        Log.Info($"Taskleiste entfernt (Originalzustand {original}, {_windows.Count} Fenster)");
    }

    /// <summary>Ab sofort nichts mehr verstecken. Wird von der Restore-Aktion aufgerufen, egal aus welchem Thread.</summary>
    public static void StopEnforcing() => s_enforcing = false;

    private void Enforce()
    {
        if (!s_enforcing) return;

        var windows = Taskbar.Windows().ToHashSet();
        if (!windows.SetEquals(_windows))
        {
            // Explorer neu gestartet oder Monitor dazugekommen. SHAppBarMessage blockiert bis Explorer antwortet,
            // deshalb nur hier prüfen und nicht jede Sekunde.
            _windows = windows;
            var state = Taskbar.GetState();
            if ((state & Native.ABS_AUTOHIDE) == 0)
            {
                Log.Info("Auto-Hide fehlt (Explorer-Neustart?), setze erneut");
                Taskbar.SetState(state | Native.ABS_AUTOHIDE);
            }
        }

        foreach (var hwnd in _windows)
        {
            if (Native.IsWindowVisible(hwnd)) Native.ShowWindowAsync(hwnd, Native.SW_HIDE);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Läuft für jedes eingeblendete Fenster im System, deshalb nur ein HashSet-Lookup
        if (!s_enforcing || idObject != Native.OBJID_WINDOW || s_instance is not { } self) return;
        if (self._windows.Contains(hwnd)) Native.ShowWindowAsync(hwnd, Native.SW_HIDE);
    }

    public void Dispose()
    {
        s_enforcing = false;
        _poll.Stop();
        if (_winEventHook != 0) Native.UnhookWinEvent(_winEventHook);
        s_instance = null;
    }
}
