using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Launcher;
using Tomodachi.Overlay;
using Tomodachi.Shell;
using Tomodachi.Widgets;

namespace Tomodachi.Notch;

public enum NotchTab
{
    Apps,
    Widgets,
    Windows,
    Power,
    Settings,
}

/// <summary>
/// Die Notch. Ausgeklappt zeigt sie Reiter: Apps (Launcher), Widgets (Dashboard) und Fenster.
/// <para>
/// Fokus: Nur der Apps-Reiter holt sich den Tastaturfokus (zum Tippen). Widgets und Fenster bleiben ohne Fokus,
/// denn hat tomodachi selbst den Fokus, ist der Tastatur-Hook blind (siehe WinKeyHook). Win+W, Win-Tipp und Esc
/// funktionieren dort deshalb zuverlässig. Wechselt man von Apps in einen anderen Reiter, geht der Fokus ans
/// vorherige Fenster zurück. Zugeklappt wird bei Fokusverlust (Apps) bzw. wenn ein anderes Programm nach vorn kommt.
/// </para>
/// </summary>
public partial class NotchWindow : OverlayWindow
{
    /// <summary>Transparenter Rand ums ausgeklappte Maximum, Platz für Hover und Schatten.</summary>
    private const double WindowPadding = 16;
    private const double HoverGrowWidth = 12;
    private const double HoverGrowHeight = 4;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    /// <summary>Nach einer eigenen Fokus-Übergabe kurz keine Vordergrund-Wechsel als „Klick woanders hin“ werten.</summary>
    private static readonly TimeSpan FocusHandoffGrace = TimeSpan.FromMilliseconds(500);

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static NotchWindow? s_instance;

    private readonly double _ear;
    private readonly double _width;
    private readonly double _height;
    private readonly double _appsWidth;
    private readonly double _widgetsWidth;
    private readonly double _expandedHeight;
    private readonly double _powerHeight;
    private readonly double _settingsHeight;
    private readonly double _widgetsHeight;
    private readonly double _integratedWidth;
    private readonly double _integratedBulge;
    private readonly double _avatarSize;
    private double _inset; // Höhe der Leiste bei „Leiste mit Notch“, sonst 0
    private bool _visible = true;
    private readonly double _corner;
    private readonly double _expandedCorner;
    private readonly Duration _fast;
    private readonly Duration _medium;
    private readonly DispatcherTimer _clock;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private LaunchHistory? _history;
    private WidgetsController? _widgets;
    private nint _previousForeground;
    private nint _foregroundHook;
    private DateTime _focusHandoffAt;
    private bool _suppressed;
    private bool _syncingTabs;

    public NotchWindow()
    {
        InitializeComponent();
        s_instance = this;

        _ear = Resource<double>("NotchEarRadius");
        _width = Resource<double>("NotchWidth");
        _height = Resource<double>("NotchHeight");
        _appsWidth = Resource<double>("NotchExpandedWidth");
        _widgetsWidth = Resource<double>("NotchWidgetsWidth");
        _expandedHeight = Resource<double>("NotchExpandedHeight");
        _powerHeight = Resource<double>("NotchPowerHeight");
        _settingsHeight = Resource<double>("NotchSettingsHeight");
        _widgetsHeight = Resource<double>("NotchWidgetsHeight");
        _integratedWidth = Resource<double>("NotchIntegratedWidth");
        _integratedBulge = Resource<double>("NotchIntegratedBulge");
        _avatarSize = Resource<double>("AvatarSize");
        _corner = Resource<double>("NotchCornerRadius");
        _expandedCorner = Resource<double>("NotchExpandedCornerRadius");
        _fast = Resource<Duration>("FastDuration");
        _medium = Resource<Duration>("MediumDuration");

        Width = Math.Max(_appsWidth, _widgetsWidth) + 2 * _ear + 2 * WindowPadding;
        Height = Math.Max(Math.Max(_expandedHeight, _widgetsHeight), _settingsHeight) +Resource<double>("TopBarHeight") + WindowPadding;
        Host.Width = _width + 2 * _ear;
        Host.Height = _height;
        Shape.CornerRadius = _corner;

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => HeaderTime.Text = DateTime.Now.ToString("HH:mm"), Dispatcher);

        PreviewKeyDown += OnPreviewKeyDown;
        Host.MouseEnter += OnHostMouseEnter;
        Host.MouseLeave += OnHostMouseLeave;
        Deactivated += OnDeactivated;
        DpiChanged += (_, _) => Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public bool IsOpen { get; private set; }

    public NotchTab Tab { get; private set; }

    /// <summary>Auf/zu, Reiter oder Fokus haben sich geändert. Feuert synchron.</summary>
    public event Action? NotchStateChanged;

    /// <summary>Klick auf das Profilbild.</summary>
    public event Action? AvatarClicked;

