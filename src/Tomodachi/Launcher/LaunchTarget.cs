using System.IO;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Launcher;

/// <summary>
/// Was hinter einem Launcher-Eintrag tatsächlich läuft, für den Start mit Adminrechten (<see cref="AppLauncher"/>).
/// Läuft tomodachi elevated, kommt bei „runas“ keine UAC-Abfrage mehr. Ohne Prüfung bekäme damit jedes Programm
/// Adminrechte geschenkt, das eine Verknüpfung auf den Desktop legen oder eine per User installierte Exe
/// (z. B. unter %LOCALAPPDATA%) austauschen kann.
/// </summary>
internal static class LaunchTarget
{
    /// <summary>
    /// Die Datei, die am Ende gestartet würde: die Exe hinter einem AppsFolder-Eintrag oder einer .lnk.
    /// Null, wenn es keine Datei ist (URL, Store-App, Shell-Objekt) oder sie sich nicht auflösen lässt.
    /// </summary>
    public static string? ResolveFile(AppEntry app)
    {
        var target = app.LaunchTarget;
        if (target.Contains("://", StringComparison.Ordinal)) return null;

        string? file;
        if (app.Target is null)
            file = ShellNative.ResolveAppsFolderTarget(target);
        else if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            file = ShellNative.ResolveShortcut(target);
        else
            return null; // .url und alles andere

        if (file is null || file.StartsWith("::", StringComparison.Ordinal) || file.Contains("://", StringComparison.Ordinal)) return null;
        return Path.IsPathFullyQualified(file) && File.Exists(file) ? Path.GetFullPath(file) : null;
    }

    /// <summary>
    /// Darf ohne UAC-Abfrage mit Adminrechten starten: eine Datei, die nur Admins, SYSTEM und TrustedInstaller
    /// ändern können (samt Ordner und Elternordnern), also typischerweise unter Program Files oder Windows.
    /// Lässt sich das nicht feststellen, gilt das Ziel als nicht vertrauenswürdig.
    /// </summary>
    public static bool IsTrustedForElevation(string file)
    {
        try
        {
            return InstallFolder.FindWritable(file, includeSiblings: false).Count == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Rechte von {file} nicht prüfbar", ex);
            return false;
        }
    }
}
