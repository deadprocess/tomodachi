using System.Runtime.InteropServices;
using Tomodachi.Core;

namespace Tomodachi.Interop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct MONITORINFOEX
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    public fixed char szDevice[32];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DISPLAY_DEVICE
{
    public int cb;
    public fixed char DeviceName[32];
    public fixed char DeviceString[128];
    public int StateFlags;
    public fixed char DeviceID[128];
    public fixed char DeviceKey[128];
}

internal static unsafe partial class Monitors
{
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;
    private static readonly object Gate = new();
    private static readonly List<nint> Collected = [];
    private static nint s_notchMonitor;

    /// <summary>
    /// Monitor für die Notch: leer = Hauptmonitor, sonst Gerätename (<c>\\.\DISPLAY3</c>) oder ein Teil der
    /// Hardware-Kennung (<c>DELA0BA</c> = Dell U2415). Die Kennung bleibt stabil, wenn sich die DISPLAY-Nummern ändern.
    /// </summary>
    public static string NotchSpec { get; set; } = "";

    public static nint PrimaryHandle() => Native.MonitorFromPoint(new POINT(0, 0), Native.MONITOR_DEFAULTTOPRIMARY);

    public static MONITORINFO Info(nint monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        return info;
    }

    /// <summary>Gecacht. Nach Änderungen der Anzeige-Einstellungen <see cref="Invalidate"/> aufrufen.</summary>
    public static nint NotchHandle()
    {
        lock (Gate)
        {
            if (s_notchMonitor == 0) s_notchMonitor = Resolve(NotchSpec);
            return s_notchMonitor;
        }
    }

    public static void Invalidate()
    {
        lock (Gate) s_notchMonitor = 0;
    }

    private static nint Resolve(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return PrimaryHandle();

        foreach (var monitor in All())
        {
            var (device, interfaceId) = Describe(monitor);
            if (device.Equals(spec, StringComparison.OrdinalIgnoreCase)
                || interfaceId.Contains(spec, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"Notch-Monitor: {device} ({spec})");
                return monitor;
            }
        }

        Log.Warn($"Notch-Monitor '{spec}' nicht gefunden, nehme den Hauptmonitor");
        return PrimaryHandle();
    }

    /// <summary>Für die Monitorwahl in den Einstellungen, von links nach rechts.</summary>
    /// <returns>Spec = Hardware-Kennung (<c>DELA0BA</c>), stabil auch wenn sich die DISPLAY-Nummern ändern.</returns>
    public static List<(string Spec, int Width, int Height, bool IsPrimary)> List()
    {
        var primary = PrimaryHandle();
        return All()
            .Select(monitor => (monitor, rect: Info(monitor).rcMonitor, id: HardwareId(Describe(monitor).InterfaceId)))
            .OrderBy(m => m.rect.Left)
            .Select(m => (m.id, m.rect.Width, m.rect.Height, m.monitor == primary))
            .ToList();
    }

    /// <summary><c>\\?\DISPLAY#DELA0BA#5&amp;…</c> → <c>DELA0BA</c> (Hersteller- und Modellkürzel).</summary>
    private static string HardwareId(string interfaceId)
    {
        var parts = interfaceId.Split('#');
        return parts.Length > 1 ? parts[1] : interfaceId;
    }

    private static List<nint> All()
    {
        lock (Gate)
        {
            Collected.Clear();
            delegate* unmanaged<nint, nint, RECT*, nint, int> callback = &Collect;
            EnumDisplayMonitors(0, null, (nint)callback, 0);
            return [.. Collected];
        }
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* rect, nint data)
    {
        Collected.Add(monitor);
        return 1;
    }

    /// <summary>Gerätename (<c>\\.\DISPLAY3</c>) und Geräte-Interface (<c>\\?\DISPLAY#DELA0BA#…</c>).</summary>
    private static (string Device, string InterfaceId) Describe(nint monitor)
    {
        var info = new MONITORINFOEX { cbSize = (uint)sizeof(MONITORINFOEX) };
        if (!GetMonitorInfoEx(monitor, &info)) return ("", "");
        var device = new string(info.szDevice);

        var display = new DISPLAY_DEVICE { cb = sizeof(DISPLAY_DEVICE) };
        var interfaceId = EnumDisplayDevices(device, 0, &display, EDD_GET_DEVICE_INTERFACE_NAME)
            ? new string(display.DeviceID)
            : "";
        return (device, interfaceId);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint hdc, RECT* clip, nint callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoEx(nint monitor, MONITORINFOEX* info);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayDevices(string device, uint index, DISPLAY_DEVICE* display, uint flags);
}