    /// <summary>
    /// Notch sichtbar (true) oder ausgeblendet (maximiert, Vollbild, Snap-Leiste). Die Leiste folgt dem, damit Notch und
    /// Leiste immer eine Einheit sind, auch wenn ein Win-Tipp die Notch über einem maximierten Fenster öffnet.
    /// </summary>
    public event Action<bool>? VisibleChanged;

    /// <summary>Im Power-Reiter bestätigt. Die Notch ist dann schon zugeklappt.</summary>
    public event Action<PowerAction>? PowerRequested;

    internal void Initialize(AppIndex index, LaunchHistory history, WidgetsController widgets, TomodachiConfig config)
    {
        RefreshAvatar();
        Settings.Initialize(config);
        Settings.AvatarChanged += RefreshAvatar;
        Settings.DialogClosed += () => _focusHandoffAt = DateTime.UtcNow;
        _history = history;
        _widgets = widgets;
        Launcher.Initialize(index, history);
        Launcher.LaunchRequested += OnLaunchRequested;
        Launcher.CloseRequested += () => CloseNotch(restoreFocus: true);
        Widgets.Initialize(widgets);
        Windows.Initialize(widgets, config);
        Windows.WindowActivated += () => CloseNotch(restoreFocus: false);
        Power.ActionRequested += action =>
        {
            CloseNotch(restoreFocus: true);
            PowerRequested?.Invoke(action);
        };
    }

    public void RefreshAvatar() => AvatarImage.Source = AvatarStore.Load();

    /// <summary>Nach einem Wechsel des Notch-Monitors in den Einstellungen.</summary>
    public void MoveToNotchMonitor() => Reposition();

    /// <summary>Über die obere Leiste legen. Beide sind „topmost“, es gewinnt das zuletzt nach vorn geholte Fenster.</summary>
    public void KeepOnTop()
    {
        if (Handle != 0)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Zu → öffnen. Offen auf diesem Reiter → zu. Offen auf einem anderen → Reiter wechseln.</summary>
    public void Toggle(NotchTab tab)
    {
        if (IsOpen && Tab == tab) CloseNotch(restoreFocus: true);
        else if (IsOpen) ShowTab(tab);
        else OpenNotch(tab);
    }

    /// <summary>Bei Vollbild-Programmen wegblenden. Ausgeklappt bleibt die Notch trotzdem sichtbar.</summary>
    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        UpdateVisibility();
    }

    private void OpenNotch(NotchTab tab)
    {
        IsOpen = true;
        var previous = Native.GetForegroundWindow();
        _previousForeground = Tomodachi.Shell.ShellWindows.IsShellFlyoutOrCloaked(previous) ? 0 : previous;

        HeaderTime.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        Header.Visibility = Visibility.Visible;
        Animate(Header, OpacityProperty, 1, _medium);
        UpdateVisibility();
        HookForeground(true);

        ShowTab(tab);
        Log.Debug($"Notch geöffnet ({tab})");
    }

    public void CloseNotch(bool restoreFocus, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (!IsOpen) return;
        IsOpen = false;
        var hadFocus = Activatable;
        Log.Debug($"Notch geschlossen ({caller}, Fokus zurück: {restoreFocus && hadFocus})");

        Activatable = false;
        HookForeground(false);
        _clock.Stop();
        _widgets?.Stop();
        AnimateShape(CollapsedWidth, CollapsedHeight, _corner, _medium);
        MoveAvatar(open: false);
        FadeOut(Header);
        FadeOut(Launcher);
        FadeOut(Widgets);
        FadeOut(Windows);
        FadeOut(Power);
        FadeOut(Settings);
        UpdateVisibility();
        NotchStateChanged?.Invoke();

        if (restoreFocus && hadFocus) GiveFocusBack();
    }

    /// <summary>Reiter zeigen, Notch auf seine Größe morphen, Fokus und Datenquellen passend schalten.</summary>
    private void ShowTab(NotchTab tab)
    {
        Tab = tab;
        SyncTabButtons();

        var (width, height) = TabSize(tab);
        AnimateShape(width, height, _expandedCorner, _medium);
        MoveAvatar(open: true);
        Show(Launcher, tab == NotchTab.Apps);
        Show(Widgets, tab == NotchTab.Widgets);
        Show(Windows, tab == NotchTab.Windows);
        Show(Power, tab == NotchTab.Power);
        Show(Settings, tab == NotchTab.Settings);
        if (tab == NotchTab.Power) Power.Reset();
        if (tab == NotchTab.Settings) Settings.Refresh();

        if (tab is NotchTab.Widgets or NotchTab.Windows) _widgets?.Start();
        else _widgets?.Stop();

        if (tab == NotchTab.Apps)
        {
            Launcher.Reset();
            TakeFocus();
        }
        else if (Activatable)
        {
            // Von Apps weg: Fokus zurückgeben, damit der Hook wieder sieht (Win+W, Esc)
            Activatable = false;
            GiveFocusBack();
        }

        NotchStateChanged?.Invoke();
    }

