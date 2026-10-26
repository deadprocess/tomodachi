using Tomodachi.Core;
using Tomodachi.Shell;

namespace Tomodachi.Safety;

internal static class RestoreIds
{
    public const string Taskbar = "taskbar";
}

/// <summary>
/// Nimmt alle Systemänderungen zurück. Idempotent und threadsicher, darf aus jedem Pfad
/// (sauberes Ende, Crash, Watchdog, <c>--restore</c>) beliebig oft aufgerufen werden.
/// </summary>
internal static class SystemRestore
{
    private static readonly object Gate = new();
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Restore-Aktionen bekommen den Originalzustand aus dem Journal (leer bei <c>--restore</c> ohne Eintrag).</summary>
    private static readonly Dictionary<string, Action<string>> Actions = new()
    {
        [RestoreIds.Taskbar] = Taskbar.Restore,
    };

    /// <param name="force">Alle bekannten Aktionen ausführen, nicht nur die im Journal.</param>
    public static void RestoreAll(string reason, bool force = false)
    {
        lock (Gate)
        {
            var pending = new Dictionary<string, string>(RestoreJournal.Entries());
            if (force)
            {
                foreach (var id in Actions.Keys) pending.TryAdd(id, "");
            }
            if (pending.Count == 0) return;

            Log.Info($"Restore ({reason}): {string.Join(", ", pending.Keys)}");
            foreach (var (id, payload) in pending)
            {
                if (Actions.TryGetValue(id, out var action)) Run(id, () => action(payload));
                else Log.Warn($"Unbekannter Journal-Eintrag '{id}' wird verworfen");
                RestoreJournal.Clear(id);
            }
        }
    }

    /// <summary>
    /// Fehler und Hänger (z. B. ein blockierter Explorer) werden geloggt, aber nie weitergereicht.
    /// Eine kaputte Aktion darf die anderen nicht aufhalten.
    /// </summary>
    private static void Run(string id, Action action)
    {
        try
        {
            if (!Task.Run(action).Wait(ActionTimeout))
                Log.Error($"Restore '{id}' hängt nach {ActionTimeout.TotalSeconds:0}s, überspringe");
        }
        catch (AggregateException ex)
        {
            Log.Error($"Restore '{id}' fehlgeschlagen", ex.InnerException);
        }
    }
}
