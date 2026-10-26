using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Launcher;

/// <summary>
/// Paket 3.1: alle installierten Apps aus <c>shell:AppsFolder</c>, also die Liste „Alle Apps“ des Startmenüs
/// (klassische Programme und Store-Apps in einem Rutsch), dazu Steam-Spiele und Desktop-Verknüpfungen
/// (<see cref="ExtraAppSources"/>). Alle Shell-Aufrufe laufen auf einem eigenen STA-Thread.
/// </summary>
internal sealed partial class AppIndex : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly StaWorker _worker = new("tomodachi.app-index");
    private readonly IconCache _icons;
    private IReadOnlyList<AppEntry> _apps = [];
    private DateTime _lastRefresh = DateTime.MinValue;
    private Task? _running;

    public AppIndex() => _icons = new IconCache(_worker);

    public IReadOnlyList<AppEntry> Apps => Volatile.Read(ref _apps);

    /// <summary>Feuert auf dem Worker-Thread, wenn sich die App-Liste geändert hat.</summary>
    public event Action? Changed;

    /// <summary>Im Hintergrund neu einlesen, höchstens alle 30 s (z. B. bei jedem Öffnen des Launchers).</summary>
    public void RefreshIfStale()
    {
        if (DateTime.UtcNow - _lastRefresh < RefreshInterval || _running is { IsCompleted: false }) return;
        _lastRefresh = DateTime.UtcNow;
        _running = _worker.Run(Refresh);
    }

    public Task<ImageSource?> GetIconAsync(AppEntry app) => _icons.GetAsync(app);

    private bool Refresh()
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var apps = Enumerate();
            var old = Apps;
            Volatile.Write(ref _apps, apps);
            Log.Info($"App-Index: {apps.Count} Apps in {stopwatch.ElapsedMilliseconds} ms");

            if (old.Count != apps.Count || !old.SequenceEqual(apps)) Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("App-Index konnte nicht gelesen werden", ex);
            return false;
        }
    }

    /// <summary>„Alle Apps“, ergänzt um Steam-Spiele und Desktop-Verknüpfungen. Gleichnamige Einträge nur einmal.</summary>
    private static List<AppEntry> Enumerate()
    {
        var apps = EnumerateAppsFolder();
        var names = new HashSet<string>(apps.Select(a => FuzzyMatcher.Normalize(a.Name)));
        int steam = 0, desktop = 0;

        foreach (var app in TryEnumerate("Steam", ExtraAppSources.Steam))
            if (names.Add(FuzzyMatcher.Normalize(app.Name))) { apps.Add(app); steam++; }
        foreach (var app in TryEnumerate("Desktop", ExtraAppSources.Desktop))
            if (!IsNoise(app.Name) && names.Add(FuzzyMatcher.Normalize(app.Name))) { apps.Add(app); desktop++; }

        if (steam + desktop > 0) Log.Info($"App-Index: zusätzlich {steam} Steam-Spiele, {desktop} Desktop-Verknüpfungen");
        apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return apps;
    }

    /// <summary>Eine kaputte Zusatzquelle (z. B. defekte Steam-Datei) darf nie den ganzen Index kosten.</summary>
    private static List<AppEntry> TryEnumerate(string source, Func<IEnumerable<AppEntry>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"App-Index: Quelle {source} nicht lesbar", ex);
            return [];
        }
    }

    private static List<AppEntry> EnumerateAppsFolder()
    {
        ShellNative.SHCreateItemFromParsingName("shell:AppsFolder", 0, ShellNative.IID_IShellItem, out var folder);
        var hr = folder.BindToHandler(0, ShellNative.BHID_EnumItems, ShellNative.IID_IEnumShellItems, out var enumPtr);
        Marshal.ThrowExceptionForHR(hr);

        var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
        Marshal.Release(enumPtr);

        var apps = new List<AppEntry>();
        var batch = new IShellItem[1];
        try
        {
            while (enumerator.Next(1, batch, out var fetched) == 0 && fetched == 1)
            {
                var item = batch[0];
                try
                {
                    var name = ShellNative.TakeString(item.GetDisplayName(SIGDN.NORMALDISPLAY, out var n), n);
                    var id = ShellNative.TakeString(item.GetDisplayName(SIGDN.PARENTRELATIVEPARSING, out var p), p);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || IsNoise(name)) continue;
                    apps.Add(AppEntry.Create(id, name));
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(folder);
        }
        return apps;
    }

    /// <summary>Deinstallationsprogramme stehen auch in „Alle Apps“, will man im Launcher aber nicht.</summary>
    private static bool IsNoise(string name) => NoisePattern().IsMatch(name);

    [GeneratedRegex(@"\b(uninstall|uninstaller|deinstall\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoisePattern();

    public void Dispose() => _worker.Dispose();
}
