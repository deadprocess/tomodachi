using System.Runtime.InteropServices;
using System.Windows.Interop;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Input;

/// <summary>
/// Systemweiter Hotkey per RegisterHotKey auf einem Message-Only-Fenster des aufrufenden (UI-)Threads.
/// Wichtig: Wer WM_HOTKEY bekommt, darf danach SetForegroundWindow aufrufen. Ein Low-Level-Hook darf das nicht.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private static int s_nextId = 0x7B00;

    private readonly HwndSource _window;
    private readonly Action _onTriggered;
    private readonly int _id = Interlocked.Increment(ref s_nextId);

    public GlobalHotkey(string name, uint modifiers, uint vk, Action onTriggered)
    {
        _onTriggered = onTriggered;
        _window = new HwndSource(new HwndSourceParameters($"tomodachi.hotkey.{name}")
        {
            ParentWindow = Native.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);

        IsRegistered = Native.RegisterHotKey(_window.Handle, _id, modifiers | Native.MOD_NOREPEAT, vk);
        if (!IsRegistered) Log.Warn($"Hotkey '{name}' nicht registrierbar (Win32 {Marshal.GetLastPInvokeError()})");
    }

    public bool IsRegistered { get; }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam == _id)
        {
            handled = true;
            _onTriggered();
        }
        return 0;
    }

    public void Dispose()
    {
        if (IsRegistered) Native.UnregisterHotKey(_window.Handle, _id);
        _window.Dispose();
    }
}
