using System.IO;

namespace Tomodachi.Core;

internal static class Paths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tomodachi");

    /// <summary>Maschinenlokales (nicht roamendes) für Caches.</summary>
    public static string LocalRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tomodachi");

    public static string LogDir => Path.Combine(Root, "logs");
    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string RestoreJournalFile => Path.Combine(Root, "restore-journal.json");
    public static string LaunchHistoryFile => Path.Combine(Root, "launch-history.json");
    public static string IconCacheDir => Path.Combine(LocalRoot, "icons");
}
