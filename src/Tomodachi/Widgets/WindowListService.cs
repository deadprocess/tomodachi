using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Shell;

namespace Tomodachi.Widgets;

internal sealed record WindowInfo(nint Handle, string Title, string ProcessName, bool Minimized);

/// <summary>
/// Paket 5.3: offene Fenster wie in Alt+Tab. Ohne Taskleiste kommt man an minimierte Fenster sonst nicht mehr heran.
/// Muss auf dem UI-Thread laufen (Icons werden zu WPF-Bildern).
/// </summary>
internal sealed unsafe class WindowListService
{
    private static readonly HashSet<string> IgnoredClasses =
        ["Progman", "WorkerW", Taskbar.PrimaryClass, Taskbar.SecondaryClass, "Windows.UI.Core.CoreWindow"];

    private static readonly List<nint> Collected = [];
    private readonly Dictionary<nint, ImageSource?> _icons = [];
    private readonly Dictionary<string, ImageSource?> _exeIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public IReadOnlyList<WindowInfo> Snapshot()
    {
        Collected.Clear();
        delegate* unmanaged<nint, nint, int> callback = &Collect;
        Native.EnumWindows((nint)callback, 0);

        var windows = new List<WindowInfo>();
        foreach (var hwnd in Collected)
        {
            if (!IsAltTabWindow(hwnd)) continue;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == _ownProcessId) continue;
            windows.Add(new WindowInfo(hwnd, Title(hwnd), ProcessName(pid), Native.IsIconic(hwnd)));
        }

        // Icons verschwundener Fenster vergessen
        foreach (var gone in _icons.Keys.Except(windows.Select(w => w.Handle)).ToList()) _icons.Remove(gone);
        return windows;
    }

    public ImageSource? Icon(nint hwnd)
    {
        if (_icons.TryGetValue(hwnd, out var cached)) return cached;
        var icon = WindowIcon(hwnd) ?? ExeIcon(hwnd);
        _icons[hwnd] = icon;
        return icon;
    }

    /// <summary>Der Klick kam bei uns an, deshalb darf tomodachi den Vordergrund weitergeben.</summary>
    public static void Activate(nint hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindowAsync(hwnd, Native.SW_RESTORE);
        if (!Native.SetForegroundWindow(hwnd)) Log.Debug("Fenster ließ sich nicht in den Vordergrund holen");
    }

    public static void Close(nint hwnd) => Native.PostMessage(hwnd, Native.WM_CLOSE, 0, 0);

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint data)
    {
        Collected.Add(hwnd);
        return 1;
    }

    /// <summary>Die Regeln von Alt+Tab: sichtbar, ohne Besitzer (oder ausdrücklich App-Fenster), kein Tool-Fenster, nicht geparkt.</summary>
    private static bool IsAltTabWindow(nint hwnd)
    {
        if (!Native.IsWindowVisible(hwnd) || Native.GetWindowTextLength(hwnd) == 0) return false;

        var exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        var appWindow = (exStyle & Native.WS_EX_APPWINDOW) != 0;
        if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != 0 && !appWindow) return false;

        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        var buffer = stackalloc char[64];
        var length = Native.GetClassName(hwnd, buffer, 64);
        return !IgnoredClasses.Contains(new string(buffer, 0, length));
    }

    private static string Title(nint hwnd)
    {
        var length = Native.GetWindowTextLength(hwnd);
        var buffer = stackalloc char[length + 1];
        var copied = Native.GetWindowText(hwnd, buffer, length + 1);
        return new string(buffer, 0, copied);
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    /// <summary>Icon, das das Fenster selbst meldet. Mit Timeout, falls das Fenster hängt.</summary>
    private static ImageSource? WindowIcon(nint hwnd)
    {
        foreach (var size in new[] { Native.ICON_BIG, Native.ICON_SMALL2 })
        {
            if (Native.SendMessageTimeout(hwnd, Native.WM_GETICON, size, 0, Native.SMTO_ABORTIFHUNG, 50, out var result) != 0 && result != 0)
                return ToImage((nint)result, destroy: false);
        }
        var classIcon = Native.GetClassLongPtr(hwnd, Native.GCLP_HICON);
        if (classIcon == 0) classIcon = Native.GetClassLongPtr(hwnd, Native.GCLP_HICONSM);
        return classIcon != 0 ? ToImage(classIcon, destroy: false) : null;
    }

    /// <summary>Fallback: Icon der Exe, pro Pfad gecacht.</summary>
    private ImageSource? ExeIcon(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var path = ExePath(pid);
        if (path is null) return null;
        if (_exeIcons.TryGetValue(path, out var cached)) return cached;

        SHFILEINFOW info;
        ImageSource? icon = null;
        if (Native.SHGetFileInfo(path, 0, &info, (uint)sizeof(SHFILEINFOW), Native.SHGFI_ICON | Native.SHGFI_LARGEICON) != 0 && info.hIcon != 0)
            icon = ToImage(info.hIcon, destroy: true);
        _exeIcons[path] = icon;
        return icon;
    }

    private static string? ExePath(uint pid)
    {
        var process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return Native.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    private static ImageSource? ToImage(nint hicon, bool destroy)
    {
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(hicon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Debug($"Icon nicht konvertierbar: {ex.Message}");
            return null;
        }
        finally
        {
            if (destroy) Native.DestroyIcon(hicon);
        }
    }
}
