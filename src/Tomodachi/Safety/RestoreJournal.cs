using System.IO;
using System.Text.Json;
using Tomodachi.Core;

namespace Tomodachi.Safety;

/// <summary>
/// Persistente Liste aller Systemänderungen, die gerade aktiv sind, jeweils mit dem Originalzustand als Payload.
/// Wer etwas am System ändert, trägt es <b>vorher</b> ein. So kann der Watchdog oder der nächste Start
/// aufräumen, egal wie der Prozess gestorben ist.
/// </summary>
internal static class RestoreJournal
{
    private static readonly object Gate = new();

    public static IReadOnlyDictionary<string, string> Entries()
    {
        lock (Gate) return Read();
    }

    public static bool IsEmpty => Entries().Count == 0;

    /// <summary>Ein schon vorhandener Eintrag wird nicht überschrieben, sein Originalzustand bleibt maßgeblich.</summary>
    public static void Mark(string id, string payload = "")
    {
        lock (Gate)
        {
            var entries = Read();
            if (!entries.TryAdd(id, payload)) return;
            Save(entries);
        }
    }

    public static void Clear(string id)
    {
        lock (Gate)
        {
            var entries = Read();
            if (entries.Remove(id)) Save(entries);
        }
    }

    private static Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(Paths.RestoreJournalFile)) return [];
            var json = File.ReadAllText(Paths.RestoreJournalFile);

            // Format aus Phase 0 war eine reine ID-Liste
            if (json.TrimStart().StartsWith('['))
                return (JsonSerializer.Deserialize<List<string>>(json) ?? []).ToDictionary(id => id, _ => "");

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("Restore-Journal unlesbar, behandle als leer", ex);
            return [];
        }
    }

    private static void Save(Dictionary<string, string> entries)
    {
        Directory.CreateDirectory(Paths.Root);
        var temp = Paths.RestoreJournalFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries));
        File.Move(temp, Paths.RestoreJournalFile, overwrite: true);
    }
}
