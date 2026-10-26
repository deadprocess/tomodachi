using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Tomodachi.Localization;

/// <summary>
/// Texte der Oberfläche aus <c>Strings.resx</c> (Englisch, neutral) und <c>Strings.de.resx</c> (Deutsch).
/// Die Sprache wird einmal beim Start gesetzt (<see cref="Apply"/>), ein Wechsel wirkt nach einem Neustart.
/// Logs und Code-Kommentare bleiben bewusst einsprachig.
/// </summary>
internal static class Loc
{
    private static readonly ResourceManager Strings = new("Tomodachi.Localization.Strings", typeof(Loc).Assembly);

    /// <summary>Sprache der Oberfläche, auch für Wochentage, Monatsnamen und Datumsformate.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(Resolve("", CultureInfo.CurrentUICulture));

    /// <param name="setting">Config-Wert <c>Language</c>: <c>de</c>, <c>en</c> oder leer = Windows-Anzeigesprache.</param>
    public static void Apply(string setting)
    {
        Culture = CultureInfo.GetCultureInfo(Resolve(setting, CultureInfo.CurrentUICulture));
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    /// <summary>Deutsch nur, wenn gewählt oder Windows deutsch ist, sonst Englisch.</summary>
    internal static string Resolve(string setting, CultureInfo windows) => setting?.ToLowerInvariant() switch
    {
        "de" => "de-DE",
        "en" => "en-US",
        _ => windows.TwoLetterISOLanguageName == "de" ? "de-DE" : "en-US",
    };

    public static string Get(string key) => Get(key, Culture);

    public static string Get(string key, CultureInfo culture) => Strings.GetString(key, culture) ?? key;

    public static string Format(string key, params object?[] args) => Format(Culture, key, args);

    public static string Format(CultureInfo culture, string key, params object?[] args) =>
        string.Format(culture, Get(key, culture), args);
}

/// <summary>XAML: <c>Text="{loc:Tr Settings_Avatar}"</c>. Wird beim Laden aufgelöst, nicht live.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
