using System.Runtime.InteropServices;

namespace Tomodachi.Interop;

/// <summary>Mit genau einem Eintrag. Pack 4, weil LUID nativ aus zwei DWORDs besteht (Offset 4, nicht 8).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public long Luid;
    public uint Attributes;
}

internal static partial class Native
{
    public const uint EWX_LOGOFF = 0x00000000;
    public const uint EWX_POWEROFF = 0x00000008;
    public const uint EWX_REBOOT = 0x00000002;
    public const uint EWX_HYBRID_SHUTDOWN = 0x00400000;

    /// <summary>SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED</summary>
    public const uint SHTDN_REASON_PLANNED_OTHER = 0x80000000;

    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ExitWindowsEx(uint flags, uint reason);

    [LibraryImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES newState, uint bufferLength, nint previousState, nint returnLength);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();
}
