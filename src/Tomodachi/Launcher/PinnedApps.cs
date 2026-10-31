using System.IO;
using System.Text.Json;
using Tomodachi.Core;

namespace Tomodachi.Launcher;

/// <summary>
/// Apps the user pinned in the Apps tab: a short, ordered list of app ids (see <see cref="AppEntry.Id"/>) shown as
/// tiles above the results, so everyday apps are one click away without typing. Stored as a JSON array in
/// <c>%APPDATA%\tomodachi\pinned-apps.json</c>. Ids that are not in the index right now (uninstalled, or the index
/// is still loading) are skipped when shown, but stay pinned.
/// </summary>
internal sealed class PinnedApps
{
    public const int Max = 5;

    private readonly string? _file;
    private readonly List<string> _ids;

    private PinnedApps(string? file, List<string> ids)
    {
        _file = file;
        _ids = ids;
    }

    /// <summary>Pinned ids in pin order.</summary>
    public IReadOnlyList<string> Ids => _ids;

    public bool IsFull => _ids.Count >= Max;

    /// <summary>Raised after every pin or unpin, on the calling thread.</summary>
    public event Action? Changed;

    /// <summary>In memory only, for tests.</summary>
    public static PinnedApps InMemory(params string[] ids) => new(null, ids.Distinct(StringComparer.Ordinal).Take(Max).ToList());

    public static PinnedApps Load()
    {
        var file = Paths.PinnedAppsFile;
        try
        {
            if (File.Exists(file))
            {
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? [];
                return new(file, ids.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).Take(Max).ToList());
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Pinned apps unreadable, starting empty", ex);
        }
        return new(file, []);
    }

    public bool IsPinned(string id) => _ids.Contains(id, StringComparer.Ordinal);

    /// <summary>Appends the app. False if it is already pinned or the list is full.</summary>
    public bool Pin(string id)
    {
        if (IsFull || IsPinned(id)) return false;
        _ids.Add(id);
        Save();
        Changed?.Invoke();
        return true;
    }

    public bool Unpin(string id)
    {
        if (_ids.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal)) == 0) return false;
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>The pinned apps that exist in <paramref name="apps"/>, in pin order.</summary>
    public IReadOnlyList<AppEntry> Resolve(IReadOnlyList<AppEntry> apps)
    {
        if (_ids.Count == 0) return [];
        var byId = new Dictionary<string, AppEntry>(StringComparer.Ordinal);
        foreach (var app in apps) byId.TryAdd(app.Id, app);

        var result = new List<AppEntry>(_ids.Count);
        foreach (var id in _ids)
        {
            if (byId.TryGetValue(id, out var app)) result.Add(app);
        }
        return result;
    }

    private void Save()
    {
        if (_file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_ids));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Pinned apps not saveable", ex);
        }
    }
}
