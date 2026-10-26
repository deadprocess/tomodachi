using System.Windows.Threading;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Shell;

/// <summary>
/// Meldet, ob auf dem Notch-Monitor gerade ein Fenster direkt unter der Notch den ganzen Monitor ausfüllt:
/// Vollbild (Spiel, Video) <b>oder maximiert</b> (abschaltbar, <see cref="HideOnMaximized"/>). Die Notch säße sonst über
/// Tab- oder Titelleiste (26.10.2026, Wunsch nach YouTube in maximiertem Firefox). Erreichbar bleibt sie per Win-Tipp.
/// <para>
/// Bewusst die Z-Reihenfolge statt des Vordergrundfensters: Ein frisch gestartetes Spiel liegt oft schon oben,
/// ohne den Fokus zu haben (26.10.2026, Wuthering Waves über den Launcher gestartet).
/// </para>
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    private static readonly HashSet<string> ShellClasses =
        ["Progman", "WorkerW", Taskbar.PrimaryClass, Taskbar.SecondaryClass];

    private readonly DispatcherTimer _timer;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public FullscreenWatcher()
    {
        // 250 ms: Die Snap-Leiste erscheint während man zieht, die Notch soll ihr rasch Platz machen
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Check(),
            Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    public bool IsFullscreen { get; private set; }

    /// <summary>
    /// Aus: Nur echtes Vollbild zählt, erkannt an der fehlenden Titelleiste (WS_CAPTION). Nicht über „maximiert“, denn
    /// Firefox bleibt im Video-Vollbild maximiert. Maximierte Fenster behalten ihre Titelleiste und damit die Notch.
    /// </summary>
    public bool HideOnMaximized { get; set; } = true;

    public event Action<bool>? Changed;

    private void Check()
    {
        var window = DetectFullscreen();
        var now = window is not null;
        if (now == IsFullscreen) return;
        IsFullscreen = now;
        // Nur der Fensterklasse wegen (z. B. „UnrealWindow“, „MozillaWindowClass“), damit Fehlerkennungen nachvollziehbar sind
        Log.Info(now ? $"Notch versteckt ({window})" : "Notch wieder da");
        Changed?.Invoke(now);
    }

    /// <returns>Grund fürs Verstecken (Fensterklasse, „maximiert“, „Snap-Leiste“), oder null.</returns>
    private string? DetectFullscreen()
    {
        var notchMonitor = Monitors.NotchHandle();
        var monitor = Monitors.Info(notchMonitor).rcMonitor;
        if (SnapBarVisible(monitor)) return "Snap-Leiste";

        var hwnd = WindowUnderNotch(monitor.Left + monitor.Width / 2, monitor.Top);
        if (hwnd == 0) return null;

        var className = ClassName(hwnd);
        if (ShellClasses.Contains(className)) return null;
        if (!HideOnMaximized && (Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION) return null;

        Native.GetWindowRect(hwnd, out var rect);
        var coversMonitor = rect.Left <= monitor.Left && rect.Top <= monitor.Top
            && rect.Right >= monitor.Right && rect.Bottom >= monitor.Bottom;
        if (!coversMonitor || OtherWindowHasFocus(hwnd, notchMonitor)) return null;
        return Native.IsZoomed(hwnd) ? $"{className}, maximiert" : className;
    }

    /// <summary>
    /// Die Snap-Leiste von Windows 11 (Fenster an den oberen Rand ziehen): ein Explorer-Fenster der Klasse
    /// <c>XamlExplorerHostIslandWindow</c> ohne Titel, oben am Monitor, gut 2560 × 360 px, das nur während des Ziehens
    /// existiert (gemessen 27.10.2026). Dieselbe Klasse nutzt Windows für Alt+Tab und Aufgabenansicht, die sind aber
    /// bildschirmfüllend und haben einen Titel. Die Notch säße sonst mitten auf den Layout-Kacheln.
    /// </summary>
    private static bool SnapBarVisible(RECT monitor)
    {
        for (var hwnd = Native.FindWindowEx(0, 0, "XamlExplorerHostIslandWindow", null); hwnd != 0;
             hwnd = Native.FindWindowEx(0, hwnd, "XamlExplorerHostIslandWindow", null))
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetWindowTextLength(hwnd) > 0) continue;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;

            Native.GetWindowRect(hwnd, out var rect);
            var atTop = rect.Top == monitor.Top && rect.Left < monitor.Right && rect.Right > monitor.Left;
            if (atTop && rect.Height < monitor.Height / 2) return true;
        }
        return false;
    }

    /// <summary>
    /// Nach Alt+Tab bleibt das Spiel oft bildschirmfüllend unter der Notch liegen, weil das neue Fenster kleiner ist.
    /// Arbeitet man dann in einem anderen Fenster <b>auf dem Notch-Monitor</b>, soll die Notch zurückkommen.
    /// Fokus auf einem anderen Monitor zählt nicht, die Notch säße sonst wieder über der Tab-Leiste.
    /// Ebenso wenig: das Spiel selbst und sein Prozess, tomodachi, der Desktop sowie unsichtbare oder minimierte Fenster
    /// (so direkt nach dem Spielstart, wenn der Launcher noch den Fokus hat).
    /// </summary>
    private bool OtherWindowHasFocus(nint fullscreen, nint notchMonitor)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0 || foreground == fullscreen) return false;
        if (!Native.IsWindowVisible(foreground) || Native.IsIconic(foreground)) return false;
        if (Native.MonitorFromWindow(foreground, Native.MONITOR_DEFAULTTONEAREST) != notchMonitor) return false;

        Native.GetWindowThreadProcessId(foreground, out var foregroundPid);
        Native.GetWindowThreadProcessId(fullscreen, out var fullscreenPid);
        if (foregroundPid == fullscreenPid || foregroundPid == _ownProcessId) return false;
        if (ShellClasses.Contains(ClassName(foreground))) return false;

        return !(Native.DwmGetWindowAttribute(foreground, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0);
    }

    /// <summary>
    /// Oberstes echtes Fenster an diesem Punkt, von oben durch die Z-Reihenfolge. Übersprungen werden eigene Fenster und
    /// Overlays: Tool- und klickdurchlässige Fenster. Der Dell Display Manager etwa legt zwei unsichtbare, bildschirmfüllende
    /// Tool-Fenster über alles.
    /// </summary>
    private nint WindowUnderNotch(int x, int y)
    {
        const nint overlayStyles = Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;

        for (var hwnd = Native.GetTopWindow(0); hwnd != 0; hwnd = Native.GetWindow(hwnd, Native.GW_HWNDNEXT))
        {
            // Fenster mit Besitzer sind Popups, Menüs, Hinweise („youtube.com ist jetzt im Vollbildmodus“), kein Vollbild
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != 0) continue;
            if ((Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE) & overlayStyles) != 0) continue;

            Native.GetWindowRect(hwnd, out var rect);
            if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom) continue;

            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == _ownProcessId) continue;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;

            return hwnd;
        }
        return 0;
    }

    private static unsafe string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[64];
        var length = Native.GetClassName(hwnd, buffer, 64);
        return new string(buffer, 0, length);
    }

    public void Dispose() => _timer.Stop();
}
