using System.IO;
using System.Text.RegularExpressions;

namespace Tomodachi.Launcher;

/// <param name="Id">
/// Stabile Kennung (Verlauf, Icon-Cache). Im AppsFolder der Parsing-Name: AUMID (Store-Apps) oder Pfad.
/// Bei Desktop-Verknüpfungen der Pfad der .lnk/.url, bei Steam-Spielen <c>steam:{appid}</c>.
/// </param>
/// <param name="Alias">Zweiter Suchbegriff, abgeleitet aus Exe- oder Paketname (z. B. „notepad“ für „Editor“), oder null.</param>
internal sealed partial record AppEntry(string Id, string Name, string? Alias)
{
    /// <summary>Was gestartet wird, falls nicht der AppsFolder: Verknüpfungsdatei oder URL wie <c>steam://rungameid/377160</c>.</summary>
    public string? Target { get; init; }

    /// <summary>Fertiges Icon-Bild (z. B. aus Steams Bibliothek), sonst liefert die Shell das Icon von <see cref="LaunchTarget"/>.</summary>
    public string? IconFile { get; init; }

    public string LaunchTarget => Target ?? $@"shell:AppsFolder\{Id}";

    public static AppEntry Create(string id, string name)
    {
        var alias = DeriveAlias(id);
        if (alias is not null && FuzzyMatcher.Normalize(alias) == FuzzyMatcher.Normalize(name)) alias = null;
        return new AppEntry(id, name, alias);
    }

    /// <summary>
    /// „Microsoft.WindowsNotepad_8wekyb3d8bbwe!App“ → „Windows Notepad“,
    /// „{GUID}\Mozilla Firefox\firefox.exe“ → „firefox“, „MSEdge“ → „MS Edge“.
    /// </summary>
    internal static string? DeriveAlias(string id)
    {
        string raw;
        if (id.Contains('!'))
        {
            var package = id[..id.IndexOf('!')];
            if (package.Contains('_')) package = package[..package.LastIndexOf('_')];
            raw = package[(package.LastIndexOf('.') + 1)..];
        }
        else if (id.Contains('\\') || id.Contains('/'))
        {
            raw = Path.GetFileNameWithoutExtension(id);
        }
        else
        {
            raw = id[(id.LastIndexOf('.') + 1)..];
        }

        raw = CamelCaseBoundary().Replace(raw, " ").Trim();
        return raw.Length >= 2 ? raw : null;
    }

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelCaseBoundary();
}
