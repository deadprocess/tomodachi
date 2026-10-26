using System.Diagnostics;
using Tomodachi.Interop;

namespace Tomodachi.Shell;

internal static class ShellWindows
{
    /// <summary>Prozesse hinter Startmenü, Suche, Info-Center und Touch-Tastatur von Windows 11.</summary>
    private static readonly HashSet<string> FlyoutProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost",
    };

    /// <summary>
    /// Fenster, das tomodachi nie wieder in den Vordergrund holen darf: unsichtbar geparkte (cloaked) Fenster und die
    /// Shell-Flyouts. SetForegroundWindow auf die geparkte Suche öffnet sie sichtbar (beobachtet am 26.10.2026).
    /// </summary>
    public static bool IsShellFlyoutOrCloaked(nint hwnd)
    {
        if (hwnd == 0) return true;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return true;

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return FlyoutProcesses.Contains(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return true; // Prozess schon weg
        }
    }
}
