using System.Runtime.InteropServices;
using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Shell;

namespace Tomodachi.Notch;

/// <summary>
/// Fokus-Übergabe der Notch, aus der <see cref="NotchWindow"/> herausgezogen: merkt sich das zuvor aktive Fenster, gibt
/// den Fokus dorthin zurück und erkennt über einen Vordergrund-Hook Klicks in ein anderes Programm (dann zuklappen).
/// <para>
/// Hat tomodachi selbst den Fokus, ist der Tastatur-Hook blind (siehe WinKeyHook), deshalb halten Widgets und Fenster
/// den Fokus gar nicht erst. Nach einer eigenen Übergabe gilt eine kurze Schonfrist, damit sie nicht selbst als
/// „Klick woanders hin“ zählt.
/// </para>
/// </summary>
internal sealed unsafe class FocusHandoff
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    // Der native Vordergrund-Callback muss statisch sein und kommt so an seine Instanz
    private static FocusHandoff? s_instance;

    private readonly uint _ownProcessId;
    private readonly Func<bool> _isOpen;
    private readonly Action _onForeignForeground;
    private nint _previousForeground;
    private nint _hook;
    private DateTime _handoffAt;

    /// <param name="isOpen">Ob die Notch ausgeklappt ist (nur dann zählt ein fremdes Fenster im Vordergrund).</param>
    /// <param name="onForeignForeground">Ein anderes Programm ist nach vorn gekommen: die Notch soll zuklappen.</param>
    public FocusHandoff(uint ownProcessId, Func<bool> isOpen, Action onForeignForeground)
    {
        _ownProcessId = ownProcessId;
        _isOpen = isOpen;
        _onForeignForeground = onForeignForeground;
        s_instance = this;
    }

    /// <summary>In der Schonfrist nach der letzten eigenen Übergabe: Vordergrund-Wechsel nicht als „woanders hin“ werten.</summary>
    public bool InGrace => DateTime.UtcNow - _handoffAt < Grace;

    /// <summary>Eine eigene Fokus-Übergabe vermerken (Zeitstempel), z. B. bevor die Notch selbst nach vorn kommt.</summary>
    public void Mark() => _handoffAt = DateTime.UtcNow;

    /// <summary>Beim Öffnen: das aktuelle Vordergrundfenster merken (Flyouts/cloaked ignorieren), um es später zurückzuholen.</summary>
    public void Capture()
    {
        var previous = Native.GetForegroundWindow();
        _previousForeground = ShellWindows.IsShellFlyoutOrCloaked(previous) ? 0 : previous;
    }

    /// <summary>Fokus ans gemerkte Fenster zurückgeben und das als eigene Übergabe vermerken.</summary>
    public void GiveBack()
    {
        Mark();
        if (_previousForeground != 0 && Native.IsWindow(_previousForeground) && !Native.SetForegroundWindow(_previousForeground))
            Log.Debug("Fokus konnte nicht zurückgegeben werden");
    }

    /// <summary>Vordergrund-Hook an/aus. Ohne eigenen Fokus merkt die Notch Klicks woanders hin nur über Vordergrund-Wechsel.</summary>
    public void Hook(bool on)
    {
        if (on && _hook == 0)
        {
            delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback = &OnForegroundChanged;
            _hook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, (nint)callback,
                0, 0, Native.WINEVENT_OUTOFCONTEXT);
        }
        else if (!on && _hook != 0)
        {
            Native.UnhookWinEvent(_hook);
            _hook = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnForegroundChanged(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (s_instance is not { } self || !self._isOpen() || self.InGrace) return;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != self._ownProcessId) self._onForeignForeground();
    }
}
