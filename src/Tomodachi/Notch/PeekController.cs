using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using Tomodachi.Core;
using Tomodachi.Interop;

namespace Tomodachi.Notch;

/// <summary>Geometrie der zugeklappten Notch, die der Peek fürs Einblenden braucht. Ändert sich mit der Leiste (Inset).</summary>
internal readonly record struct PeekGeometry(double CollapsedWidth, double CollapsedHeight, double Ear, double Inset);

/// <summary>
/// „Peek“: Ist die Notch ausgeblendet (Vollbild), holt ein Verweilen des Zeigers oben am Rand sie samt Leiste zurück,
/// bis er sich von beiden entfernt (27.10.2026, Wunsch von deadprocess). Aus der <see cref="NotchWindow"/> herausgezogen,
/// damit die reine Zeiger-Logik ohne das übrige Fenster für sich steht. Nicht bei gedrückter Maustaste, denn wer ein
/// Fenster an den oberen Rand zieht, will die Snap-Leiste. Nicht bei ausgeblendetem Zeiger, also nicht mitten im Spiel.
/// </summary>
internal sealed class PeekController
{
    /// <summary>Ausgeblendet: Zeiger so nah (DIP) am oberen Rand über der Notch blendet sie ein.</summary>
    private const double PeekEdge = 6;
    /// <summary>Rand (DIP) um Notch bzw. Leiste, in dem sie eingeblendet bleibt.</summary>
    private const double PeekMargin = 24;
    /// <summary>So lange muss der Zeiger oben verweilen. Nur durchgewischt (Tab in Firefox anklicken) blendet nichts ein.</summary>
    private static readonly TimeSpan PeekDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan PeekHold = TimeSpan.FromMilliseconds(500);

    private readonly Visual _owner;
    private readonly uint _ownProcessId;
    private readonly Func<bool> _isOpen;
    private readonly Func<PeekGeometry> _geometry;
    private readonly Action _onChanged;
    private readonly DispatcherTimer _timer;

    private DateTime? _nearSince;
    private DateTime _lastNear;

    /// <param name="owner">Fenster, dessen DPI der Peek für die Umrechnung von DIP in Pixel nutzt.</param>
    /// <param name="isOpen">Ob die Notch ausgeklappt ist (dann bleibt sie ohnehin sichtbar).</param>
    /// <param name="geometry">Aktuelle Maße der zugeklappten Notch (ändern sich mit der Leiste).</param>
    /// <param name="onChanged">Wird gerufen, wenn sich <see cref="Peeking"/> ändert (die Notch neu ein- oder ausblenden).</param>
    public PeekController(Visual owner, uint ownProcessId, Func<bool> isOpen, Func<PeekGeometry> geometry, Action onChanged)
    {
        _owner = owner;
        _ownProcessId = ownProcessId;
        _isOpen = isOpen;
        _geometry = geometry;
        _onChanged = onChanged;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Check(), owner.Dispatcher);
        _timer.Stop();
    }

    /// <summary>Notch gerade per Maus zurückgeholt.</summary>
    public bool Peeking { get; private set; }

    /// <summary>
    /// Beim Wechsel sichtbar/ausgeblendet aufrufen: den Peek-Zustand zurücksetzen und den Zeiger nur beobachten,
    /// solange die Notch ausgeblendet ist (<paramref name="active"/>).
    /// </summary>
    public void SetActive(bool active)
    {
        Peeking = false;
        _nearSince = null;
        if (active) _timer.Start();
        else _timer.Stop();
    }

    private void Check()
    {
        var info = new CURSORINFO { cbSize = (uint)Marshal.SizeOf<CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info)) return;
        var cursor = info.ptScreenPos;
        var now = DateTime.UtcNow;

        if (Peeking)
        {
            if (_isOpen() || CursorNearShown(cursor)) _lastNear = now;
            else if (now - _lastNear > PeekHold) Stop();
            return;
        }

        var buttonDown = Native.GetAsyncKeyState(Native.VK_LBUTTON) < 0 || Native.GetAsyncKeyState(Native.VK_RBUTTON) < 0;
        if (buttonDown || (info.flags & Native.CURSOR_SHOWING) == 0 || !CursorAtNotchEdge(cursor))
        {
            _nearSince = null;
            return;
        }
        _nearSince ??= now;
        if (now - _nearSince < PeekDelay) return;

        Log.Debug("Notch per Maus eingeblendet");
        Peeking = true;
        _lastNear = now;
        _onChanged();
    }

    private void Stop()
    {
        Log.Debug("Notch per Maus wieder ausgeblendet");
        Peeking = false;
        _nearSince = null;
        _onChanged();
    }

    /// <summary>Oben am Rand des Notch-Monitors, waagrecht im Bereich der zugeklappten Notch (plus Rand).</summary>
    private bool CursorAtNotchEdge(POINT cursor)
    {
        var geo = _geometry();
        var monitor = Monitors.Info(Monitors.NotchHandle()).rcMonitor;
        var scale = VisualTreeHelper.GetDpi(_owner).DpiScaleX;
        var halfWidth = (geo.CollapsedWidth / 2 + geo.Ear + PeekMargin) * scale;
        var center = monitor.Left + monitor.Width / 2.0;
        return cursor.Y >= monitor.Top && cursor.Y < monitor.Top + PeekEdge * scale
            && Math.Abs(cursor.X - center) <= halfWidth;
    }

    /// <summary>
    /// Über Notch oder Leiste (plus Rand) oder über einem eigenen Fenster, etwa dem Rechtsklick-Menü eines Leisten-Tabs.
    /// Durchsichtige Stellen der eigenen Fenster lässt Windows durch, die zählen also nicht.
    /// </summary>
    private bool CursorNearShown(POINT cursor)
    {
        var hwnd = Native.WindowFromPoint(cursor);
        if (hwnd != 0)
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == _ownProcessId) return true;
        }

        var geo = _geometry();
        var monitor = Monitors.Info(Monitors.NotchHandle()).rcMonitor;
        var scale = VisualTreeHelper.GetDpi(_owner).DpiScaleX;
        var halfWidth = geo.Inset > 0 ? monitor.Width / 2.0 : (geo.CollapsedWidth / 2 + geo.Ear + PeekMargin) * scale;
        var center = monitor.Left + monitor.Width / 2.0;
        return cursor.Y >= monitor.Top && cursor.Y < monitor.Top + (geo.CollapsedHeight + PeekMargin) * scale
            && Math.Abs(cursor.X - center) <= halfWidth;
    }
}
