using System.IO;
using System.Text.Json;
using Tomodachi.Core;

namespace Tomodachi.Launcher;

/// <summary>
/// Frecency: wie oft und wie kürzlich eine App gestartet wurde. Der Wert wächst logarithmisch mit der Anzahl
/// und halbiert sich alle 14 Tage ohne Start. Er ist auf <see cref="MaxBoost"/> gedeckelt.
/// </summary>
internal sealed class LaunchHistory
{
    public const double MaxBoost = 120;
    private const double HalfLifeDays = 14;

    private readonly string? _file;
    private readonly Dictionary<string, Record> _records;

    public sealed record Record(int Count, DateTime LastUsedUtc);

    private LaunchHistory(string? file, Dictionary<string, Record> records)
    {
        _file = file;
        _records = records;
    }

    /// <summary>Nur im Speicher, für Tests.</summary>
    public static LaunchHistory InMemory() => new(null, []);

    public static LaunchHistory Load()
    {
        var file = Paths.LaunchHistoryFile;
        try
        {
            if (File.Exists(file))
                return new(file, JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(file)) ?? []);
        }
        catch (Exception ex)
        {
            Log.Warn("Launch-Historie unlesbar, starte leer", ex);
        }
        return new(file, []);
    }

    public void RecordLaunch(string appId, DateTime nowUtc)
    {
        _records[appId] = _records.TryGetValue(appId, out var old)
            ? new Record(old.Count + 1, nowUtc)
            : new Record(1, nowUtc);
        Save();
    }

    public double Frecency(string appId, DateTime nowUtc)
    {
        if (!_records.TryGetValue(appId, out var record)) return 0;
        var ageDays = Math.Max(0, (nowUtc - record.LastUsedUtc).TotalDays);
        var decay = Math.Pow(0.5, ageDays / HalfLifeDays);
        return Math.Min(MaxBoost, 30 * Math.Log2(1 + record.Count) * decay);
    }

    private void Save()
    {
        if (_file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_records));
        }
        catch (Exception ex)
        {
            Log.Warn("Launch-Historie nicht speicherbar", ex);
        }
    }
}
