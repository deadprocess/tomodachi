using System.Runtime.InteropServices;
using System.Windows.Interop;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Safety;

/// <summary>
/// Ctrl+Alt+Shift+Q beendet die Shell sauber. Läuft über RegisterHotKey statt über den Tastatur-Hook,
/// damit der Ausstieg auch dann funktioniert, wenn der Hook spinnt. Bewusst nicht konfigurierbar.
/// </summary>
internal sealed class EmergencyHotkey : IDisposable
{
    private const int HotkeyId = 0x7A51;
    private const uint VK_Q = 0x51;

    private readonly HwndSource _window;
    private readonly Action _onTriggered;
    private readonly bool _registered;

    public EmergencyHotkey(Action onTriggered)
    {
        _onTriggered = onTriggered;
        _window = new HwndSource(new HwndSourceParameters("tomodachi.hotkeys")
        {
            ParentWindow = Native.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);

        const uint modifiers = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
        _registered = Native.RegisterHotKey(_window.Handle, HotkeyId, modifiers, VK_Q);
        if (_registered)
            Log.Info("Notausstieg aktiv: Ctrl+Alt+Shift+Q");
        else
            Log.Error($"Notausstieg-Hotkey nicht registrierbar (Win32 {Marshal.GetLastPInvokeError()}), Ausweg: tomodachi --quit");
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Log.Info("Notausstieg ausgelöst");
            _onTriggered();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_registered) Native.UnregisterHotKey(_window.Handle, HotkeyId);
        _window.Dispose();
    }
}
