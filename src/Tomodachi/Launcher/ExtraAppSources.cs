using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tomodachi.Interop;

namespace Tomodachi.Launcher;

/// <summary>
/// Apps, die nicht in „Alle Apps“ stehen: Desktop-Verknüpfungen (viele Spiele-Launcher wie HoYoPlay oder Kuro legen nur
/// dort eine an) und installierte Steam-Spiele (Steam legt Startmenü-Einträge nur auf Wunsch an).
/// Läuft auf dem STA-Worker des <see cref="AppIndex"/>, weil Verknüpfungen per COM aufgelöst werden.
/// </summary>
internal static partial class ExtraAppSources
{
    /// <summary>Steam-Einträge, die keine Spiele sind.</summary>
    [GeneratedRegex(@"redistributable|steamworks|proton|steam linux runtime", RegexOptions.IgnoreCase)]
    private static partial Regex SteamNoise();

    [GeneratedRegex(@"^URL=(.+)$", RegexOptions.Multiline)]
    private static partial Regex UrlLine();

    [GeneratedRegex(@"""(\w+)""\s+""((?:[^""\\]|\\.)*)""")]
    private static partial Regex VdfPair();

    /// <summary>
    /// .lnk auf eine .exe und .url mit eigenem Protokoll (<c>steam://</c>, <c>com.epicgames.launcher://</c>).
    /// Verknüpfungen auf Ordner, Dokumente oder Webseiten gehören nicht in einen App-Launcher.
    /// </summary>
    public static IEnumerable<AppEntry> Desktop()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var extension = Path.GetExtension(file);
                var isApp = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? ShellNative.ResolveShortcut(file)?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
                    : extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && IsAppUrl(file);
                if (isApp) yield return AppEntry.Create(file, Path.GetFileNameWithoutExtension(file)) with { Target = file };
            }
        }
    }

    private static bool IsAppUrl(string file)
    {
        var match = UrlLine().Match(File.ReadAllText(file));
        if (!match.Success || !Uri.TryCreate(match.Groups[1].Value.Trim(), UriKind.Absolute, out var url)) return false;
        return url.Scheme is not ("http" or "https" or "file");
    }

    /// <summary>Fertig installierte Spiele aus allen Steam-Bibliotheken, gestartet über <c>steam://rungameid/</c>.</summary>
    public static IEnumerable<AppEntry> Steam()
    {
        var steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)?.Replace('/', '\\');
        if (steam is null || !Directory.Exists(steam)) yield break;

        var libraries = new List<string> { steam };
        var libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            libraries.AddRange(VdfPair().Matches(File.ReadAllText(libraryFile))
                .Where(m => m.Groups[1].Value == "path")
                .Select(m => m.Groups[2].Value.Replace(@"\\", @"\")));
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;

            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var values = VdfPair().Matches(File.ReadAllText(manifest))
                    .GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.OrdinalIgnoreCase);
                if (!values.TryGetValue("appid", out var appId) || !values.TryGetValue("name", out var name)) continue;

                // StateFlags 4 = vollständig installiert (nicht nur angefangen oder in der Warteschlange)
                var installed = values.TryGetValue("StateFlags", out var flags) && int.TryParse(flags, out var f) && (f & 4) != 0;
                if (!installed || SteamNoise().IsMatch(name)) continue;

                yield return AppEntry.Create($"steam:{appId}", name) with
                {
                    Target = $"steam://rungameid/{appId}",
                    IconFile = SteamIcon(steam, appId),
                };
            }
        }
    }

    /// <summary>Das kleine Spiel-Icon aus Steams Bibliotheks-Cache: neues Layout <c>{appid}\{sha1}.jpg</c>, altes <c>{appid}_icon.jpg</c>.</summary>
    private static string? SteamIcon(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var folder = Path.Combine(cache, appId);
        if (Directory.Exists(folder))
        {
            var icon = Directory.EnumerateFiles(folder, "*.jpg")
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Length == 40);
            if (icon is not null) return icon;
        }
        var legacy = Path.Combine(cache, $"{appId}_icon.jpg");
        return File.Exists(legacy) ? legacy : null;
    }
}
