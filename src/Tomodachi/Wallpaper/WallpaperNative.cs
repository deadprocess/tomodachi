using System.Runtime.InteropServices;

namespace Tomodachi.Wallpaper;

/// <summary>Win32-Aufrufe des Wallpaper-Pickers, unverändert aus dem eigenständigen WallpaperPicker übernommen.</summary>
internal static class WallpaperNative
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public record struct RECT(int Left, int Top, int Right, int Bottom)
    {
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT bounds, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>Größe (physische Pixel) aller Monitore. Ersetzt <c>Forms.Screen.AllScreens</c> der Tray-App.</summary>
    public static List<(int Width, int Height)> GetMonitorSizes()
    {
        var sizes = new List<(int, int)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT bounds, IntPtr _) =>
        {
            sizes.Add((bounds.Width, bounds.Height));
            return true;
        }, IntPtr.Zero);
        return sizes;
    }

    /// <summary>Bildschirmrechteck (physische Pixel) des Monitors unter dem Mauszeiger.</summary>
    public static RECT GetCursorMonitorBounds()
    {
        GetCursorPos(out POINT point);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref info);
        return info.Monitor;
    }

    /// <summary>Legt ein Fenster ganz nach oben über den angegebenen Bildschirmbereich.</summary>
    public static void CoverArea(IntPtr hWnd, RECT area)
    {
        // Zweimal: Beim Wechsel auf einen Monitor mit anderer Skalierung löst der erste Aufruf eine
        // DPI-Änderung aus, bei der WPF die Fenstergröße selbst umrechnet. Der zweite setzt sie exakt.
        for (int i = 0; i < 2; i++)
            SetWindowPos(hWnd, HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height, SWP_NOACTIVATE);
    }
}
