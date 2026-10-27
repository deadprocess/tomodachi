using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Tomodachi.Interop;

namespace Tomodachi.Core;

/// <summary>
/// Startet aus dem elevated Prozess heraus ein Programm mit den normalen Rechten des angemeldeten Users
/// (dem Token der Explorer-Instanz), so wie ein Doppelklick auf dem Desktop. explorer.exe als Vermittler
/// (<see cref="Launcher.AppLauncher"/>) kann das auch, reicht aber keine Argumente durch.
/// </summary>
internal static class DesktopUser
{
    public static void Start(string exe, params string[] args)
    {
        var shell = Native.GetShellWindow();
        if (shell == 0) throw new InvalidOperationException("Kein Desktop-Fenster, Explorer läuft nicht");
        Native.GetWindowThreadProcessId(shell, out var pid);

        var process = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, pid);
        if (process == 0) throw new Win32Exception();
        nint token = 0, primary = 0;
        try
        {
            if (!Native.OpenProcessToken(process, Native.TOKEN_DUPLICATE, out token)) throw new Win32Exception();
            const uint access = Native.TOKEN_QUERY | Native.TOKEN_DUPLICATE | Native.TOKEN_ASSIGN_PRIMARY
                                | Native.TOKEN_ADJUST_DEFAULT | Native.TOKEN_ADJUST_SESSIONID;
            if (!Native.DuplicateTokenEx(token, access, 0, Native.SecurityImpersonation, Native.TokenPrimary, out primary))
                throw new Win32Exception();

            var commandLine = string.Join(' ', args.Prepend(exe).Select(Quote));
            var startup = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
            if (!Native.CreateProcessWithToken(primary, 0, exe, commandLine, Native.CREATE_UNICODE_ENVIRONMENT, 0,
                    Path.GetDirectoryName(exe), startup, out var info))
                throw new Win32Exception();
            Native.CloseHandle(info.hThread);
            Native.CloseHandle(info.hProcess);
        }
        finally
        {
            if (primary != 0) Native.CloseHandle(primary);
            if (token != 0) Native.CloseHandle(token);
            Native.CloseHandle(process);
        }
    }

    /// <summary>Windows-Kommandozeilenregeln: Anführungszeichen im Wert werden mit Backslash maskiert.</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => c is ' ' or '"' or '\t')) return arg;
        var escaped = arg.Replace("\\\"", "\\\\\"").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
