using System.Runtime.InteropServices;

namespace Tomodachi.Interop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct STARTUPINFOW
{
    public uint cb;
    public nint lpReserved, lpDesktop, lpTitle;
    public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
    public ushort wShowWindow, cbReserved2;
    public nint lpReserved2, hStdInput, hStdOutput, hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_INFORMATION
{
    public nint hProcess, hThread;
    public uint dwProcessId, dwThreadId;
}

internal static partial class Native
{
    public const uint TOKEN_DUPLICATE = 0x0002;
    public const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    public const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    public const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public const int SecurityImpersonation = 2;
    public const int TokenPrimary = 1;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    /// <summary>Das Desktop-Fenster (Progman) gehört der Explorer-Instanz des angemeldeten Users.</summary>
    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateTokenEx(nint token, uint desiredAccess, nint attributes,
        int impersonationLevel, int tokenType, out nint newToken);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateProcessWithTokenW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessWithToken(nint token, uint logonFlags, string? applicationName,
        string? commandLine, uint creationFlags, nint environment, string? currentDirectory,
        in STARTUPINFOW startupInfo, out PROCESS_INFORMATION processInformation);
}
