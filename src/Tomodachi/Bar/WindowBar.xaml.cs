using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Overlay;
using Tomodachi.Widgets;

namespace Tomodachi.Bar;

public sealed class BarTab(nint handle) : Observable
{
    private string _title = "";
    private ImageSource? _icon;
    private bool _isActive, _minimized, _showTitle = true;
    private double _width = BarLayout.MaxTabWidth;

    public nint Handle { get; } = handle;
    public string Title { get => _title; set => Set(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool Minimized { get => _minimized; set => Set(ref _minimized, value); }
    public double Width { get => _width; set => Set(ref _width, value); }
    public bool ShowTitle { get => _showTitle; set => Set(ref _showTitle, value); }
}

/// <summary>
/// Obere Leiste über die ganze Breite des Notch-Monitors, mit der Notch in der Mitte. Offene Fenster als Tabs links der
/// Notch (Icon + Titel), das aktive hervorgehoben. Klick holt nach vorn bzw. minimiert das aktive, Rechtsklick öffnet das
/// <see cref="WindowMenu"/>. Reserviert keinen Platz: Wie die Notch liegt sie über den Fenstern und blendet sich mit ihr
/// aus, sobald ein Fenster maximiert oder im Vollbild ist oder die Snap-Leiste erscheint (Wunsch von deadprocess:
/// „gleiches Spiel wie Notch only“). Aktualisiert alle 500 ms, aber nur solange sie sichtbar ist.
/// </summary>
public partial class WindowBar : OverlayWindow
{
    private const double NotchMargin = 16; // Abstand der Tabs zur Notch, auch beim Hover-Wachsen

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly List<BarTab> _tabs = [];
    private readonly ObservableCollection<BarTab> _left = [];
    private readonly WindowListService _windows = new();
    private readonly DispatcherTimer _timer;
    private readonly double _barHeight;
    private TomodachiConfig? _config;
    private bool _enabled = true;
    private bool _suppressed;

    public WindowBar()
    {
        InitializeComponent();
        LeftTabs.ItemsSource = _left;
        _barHeight = (double)FindResource("TopBarHeight");
        NotchGap.Width = new GridLength((double)FindResource("NotchIntegratedWidth") + 2 * (double)FindResource("NotchEarRadius") + 2 * NotchMargin);
        Height = _barHeight;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        _timer.Stop();
        DpiChanged += (_, _) => Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Nach jedem Einblenden. Die Notch muss über der Leiste liegen, beide sind „topmost“.</summary>
    public event Action? Shown;

    /// <summary>
    /// Leiste da (true) oder weg (false, ausgeschaltet oder Vollbild). Die Notch wölbt sich dann aus der Leiste heraus
    /// bzw. sitzt wieder allein am Bildschirmrand.
    /// </summary>
    public event Action<bool>? PresenceChanged;

    public double BarHeight => _barHeight;

    /// <summary>Vor <c>Show()</c> aufrufen, damit eine ausgeschaltete Leiste gar nicht erst aufblitzt.</summary>
    internal void Initialize(TomodachiConfig config)
    {
        _config = config;
        _enabled = config.ShowTopBar;
    }

    /// <summary>Einstellung „Leiste mit Notch“ / „Nur Notch“, wirkt sofort.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        UpdateVisibility();
    }

    /// <summary>
    /// Folgt der Notch: Ist sie ausgeblendet (maximiert, Vollbild, Snap-Leiste), ist es auch die Leiste. Öffnet ein
    /// Win-Tipp die Notch trotzdem, kommt die Leiste mit.
    /// </summary>
    public void SetNotchVisible(bool visible)
    {
        _suppressed = !visible;
        UpdateVisibility();
    }

    /// <summary>Nach einem Wechsel des Notch-Monitors in den Einstellungen.</summary>
    public void MoveToNotchMonitor() => Reposition();

    private void UpdateVisibility()
    {
        if (_enabled && !_suppressed)
        {
            Refresh();
            _timer.Start();
            Visibility = Visibility.Visible; // ShowActivated = false: holt keinen Fokus
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = Ease });
            // Erst wenn die Leiste wirklich steht, sonst landet sie danach doch wieder über der Notch
            Dispatcher.BeginInvoke(() => Shown?.Invoke(), DispatcherPriority.ApplicationIdle);
            PresenceChanged?.Invoke(true);
            return;
        }

        PresenceChanged?.Invoke(false);
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            // Ganz verstecken statt nur durchsichtig: Ein Topmost-Fenster über einem Spiel kann Leistung kosten
            if (!_enabled || _suppressed) Visibility = Visibility.Hidden;
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void Refresh()
    {
        var snapshot = _windows.Snapshot();
        var foreground = Native.GetForegroundWindow();
        var byHandle = snapshot.ToDictionary(w => w.Handle);

        var order = BarLayout.Order(_tabs.Select(t => t.Handle).ToList(), snapshot.Select(w => w.Handle).ToList());
        var known = _tabs.ToDictionary(t => t.Handle);
        _tabs.Clear();
        foreach (var handle in order)
        {
            if (!known.TryGetValue(handle, out var tab)) tab = new BarTab(handle) { Icon = _windows.Icon(handle) };
            var info = byHandle[handle];
            tab.Title = info.Title;
            tab.Minimized = info.Minimized;
            tab.IsActive = handle == foreground;
            _tabs.Add(tab);
        }

        // Nur links der Notch, rechts bleibt frei
        var side = Math.Max(0, (ActualWidth - NotchGap.ActualWidth) / 2 - 8);
        var (width, showTitle) = BarLayout.TabWidth(side, _tabs.Count);
        foreach (var tab in _tabs)
        {
            tab.Width = width;
            tab.ShowTitle = showTitle;
        }
        Sync(_left, _tabs);
    }

    /// <summary>Nur ändern, was sich geändert hat, sonst flackern die Tabs jede halbe Sekunde.</summary>
    private static void Sync(ObservableCollection<BarTab> target, IReadOnlyList<BarTab> desired)
    {
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            var existing = target.IndexOf(desired[i]);
            if (existing >= 0) target.Move(existing, i);
            else target.Insert(i, desired[i]);
        }
        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }

    /// <summary>Wie die Taskleiste: Klick holt nach vorn, Klick aufs schon aktive Fenster minimiert es.</summary>
    private void OnTabClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: nint hwnd }) return;
        if (hwnd == Native.GetForegroundWindow() && !Native.IsIconic(hwnd)) WindowListService.Minimize(hwnd);
        else WindowListService.Activate(hwnd);
        Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
    }

    private void OnTabRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: nint hwnd } tab) return;
        e.Handled = true;
        WindowMenu.Show(tab, hwnd, _config);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Reposition();
        UpdateVisibility();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Monitors.Invalidate();
        Dispatcher.BeginInvoke(Reposition);
    }

    /// <summary>Oben über die ganze Breite des Notch-Monitors, in physischen Pixeln ausgerichtet.</summary>
    private void Reposition()
    {
        if (Handle == 0) return;

        var monitor = Monitors.Info(Monitors.NotchHandle()).rcMonitor;
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = monitor.Width / dpi.DpiScaleX;
        Height = _barHeight;
        MoveToDevicePixels(monitor.Left, monitor.Top);
        if (_enabled && !_suppressed) Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Loaded);
    }
}