    private void TakeFocus()
    {
        Activatable = true;
        _focusHandoffAt = DateTime.UtcNow;
        var foreground = BringToForeground();
        Launcher.FocusQuery();
        if (!foreground) Log.Warn($"Notch konnte den Fokus nicht holen (Vordergrund bleibt {Native.GetForegroundWindow()})");
    }

    private void GiveFocusBack()
    {
        _focusHandoffAt = DateTime.UtcNow;
        if (_previousForeground != 0 && Native.IsWindow(_previousForeground) && !Native.SetForegroundWindow(_previousForeground))
            Log.Debug("Fokus konnte nicht zurückgegeben werden");
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Nur im Apps-Reiter mit Fokus heißt Deaktivieren „Klick woanders hin“. Eigene Fokus-Übergaben nicht mitzählen.
        if (!IsOpen || Tab != NotchTab.Apps || DateTime.UtcNow - _focusHandoffAt < FocusHandoffGrace) return;
        CloseNotch(restoreFocus: false, "Deactivated");
    }

    /// <summary>Ohne eigenen Fokus merkt die Notch Klicks woanders hin nur über Vordergrund-Wechsel.</summary>
    private void HookForeground(bool on)
    {
        if (on && _foregroundHook == 0)
        {
            unsafe
            {
                delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback = &OnForegroundChanged;
                _foregroundHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, (nint)callback,
                    0, 0, Native.WINEVENT_OUTOFCONTEXT);
            }
        }
        else if (!on && _foregroundHook != 0)
        {
            Native.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnForegroundChanged(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (s_instance is not { IsOpen: true } notch || DateTime.UtcNow - notch._focusHandoffAt < FocusHandoffGrace) return;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != notch._ownProcessId) notch.CloseNotch(restoreFocus: false, "anderes Fenster vorn");
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingTabs || !IsOpen) return;
        var tab = sender == TabWidgets ? NotchTab.Widgets
            : sender == TabWindows ? NotchTab.Windows
            : sender == TabPower ? NotchTab.Power
            : sender == TabSettings ? NotchTab.Settings
            : NotchTab.Apps;
        if (tab != Tab) ShowTab(tab); // Klick ist Eingabe bei uns: Fokus holen für Apps klappt
    }

    private void SyncTabButtons()
    {
        _syncingTabs = true;
        TabApps.IsChecked = Tab == NotchTab.Apps;
        TabWidgets.IsChecked = Tab == NotchTab.Widgets;
        TabWindows.IsChecked = Tab == NotchTab.Windows;
        TabPower.IsChecked = Tab == NotchTab.Power;
        TabSettings.IsChecked = Tab == NotchTab.Settings;
        _syncingTabs = false;
    }

    /// <summary>Esc schließt, Ctrl+Tab / Ctrl+Shift+Tab und Ctrl+1–5 wechseln Reiter. Greift nur mit Fokus (Apps-Reiter).</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsOpen) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.Key == Key.Escape)
        {
            CloseNotch(restoreFocus: true, "Esc");
        }
        else if (ctrl && e.Key == Key.Tab)
        {
            var count = Enum.GetValues<NotchTab>().Length;
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? count - 1 : 1;
            ShowTab((NotchTab)(((int)Tab + step) % count));
        }
        else if (ctrl && e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5)
        {
            ShowTab((NotchTab)(e.Key - Key.D1));
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private void OnAvatarClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AvatarClicked?.Invoke();
    }

    private void OnLaunchRequested(AppEntry app, bool elevated)
    {
        // Erst starten, solange wir noch Vordergrund sind: AllowSetForegroundWindow wirkt nur dann
        var launched = AppLauncher.Launch(app, elevated);
        if (launched) _history?.RecordLaunch(app.Id, DateTime.UtcNow);
        CloseNotch(restoreFocus: !launched);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Reposition();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        HookForeground(false);
        base.OnClosed(e);
    }

    /// <summary>
    /// Bei Vollbild ausblenden und danach das Fenster ganz verstecken, nicht nur durchsichtig lassen: Ein Topmost-Fenster
    /// über einem Spiel, auch ein unsichtbares, kann den direkten Darstellungsweg (Independent Flip) kosten.
    /// </summary>
    private void UpdateVisibility()
    {
        var visible = !_suppressed || IsOpen;
        Root.IsHitTestVisible = visible;
        if (visible != _visible)
        {
            _visible = visible;
            VisibleChanged?.Invoke(visible);
        }
        if (visible)
        {
            Visibility = Visibility.Visible; // ShowActivated = false: holt keinen Fokus
            Animate(Root, OpacityProperty, 1, _medium);
            return;
        }

        var fade = new DoubleAnimation(0, _medium) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            if (_suppressed && !IsOpen) Visibility = Visibility.Hidden;
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void OnHostMouseEnter(object sender, MouseEventArgs e)
    {
        if (!IsOpen) AnimateShape(CollapsedWidth + HoverGrowWidth, CollapsedHeight + HoverGrowHeight, _corner, _fast);
    }

    private void OnHostMouseLeave(object sender, MouseEventArgs e)
    {
        if (!IsOpen) AnimateShape(CollapsedWidth, CollapsedHeight, _corner, _fast);
    }

    private void AnimateShape(double bodyWidth, double height, double corner, Duration duration)
    {
        Animate(Host, WidthProperty, bodyWidth + 2 * _ear, duration);
        Animate(Host, HeightProperty, height, duration);
        Animate(Shape, NotchShape.CornerRadiusProperty, corner, duration);
    }

    /// <summary>
    /// „Leiste mit Notch“ an (Höhe der Leiste) oder aus (0). Die Form beginnt dann an der Unterkante der Leiste: Ihre
    /// Füllung überdeckt dort die Trennlinie der Leiste, ihre Ohren und ihre Kontur laufen in die Leiste über. So ist es
    /// eine einzige Form. Kopfzeile und Reiter rutschen unter die Leiste.
    /// </summary>
    public void SetBarInset(double inset)
    {
        if (Math.Abs(inset - _inset) < 0.5) return;
        _inset = inset;
        Shape.Margin = new Thickness(0, inset, 0, 0);
        Header.Margin = new Thickness(0, inset, 0, 0);
        foreach (var view in new FrameworkElement[] { Launcher, Widgets, Windows, Settings, Power })
            view.Margin = new Thickness(0, 48 + inset, 0, 0);

        if (IsOpen)
        {
            var (width, height) = TabSize(Tab);
            AnimateShape(width, height, _expandedCorner, _medium);
        }
        else
        {
            AnimateShape(CollapsedWidth, CollapsedHeight, _corner, _medium);
        }
        MoveAvatar(IsOpen);
    }

    /// <summary>Zugeklappt: ohne Leiste die klassische Notch, mit Leiste eine schmale Wölbung unter ihr.</summary>
    private double CollapsedWidth => _inset > 0 ? _integratedWidth : _width;

    private double CollapsedHeight => _inset > 0 ? _inset + _integratedBulge : _height;

    private (double Width, double Height) TabSize(NotchTab tab)
    {
        var width = tab == NotchTab.Widgets ? _widgetsWidth : _appsWidth;
        var height = tab switch
        {
            NotchTab.Power => _powerHeight,
            NotchTab.Settings => _settingsHeight,
            NotchTab.Widgets => _widgetsHeight,
            _ => _expandedHeight,
        };
        return (width, _inset + height);
    }

    /// <summary>Profilbild: zugeklappt mittig auf der Kante zwischen Leiste und Wölbung, aufgeklappt in der Kopfzeile.</summary>
    private void MoveAvatar(bool open)
    {
        var top = open ? _inset + 5
            : _inset > 0 ? (_inset + _integratedBulge - _avatarSize) / 2
            : 5;
        Avatar.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, top, 0, 0), _medium) { EasingFunction = Ease });
    }

    private void Show(UIElement element, bool visible)
    {
        if (visible)
        {
            element.Visibility = Visibility.Visible;
            Animate(element, OpacityProperty, 1, _medium);
        }
        else
        {
            FadeOut(element);
        }
    }

    /// <summary>Ausblenden und danach einklappen, außer das Element wurde inzwischen wieder eingeblendet.</summary>
    private void FadeOut(UIElement element)
    {
        if (element.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(0, _medium) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            if (element.Opacity == 0) element.Visibility = Visibility.Collapsed;
        };
        element.BeginAnimation(OpacityProperty, fade);
    }

    private static void Animate(UIElement target, DependencyProperty property, double to, Duration duration) =>
        target.BeginAnimation(property, new DoubleAnimation(to, duration) { EasingFunction = Ease });

    private T Resource<T>(string key) => (T)FindResource(key);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Monitors.Invalidate();
        Dispatcher.BeginInvoke(Reposition);
    }

    /// <summary>Oben mittig, bündig an der Bildschirmkante des Notch-Monitors (Config <c>NotchMonitor</c>), auf ganze Pixel gerundet.</summary>
    private void Reposition()
    {
        if (Handle == 0) return;

        var monitor = Monitors.Info(Monitors.NotchHandle()).rcMonitor;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Width * dpi.DpiScaleX);
        MoveToDevicePixels(monitor.Left + (monitor.Width - width) / 2, monitor.Top);
    }
}
