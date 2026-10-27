using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
/// <see cref="WindowMenu"/>, Ziehen sortiert die Tabs um (Wunsch von deadprocess). Reserviert keinen Platz: Wie die Notch liegt sie über den Fenstern und blendet sich mit ihr
/// aus, sobald ein Fenster maximiert oder im Vollbild ist oder die Snap-Leiste erscheint (Wunsch von deadprocess:
/// „gleiches Spiel wie Notch only“). Aktualisiert alle 500 ms, aber nur solange sie sichtbar ist.
/// </summary>
public partial class WindowBar : OverlayWindow
{
    private const double NotchMargin = 16; // Abstand der Tabs zur Notch, auch beim Hover-Wachsen

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(150);

    private readonly List<BarTab> _tabs = [];
    private readonly ObservableCollection<BarTab> _left = [];
    private readonly WindowListService _windows = new();
    private readonly DispatcherTimer _timer;
    private readonly double _barHeight;
    private TomodachiConfig? _config;
    private bool _enabled = true;
    private bool _suppressed;

    // Umsortieren per Ziehen
    private BarTab? _pressed;   // linke Taste auf diesem Tab gedrückt, aber noch nicht weit genug bewegt
    private Point _pressPoint;
    private double _grabOffset; // wo im Tab gegriffen wurde
    private BarTab? _dragged;

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
        if (_dragged is not null) return; // sonst springt der gezogene Tab unter der Maus weg

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

    private void OnTabPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BarTab tab } || Container(tab) is not { } container) return;
        _pressed = tab;
        _pressPoint = e.GetPosition(LeftTabs);
        _grabOffset = e.GetPosition(container).X;
    }

    /// <summary>
    /// Erst ab ein paar Pixeln wird aus dem Klick ein Ziehen. Dann hängt der Tab an der Maus, die anderen rutschen zur
    /// Seite, sobald seine Mitte über ihrem Platz liegt. Die Maus hängt dabei an der ganzen Reihe, sodass der Tab das
    /// Loslassen nicht als Klick sieht.
    /// </summary>
    private void OnTabsMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragged is null)
        {
            if (_pressed is null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pressed = null;
                return;
            }
            if (Math.Abs(e.GetPosition(LeftTabs).X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _dragged = _pressed;
            _pressed = null;
            LeftTabs.CaptureMouse();
        }

        if (Container(_dragged) is not { } container || VisualTreeHelper.GetParent(container) is not UIElement row) return;
        var slot = container.ActualWidth; // alle Tabs gleich breit
        var left = Math.Clamp(e.GetPosition(row).X - _grabOffset, 0, (_tabs.Count - 1) * slot);
        var from = _tabs.IndexOf(_dragged);
        var to = BarLayout.DropIndex(left, slot, _tabs.Count);
        if (from >= 0 && to != from) MoveTab(from, to);

        container = Container(_dragged) ?? container;
        Panel.SetZIndex(container, 1);
        var shift = Shift(container);
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.X = left - VisualTreeHelper.GetOffset(container).X;
    }

    /// <summary>Verschiebt den Tab und lässt die übersprungenen weich an ihren neuen Platz gleiten.</summary>
    private void MoveTab(int from, int to)
    {
        var before = _left.ToDictionary(t => t, VisualX);
        _tabs.RemoveAt(from);
        _tabs.Insert(to, _dragged!);
        _left.Move(from, to);
        LeftTabs.UpdateLayout();

        foreach (var tab in _left)
        {
            if (tab == _dragged || Container(tab) is not { } container) continue;
            var delta = before[tab] - VisualTreeHelper.GetOffset(container).X;
            if (Math.Abs(delta) < 0.5) continue;
            Shift(container).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(delta, 0, SlideDuration) { EasingFunction = Ease });
        }
    }

    private void OnTabsReleased(object sender, MouseButtonEventArgs e)
    {
        _pressed = null;
        if (_dragged is null) return;
        e.Handled = true;
        EndDrag();
    }

    private void OnTabsLostCapture(object sender, MouseEventArgs e) => EndDrag();

    /// <summary>Tab gleitet in seinen Platz, die Leiste aktualisiert wieder. Die neue Reihenfolge hält <see cref="BarLayout.Order"/>.</summary>
    private void EndDrag()
    {
        if (_dragged is not { } tab) return;
        _dragged = null;
        if (LeftTabs.IsMouseCaptured) LeftTabs.ReleaseMouseCapture();

        if (Container(tab) is { } container)
        {
            var shift = Shift(container);
            var settle = new DoubleAnimation(shift.X, 0, SlideDuration) { EasingFunction = Ease };
            settle.Completed += (_, _) => Panel.SetZIndex(container, 0);
            shift.BeginAnimation(TranslateTransform.XProperty, settle);
        }
        Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Background);
    }

    private FrameworkElement? Container(BarTab tab) => LeftTabs.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;

    /// <summary>Wo der Tab gerade zu sehen ist, samt laufender Verschiebung.</summary>
    private double VisualX(BarTab tab) =>
        Container(tab) is { } container ? VisualTreeHelper.GetOffset(container).X + Shift(container).X : 0;

    private static TranslateTransform Shift(UIElement container)
    {
        if (container.RenderTransform is TranslateTransform shift) return shift;
        container.RenderTransform = shift = new TranslateTransform();
        return shift;
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
