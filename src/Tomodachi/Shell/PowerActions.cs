using System.Runtime.InteropServices;
using Microsoft.Win32;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Shell;

public enum PowerAction
{
    Lock,
    Sleep,
    SignOut,
    Restart,
    Shutdown,
}

/// <summary>
/// Das Powermenü der Notch. Herunterfahren, Neustart und Abmelden laufen über ExitWindowsEx, also wie im Startmenü:
/// Apps dürfen noch speichern bzw. nachfragen, nichts wird erzwungen. Die Taskleiste stellt App.OnSessionEnding wieder her.
/// </summary>
internal static class PowerActions
{
    /// <summary>Diese Aktionen beenden die Sitzung und brauchen deshalb einen zweiten Klick.</summary>
    public static bool NeedsConfirmation(PowerAction action) =>
        action is PowerAction.SignOut or PowerAction.Restart or PowerAction.Shutdown;

    public static void Run(PowerAction action)
    {
        Log.Info($"Powermenü: {action}");
        var ok = action switch
        {
            PowerAction.Lock => Native.LockWorkStation(),
            PowerAction.Sleep => EnableShutdownPrivilege() && Native.SetSuspendState(false, false, false),
            PowerAction.SignOut => Native.ExitWindowsEx(Native.EWX_LOGOFF, Native.SHTDN_REASON_PLANNED_OTHER),
            PowerAction.Restart => EnableShutdownPrivilege()
                && Native.ExitWindowsEx(Native.EWX_REBOOT, Native.SHTDN_REASON_PLANNED_OTHER),
            PowerAction.Shutdown => EnableShutdownPrivilege()
                && Native.ExitWindowsEx(Native.EWX_POWEROFF | (FastStartupEnabled() ? Native.EWX_HYBRID_SHUTDOWN : 0),
                    Native.SHTDN_REASON_PLANNED_OTHER),
            _ => false,
        };
        if (!ok) Log.Warn($"Powermenü: {action} fehlgeschlagen (Win32 {Marshal.GetLastPInvokeError()})");
    }

    /// <summary>Wie das Startmenü: Mit Schnellstart wird hybrid heruntergefahren.</summary>
    private static bool FastStartupEnabled() =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0) is 1;

    /// <summary>SeShutdownPrivilege ist im Token vorhanden, aber standardmäßig aus.</summary>
    private static bool EnableShutdownPrivilege()
    {
        if (!Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_ADJUST_PRIVILEGES | Native.TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!Native.LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid)) return false;
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = Native.SE_PRIVILEGE_ENABLED };
            // Gibt auch bei ERROR_NOT_ALL_ASSIGNED true zurück, dann scheitert eben der eigentliche Aufruf
            return Native.AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0);
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }
}

/// <summary>Zweiter Klick auf dieselbe Aktion innerhalb des Fensters bestätigt sie. Testbar ohne UI.</summary>
internal sealed class ConfirmGate(TimeSpan window)
{
    private PowerAction? _armed;
    private DateTime _armedAt;

    public PowerAction? Armed => _armed;

    /// <returns><c>true</c>, wenn die Aktion jetzt ausgeführt werden soll.</returns>
    public bool Press(PowerAction action, DateTime now)
    {
        if (!PowerActions.NeedsConfirmation(action))
        {
            _armed = null;
            return true;
        }
        if (_armed == action && now - _armedAt <= window)
        {
            _armed = null;
            return true;
        }
        _armed = action;
        _armedAt = now;
        return false;
    }

    /// <summary>Zeit abgelaufen oder Menü zu: nichts mehr scharf.</summary>
    public void Disarm() => _armed = null;
}
