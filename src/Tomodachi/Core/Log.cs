using System.IO;

namespace Tomodachi.Core;

internal enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Minimaler Datei-Logger. Eine Datei pro Rolle und Tag, damit Hauptprozess und Watchdog sich nicht in die Quere kommen.
/// </summary>
internal static class Log
{
    private const int RetentionDays = 14;
    private static readonly object Gate = new();
    private static string? _file;

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static void Init(string role)
    {
        try
        {
            Directory.CreateDirectory(Paths.LogDir);
            _file = Path.Combine(Paths.LogDir, $"{role}-{DateTime.Now:yyyyMMdd}.log");
            DeleteOldLogs();
        }
        catch
        {
            _file = null; // ohne Log weiterlaufen ist besser als gar nicht starten
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warn, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < MinLevel) return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level,-5}] [{Environment.CurrentManagedThreadId,3}] {message}";
        if (ex is not null) line += Environment.NewLine + ex;

        System.Diagnostics.Debug.WriteLine(line);
        if (_file is null) return;

        lock (Gate)
        {
            try
            {
                using var stream = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
            }
            catch
            {
                // Logging darf nie selbst einen Crash auslösen
            }
        }
    }

    private static void DeleteOldLogs()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(Paths.LogDir, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
                // gesperrte Datei, nächstes Mal
            }
        }
    }
}
