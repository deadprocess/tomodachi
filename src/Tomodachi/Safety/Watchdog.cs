using System.Diagnostics;
using Tomodachi.Core;

namespace Tomodachi.Safety;

/// <summary>
/// Zweiter Prozess (<c>tomodachi --watchdog &lt;pid&gt;</c>). Er wartet, bis die Shell endet.
/// Steht dann noch etwas im Journal, ist die Shell nicht sauber beendet worden
/// (Task-Manager, Absturz in nativem Code …), und der Watchdog räumt auf.
/// Der Tastatur-Hook braucht kein Aufräumen, Windows entfernt ihn mit dem Prozess.
/// </summary>
internal static class Watchdog
{
    public static int Run(int pid)
    {
        Log.Info($"Watchdog überwacht PID {pid}");
        try
        {
            using var shell = Process.GetProcessById(pid);
            shell.WaitForExit();
        }
        catch (ArgumentException)
        {
            Log.Warn($"PID {pid} läuft schon nicht mehr");
        }

        if (RestoreJournal.IsEmpty)
        {
            Log.Info("Shell sauber beendet, nichts zu tun");
            return 0;
        }

        Log.Warn("Shell unsauber beendet, stelle System wieder her");
        SystemRestore.RestoreAll("watchdog");
        return 0;
    }
}

/// <summary>Startet den Watchdog aus der Shell heraus und hält ihn am Leben.</summary>
internal sealed class WatchdogSupervisor : IDisposable
{
    private const int MaxRespawns = 5;
    private Process? _process;
    private int _respawns;
    private bool _disposed;

    public void Start()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unbekannt");
        _process = Process.Start(new ProcessStartInfo(exe, ["--watchdog", Environment.ProcessId.ToString()])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (_process is null)
        {
            Log.Error("Watchdog konnte nicht gestartet werden");
            return;
        }

        _process.EnableRaisingEvents = true;
        _process.Exited += OnExited;
        Log.Info($"Watchdog gestartet (PID {_process.Id})");
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        if (++_respawns > MaxRespawns)
        {
            Log.Error("Watchdog stirbt immer wieder, gebe auf");
            return;
        }
        Log.Warn($"Watchdog unerwartet beendet, starte neu ({_respawns}/{MaxRespawns})");
        Start();
    }

    /// <summary>Den Watchdog bewusst weiterlaufen lassen. Er beendet sich selbst, sobald die Shell weg ist.</summary>
    public void Dispose()
    {
        _disposed = true;
        _process?.Dispose();
    }
}
