using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Shell;

/// <summary>
/// Zustandslose Helfer rund um die Explorer-Taskleiste. Auch der Watchdog und <c>--restore</c> nutzen sie.
/// <para>
/// Prinzip: Die Taskleiste wird auf „automatisch ausblenden“ gestellt. Dann gibt Explorer selbst den
/// Arbeitsbereich auf allen Monitoren frei. Zusätzlich wird ihr Fenster versteckt, damit sie auch beim
/// Hovern an der Kante nicht hereinfährt. Den Originalzustand hält das Restore-Journal.
/// </para>
/// </summary>
internal static class Taskbar
{
    public const string PrimaryClass = "Shell_TrayWnd";
    public const string SecondaryClass = "Shell_SecondaryTrayWnd";

    public static bool IsVisible
    {
        get
        {
            var primary = Native.FindWindow(PrimaryClass, null);
            return primary != 0 && Native.IsWindowVisible(primary);
        }
    }

    public static IEnumerable<nint> Windows()
    {
        var primary = Native.FindWindow(PrimaryClass, null);
        if (primary != 0) yield return primary;

        nint secondary = 0;
        while ((secondary = Native.FindWindowEx(0, secondary, SecondaryClass, null)) != 0)
            yield return secondary;
    }

    public static uint GetState()
    {
        var data = NewData();
        return (uint)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref data);
    }

    public static void SetState(uint state)
    {
        var data = NewData();
        data.lParam = (nint)state;
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref data);
    }

    public static void HideWindows()
    {
        foreach (var hwnd in Windows()) Native.ShowWindowAsync(hwnd, Native.SW_HIDE);
    }

    /// <summary>Restore-Aktion. <paramref name="originalState"/> stammt aus dem Journal, leer heißt „unbekannt“.</summary>
    public static void Restore(string originalState)
    {
        TaskbarHider.StopEnforcing();

        if (Native.FindWindow(PrimaryClass, null) == 0)
        {
            // Explorer läuft nicht (abgestürzt oder beendet). Neu starten baut die Taskleiste wieder auf.
            Log.Warn("Shell_TrayWnd nicht gefunden, starte explorer.exe");
            Process.Start(new ProcessStartInfo(Paths.Explorer) { UseShellExecute = true });
            return;
        }

        if (uint.TryParse(originalState, NumberStyles.Integer, CultureInfo.InvariantCulture, out var state))
            SetState(state);
        else if (originalState.Length == 0)
            Log.Info("Kein Originalzustand bekannt, lasse Auto-Hide-Einstellung unverändert");

        foreach (var hwnd in Windows()) Native.ShowWindowAsync(hwnd, Native.SW_SHOWNA);
        Log.Info($"Taskleiste wiederhergestellt (Zustand {originalState})");
    }

    private static APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = Native.FindWindow(PrimaryClass, null),
    };
}
