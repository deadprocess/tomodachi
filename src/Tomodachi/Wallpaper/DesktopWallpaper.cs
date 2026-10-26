using System.Runtime.InteropServices;

namespace Tomodachi.Wallpaper;

/// <summary>Setzt und liest das Hintergrundbild über die Windows-Schnittstelle IDesktopWallpaper, auch pro Monitor.</summary>
internal static class DesktopWallpaper
{
    /// <summary>Setzt das Bild auf dem angegebenen Monitor; <c>null</c> bedeutet alle Monitore.</summary>
    public static void Apply(string path, string? monitorId)
    {
        var wallpaper = Create();
        wallpaper.SetPosition(DesktopWallpaperPosition.Fill);
        wallpaper.SetWallpaper(monitorId, path);
    }

    public static string? GetCurrent(string? monitorId)
    {
        try
        {
            return Create().GetWallpaper(monitorId);
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Windows-interne Kennung des Monitors mit genau diesen Bildschirmkoordinaten.</summary>
    public static string? FindMonitorId(WallpaperNative.RECT bounds)
    {
        var wallpaper = Create();
        uint count = wallpaper.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string id = wallpaper.GetMonitorDevicePathAt(i);
            try
            {
                if (wallpaper.GetMonitorRECT(id) == bounds)
                    return id;
            }
            catch (COMException)
            {
                // Monitor ist Windows bekannt, aber gerade nicht angeschlossen
            }
        }
        return null;
    }

    private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    private enum DesktopWallpaperPosition
    {
        Center,
        Tile,
        Stretch,
        Fit,
        Fill,
        Span,
    }

    // Reihenfolge der Methoden muss exakt der Vtable in ShObjIdl_core.h entsprechen.
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        uint GetMonitorDevicePathCount();

        WallpaperNative.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);

        void SetBackgroundColor(uint color);

        uint GetBackgroundColor();

        void SetPosition(DesktopWallpaperPosition position);

        DesktopWallpaperPosition GetPosition();
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass
    {
    }
}
