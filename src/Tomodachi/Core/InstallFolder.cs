using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Tomodachi.Core;

/// <summary>
/// Die Autostart-Aufgabe startet die Exe ohne UAC-Abfrage mit höchsten Rechten. Darf ein Programm ohne Adminrechte
/// die Exe, eine DLL daneben oder den Ordner selbst austauschen, bekommt es darüber Adminrechte geschenkt.
/// Sicher ist ein Ordner, den nur Admins, SYSTEM und TrustedInstaller ändern dürfen (z. B. unter Program Files).
/// </summary>
internal static class InstallFolder
{
    private static readonly SecurityIdentifier[] TrustedSids =
    [
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
        new(WellKnownSidType.CreatorOwnerSid, null), // nur Vorlage für Vererbung, steht für den Besitzer
    ];

    // GENERIC_ALL und GENERIC_WRITE fehlen im Enum, stehen aber manchmal so im ACL
    private const FileSystemRights GenericAll = (FileSystemRights)0x10000000;
    private const FileSystemRights GenericWrite = (FileSystemRights)0x40000000;
    private const FileSystemRights TakeOver = FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | GenericAll;

    /// <summary>Exe oder DLL überschreiben bzw. löschen.</summary>
    private const FileSystemRights FileWrite =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete | GenericWrite | TakeOver;

    /// <summary>Im Ordner der Exe: Dateien (z. B. eine DLL) oder Unterordner anlegen, Inhalt löschen, Ordner umbenennen.</summary>
    private const FileSystemRights FolderWrite =
        FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.Delete | GenericWrite | TakeOver;

    /// <summary>Darüberliegende Ordner: umbenennen bzw. Unterordner löschen genügt, um den ganzen Baum auszutauschen.</summary>
    private const FileSystemRights AncestorWrite =
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete | TakeOver;

    /// <summary>
    /// Pfade, die ein Nicht-Admin ändern darf, jeweils mit Konto und Rechten (für die Meldung bewusst ohne deutsche Wörter).
    /// Leer = sicher.
    /// Deny-Einträge werden ignoriert: lieber einmal zu oft warnen als zu selten.
    /// </summary>
    /// <param name="includeSiblings">
    /// Auch die anderen Exe- und DLL-Dateien im Ordner prüfen (für die eigene Exe: eine ausgetauschte DLL daneben würde mitgeladen).
    /// Für ein fremdes Startziel reicht die Datei selbst samt Ordnern.
    /// </param>
    public static IReadOnlyList<string> FindWritable(string exe, bool includeSiblings = true)
    {
        var problems = new List<string>();
        var target = new FileInfo(Path.GetFullPath(exe));
        var folder = target.Directory!;

        var files = includeSiblings
            ? folder.EnumerateFiles().Where(f => f.Extension.ToLowerInvariant() is ".exe" or ".dll")
            : [target];
        foreach (var file in files.Where(f => f.Exists))
            Check(file.FullName, file.GetAccessControl(), FileWrite, problems);

        Check(folder.FullName, folder.GetAccessControl(), FolderWrite, problems);
        for (var parent = folder.Parent; parent is not null; parent = parent.Parent)
            Check(parent.FullName, parent.GetAccessControl(), AncestorWrite, problems);

        return problems;
    }

    private static void Check(string path, FileSystemSecurity security, FileSystemRights dangerous, List<string> problems)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !IsTrusted(owner))
            problems.Add($"{path} ({Name(owner)}, owner)"); // der Besitzer darf die Rechte jederzeit selbst ändern

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue; // gilt nur für Kinder, die prüfen wir selbst
            if ((rule.FileSystemRights & dangerous) == 0 || rule.IdentityReference is not SecurityIdentifier sid || IsTrusted(sid)) continue;
            problems.Add($"{path} ({Name(sid)}: {rule.FileSystemRights})");
        }
    }

    private static bool IsTrusted(SecurityIdentifier sid) => TrustedSids.Contains(sid);

    /// <summary>True if <paramref name="folder"/> is Program Files (64- or 32-bit) or lies below it.</summary>
    public static bool IsUnderProgramFiles(string folder)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(special);
            if (string.IsNullOrEmpty(root)) continue;
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Name(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }
}
