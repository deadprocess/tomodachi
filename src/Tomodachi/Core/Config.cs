using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tomodachi.Core;

internal sealed class TomodachiConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    /// <summary>Win-Tipp und Ctrl+Esc öffnen die Shell statt des Startmenüs.</summary>
    public bool InterceptWinKey { get; set; } = true;

    /// <summary>Explorer-Taskleiste entfernen, solange tomodachi läuft.</summary>
    public bool HideTaskbar { get; set; } = true;

    /// <summary>
    /// Monitor für die Notch: leer = Hauptmonitor, sonst Gerätename (<c>\\.\DISPLAY3</c>) oder Teil der
    /// Hardware-Kennung (<c>DELA0BA</c>).
    /// </summary>
    public string NotchMonitor { get; set; } = "";

    /// <summary>Bilder für den Wallpaper-Picker (Win+Strg+W). Leer = <c>%USERPROFILE%\Wallpaper</c>.</summary>
    public string WallpaperFolder { get; set; } = "";

    /// <summary>Notch auch über maximierten Fenstern verstecken, nicht nur bei Vollbild (Spiel, Video).</summary>
    public bool HideOnMaximized { get; set; } = true;

    /// <summary>„Leiste mit Notch“ (true) oder „Nur Notch“: Leiste oben über die ganze Breite des Notch-Monitors, mit den offenen Fenstern als Tabs.</summary>
    public bool ShowTopBar { get; set; } = true;

    /// <summary>Rechtsklick im Fenster-Reiter bietet „Task beenden“ an (Prozess sofort beenden). Wie bei Windows standardmäßig aus.</summary>
    public bool EnableEndTask { get; set; }

    /// <summary>Sprache der Oberfläche: <c>de</c>, <c>en</c> oder leer = wie Windows (Deutsch → de, sonst en).</summary>
    public string Language { get; set; } = "";

    /// <summary>
    /// Nur zur Fehlersuche: Win-Tipp wird erkannt und geschluckt, löst aber nichts aus.
    /// Damit lässt sich prüfen, ob Windows das Startmenü an tomodachi vorbei öffnet.
    /// </summary>
    public bool DiagnoseWinTapNoAction { get; set; }
}

internal static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Lädt die Config. Fehlt sie, wird eine Default-Datei angelegt.
    /// Ist sie kaputt, laufen wir mit Defaults weiter und überschreiben die Datei des Users nicht.
    /// </summary>
    public static TomodachiConfig Load()
    {
        var path = Paths.ConfigFile;
        try
        {
            if (!File.Exists(path))
            {
                var defaults = new TomodachiConfig();
                Directory.CreateDirectory(Paths.Root);
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, Options));
                Log.Info($"Default-Config angelegt: {path}");
                return defaults;
            }

            return JsonSerializer.Deserialize<TomodachiConfig>(File.ReadAllText(path), Options) ?? new TomodachiConfig();
        }
        catch (Exception ex)
        {
            Log.Error($"Config {path} unlesbar, nutze Defaults", ex);
            return new TomodachiConfig();
        }
    }

    /// <summary>Aus dem Einstellungs-Reiter. Erst in eine Temp-Datei, damit ein Absturz nie eine halbe Config hinterlässt.</summary>
    public static void Save(TomodachiConfig config)
    {
        var path = Paths.ConfigFile;
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Config {path} nicht speicherbar", ex);
        }
    }
}
