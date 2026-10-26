using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Tomodachi.Interop;

namespace Tomodachi.Overlay;

/// <summary>
/// Basis für alle Shell-Fenster: randlos, per-Pixel-transparent, immer oben, nicht in Alt+Tab,
/// klaut standardmäßig keinen Fokus. Voll transparente Pixel sind automatisch klickdurchlässig (Layered Window).
/// Panels mit Tastatureingabe (Launcher) schalten <see cref="Activatable"/> vorübergehend ein.
/// </summary>
public class OverlayWindow : Window
{
    private bool _clickThrough;
    private bool _activatable;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
    }

    protected nint Handle { get; private set; }

    /// <summary>Gesamtes Fenster klickdurchlässig, auch dort, wo es sichtbar ist.</summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            if (Handle != 0) ApplyExStyle();
        }
    }

    /// <summary>Darf das Fenster aktiviert werden und Tastatureingaben bekommen?</summary>
    public bool Activatable
    {
        get => _activatable;
        set
        {
            _activatable = value;
            if (Handle != 0) ApplyExStyle();
        }
    }

    /// <summary>
    /// Holt das Fenster nach vorn. Windows erlaubt das nur mit Vordergrund-Recht, zum Beispiel direkt nach einem
    /// empfangenen WM_HOTKEY (deshalb läuft der Win-Tipp über einen F24-Hotkey, siehe App). Tricks wie Alt-Injektion
    /// oder SwitchToThisWindow öffneten auf Windows 11 das Startmenü (26.10.2026) und sind bewusst raus.
    /// Ebenso AttachThreadInput: Ist der Vordergrund die unsichtbar geparkte Windows-Suche, wird sie dadurch wieder aktiv.
    /// </summary>
    public bool BringToForeground()
    {
        if (Native.GetForegroundWindow() == Handle) return true;
        Native.SetForegroundWindow(Handle);
        Activate();
        return Native.GetForegroundWindow() == Handle;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        ApplyExStyle();
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
    }

    /// <summary>
    /// Position in physischen Pixeln des virtuellen Bildschirms. Läuft über Left/Top,
    /// weil WPF ein direktes SetWindowPos beim nächsten Layout-Durchlauf überschreibt.
    /// </summary>
    protected void MoveToDevicePixels(int x, int y)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
    }

    /// <summary>Aktuelle Fenstergröße in physischen Pixeln.</summary>
    protected (int Width, int Height) DeviceSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return ((int)Math.Round(ActualWidth * dpi.DpiScaleX), (int)Math.Round(ActualHeight * dpi.DpiScaleY));
    }

    private void ApplyExStyle()
    {
        var style = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE);
        style |= Native.WS_EX_TOOLWINDOW;
        style &= ~Native.WS_EX_APPWINDOW;
        style = _activatable ? style & ~Native.WS_EX_NOACTIVATE : style | Native.WS_EX_NOACTIVATE;
        style = _clickThrough ? style | Native.WS_EX_TRANSPARENT : style & ~Native.WS_EX_TRANSPARENT;

        Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, style);
        Native.SetWindowPos(Handle, 0, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Klicks auf die Shell sollen das Vordergrundfenster nicht deaktivieren, außer ein Panel braucht die Tastatur
        if (msg == Native.WM_MOUSEACTIVATE && !_activatable)
        {
            handled = true;
            return Native.MA_NOACTIVATE;
        }
        return 0;
    }
}
