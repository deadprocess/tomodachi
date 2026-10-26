using System.IO;
using Tomodachi.Core;
using Tomodachi.Input;
using Tomodachi.Interop;

namespace Tomodachi.Wallpaper;

/// <summary>
/// Der frühere eigenständige WallpaperPicker als Teil von tomodachi: Vollbild-Band aus schrägen Wallpaper-Karten,
/// geöffnet per Win+Strg+W oder <c>tomodachi --wallpaper</c>. Overlay und Bedienung sind 1:1 übernommen,
/// nur Tray-Icon und eigener Prozess fallen weg (ohne Taskleiste gibt es keinen Infobereich mehr).
/// </summary>
internal sealed class WallpaperPicker : IDisposable
{
    // Win+Strg+W: Strg+Alt-Kombinationen scheiden aus, weil sie auf deutschen Tastaturen AltGr entsprechen.
    private const uint HotkeyModifiers = Native.MOD_WIN | Native.MOD_CONTROL;
    private const uint HotkeyKey = 0x57; // W

    private readonly WallpaperLibrary _library;
    private readonly GlobalHotkey _hotkey;
    private WallpaperOverlay? _overlay;

    /// <param name="folder">Leer = <c>%USERPROFILE%\Wallpaper</c>, wie bei der Tray-App.</param>
    public WallpaperPicker(string folder)
    {
        folder = string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Wallpaper")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder));

        _library = new WallpaperLibrary(folder, CardLayout.ThumbnailPixelHeight(WallpaperNative.GetMonitorSizes()));
        _library.Refresh(); // Vorschaubilder schon jetzt im Hintergrund laden, damit das Overlay sofort fertig ist

        _hotkey = new GlobalHotkey("wallpaper", HotkeyModifiers, HotkeyKey, Toggle);
        Log.Info($"Wallpaper-Picker bereit ({folder}){(_hotkey.IsRegistered ? ", Win+Strg+W" : ", Win+Strg+W belegt, nur --wallpaper")}");
    }

    /// <summary>Kurz bevor das Overlay erscheint, z. B. um die Notch zuzuklappen.</summary>
    public event Action? Opening;

    public bool IsOpen => _overlay is { IsOpen: true };

    public void Toggle()
    {
        if (_overlay is { IsOpen: true })
            _overlay.HidePicker();
        else
            Show();
    }

    private void Show()
    {
        if (_overlay is { IsOpen: true })
        {
            _overlay.Activate();
            return;
        }

        Opening?.Invoke();
        var overlay = new WallpaperOverlay(_library);
        overlay.Closed += (_, _) =>
        {
            if (_overlay == overlay) _overlay = null;
            MemoryRelief.CollectInBackground();
        };
        _overlay = overlay;
        overlay.ShowPicker();
    }

    public void Dispose()
    {
        _hotkey.Dispose();
        _overlay?.Close();
    }
}
