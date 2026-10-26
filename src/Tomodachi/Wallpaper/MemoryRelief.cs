namespace Tomodachi.Wallpaper;

/// <summary>
/// Bilder und geschlossene WPF-Fenster belegen nativen Speicher, den erst ihre Finalizer freigeben. Der GC sieht
/// davon nur kleine verwaltete Objekte und räumt lange nicht auf – die Tray-App bliebe Hunderte MB groß.
/// Nach großen Schüben (Vorschaubilder geladen, Overlay geschlossen) deshalb einmal gezielt aufräumen.
/// </summary>
internal static class MemoryRelief
{
    /// <summary>Im Hintergrund aufräumen; nicht auf dem UI-Thread, weil das Warten auf Finalizer dauern kann.</summary>
    public static void CollectInBackground() => Task.Run(() =>
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    });
}
