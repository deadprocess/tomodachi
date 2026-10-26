using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Tomodachi.Wallpaper;

/// <summary>
/// Vollbild-Overlay mit dem Band aus schrägen Wallpaper-Karten. Wird für jedes Öffnen neu erzeugt und danach
/// geschlossen, damit WPF die Renderflächen des Vollbildfensters freigibt; ein nur verstecktes Fenster behält sie.
/// </summary>
public partial class WallpaperOverlay : Window
{
    private static readonly string Hint = Localization.Loc.Get("Wallpaper_Hint");

    private static string? s_lastPath;

    private readonly WallpaperLibrary _library;
    private readonly IntPtr _hwnd;
    private IReadOnlyList<WallpaperItem> _items = [];
    private int _selected = -1;
    private string? _monitorId;
    private double _viewWidth;
    private double _cardWidth;
    private double _fadeWidth;
    private bool _trackingFade;
    private int _wheelDelta;

    public WallpaperOverlay(WallpaperLibrary library)
    {
        _library = library;
        InitializeComponent();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
    }

    public bool IsOpen { get; private set; }

    public void ShowPicker()
    {
        if (IsOpen)
        {
            Activate();
            return;
        }
        IsOpen = true;

        Scene.BeginAnimation(OpacityProperty, null);
        Scene.Opacity = 0;
        Show();

        // Auf dem Monitor öffnen, auf dem gerade die Maus steht
        WallpaperNative.RECT bounds = WallpaperNative.GetCursorMonitorBounds();
        WallpaperNative.CoverArea(_hwnd, bounds);
        UpdateMetrics(bounds);
        _monitorId = DesktopWallpaper.FindMonitorId(bounds);

        _items = _library.Refresh();
        foreach (var item in _items)
            item.IsSelected = false;
        _selected = -1;
        Cards.ItemsSource = _items;
        DateText.Text = FormatDate(DateTime.Now, Localization.Loc.Culture);
        HintText.Text = _items.Count > 0 ? Hint : Localization.Loc.Format("Wallpaper_NoImages", _library.Folder);

        // Mit dem aktuellen Wallpaper dieses Monitors starten, sonst mit dem zuletzt gewählten
        int index = IndexOfPath(DesktopWallpaper.GetCurrent(_monitorId));
        if (index < 0)
            index = Math.Max(0, IndexOfPath(s_lastPath));
        Select(index, animate: false);
        // Die Karten-Container entstehen erst beim nächsten Layout-Durchlauf
        Dispatcher.InvokeAsync(UpdateEdgeFade, DispatcherPriority.Loaded);

        Activate();
        WallpaperNative.SetForegroundWindow(_hwnd);
        Focus();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Scene.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        Rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    public void HidePicker()
    {
        if (!IsOpen) return;
        IsOpen = false;
        TrackEdgeFade(false);

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => Close();
        Scene.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateMetrics(WallpaperNative.RECT bounds)
    {
        double scale = WallpaperNative.GetDpiForWindow(_hwnd) / 96.0;
        _viewWidth = bounds.Width / scale;
        _cardWidth = CardLayout.CardWidth(_viewWidth, bounds.Height / scale);
        double cardHeight = _cardWidth * CardLayout.HeightPerWidth;
        double overhang = CardLayout.SkewOverhang(cardHeight) + 1;

        Resources["CardWidth"] = _cardWidth;
        Resources["CardHeight"] = cardHeight;
        Resources["CardImageMargin"] = new Thickness(-overhang, 0, -overhang, 0);

        Band.Height = cardHeight * 1.12;
        Canvas.SetTop(Cards, cardHeight * 0.06);
        DateText.FontSize = Math.Round(_cardWidth * 0.17);
        DateText.Margin = new Thickness(0, 0, 0, cardHeight * 0.07);
        HintText.FontSize = Math.Max(12, Math.Round(_cardWidth * 0.06));
        HintText.Margin = new Thickness(0, cardHeight * 0.06, 0, 0);
        _fadeWidth = Math.Min(_cardWidth * 1.5, _viewWidth * 0.15);
    }

    // Karten zu den Bildschirmrändern hin ausblenden. Bewusst pro Karte statt mit einer OpacityMask auf dem
    // Band: Für die Maske legt WPF eine Zwischenfläche über das ganze, vielfach bildschirmbreite Band an
    // (gemessen gut 100 MB mehr, auch mit ClipToBounds).
    private void UpdateEdgeFade()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (Cards.ItemContainerGenerator.ContainerFromIndex(i) is not UIElement card) continue;
            double center = Scroll.X + CardCenter(i);
            card.Opacity = Math.Clamp(Math.Min(center, _viewWidth - center) / _fadeWidth, 0, 1);
        }
    }

    // Während das Band gleitet, die Ausblendung jedes Frame nachführen – nur dann, weil ein
    // Rendering-Abo WPF zum Dauerzeichnen des Vollbildfensters zwingt.
    private void TrackEdgeFade(bool enable)
    {
        if (enable == _trackingFade) return;
        _trackingFade = enable;
        if (enable)
            CompositionTarget.Rendering += OnRendering;
        else
            CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e) => UpdateEdgeFade();

    private double CardCenter(int index) => index * (_cardWidth + CardLayout.Gap) + CardLayout.Gap / 2 + _cardWidth / 2;

    private void Select(int index, bool animate)
    {
        if (_items.Count == 0) return;
        index = Math.Clamp(index, 0, _items.Count - 1);

        if (_selected >= 0)
            _items[_selected].IsSelected = false;
        _selected = index;
        _items[index].IsSelected = true;
        s_lastPath = _items[index].Path;

        // Ausgewählte Karte in die Bildschirmmitte schieben
        double target = _viewWidth / 2 - CardCenter(index);
        if (animate)
        {
            var slide = new DoubleAnimation(target, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            slide.Completed += (_, _) =>
            {
                TrackEdgeFade(false);
                UpdateEdgeFade();
            };
            TrackEdgeFade(true);
            Scroll.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        else
        {
            Scroll.BeginAnimation(TranslateTransform.XProperty, null);
            Scroll.X = target;
            UpdateEdgeFade();
        }
    }

    private void Apply(WallpaperItem item, bool allMonitors)
    {
        string? monitorId = allMonitors ? null : _monitorId;
        s_lastPath = item.Path;
        HidePicker();

        // Erst nach dem Ausblenden setzen – Windows braucht dafür einen Moment, das Overlay soll nicht hängen.
        // Über den Dispatcher der App, weil dieses Fenster bis dahin schon geschlossen sein kann.
        Application.Current.Dispatcher.InvokeAsync(() => DesktopWallpaper.Apply(item.Path, monitorId), DispatcherPriority.Background);
    }

    private int IndexOfPath(string? path)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    // "18 SEP 2026" mit weitem Buchstabenabstand; TextBlock kennt kein Letter-Spacing, daher schmale Leerzeichen.
    // Monat in der Sprache der Oberfläche ("27 OKT 2026"), passend zum Hinweis darunter. Punkte wie in "Okt." fallen weg.
    internal static string FormatDate(DateTime date, CultureInfo culture) =>
        string.Join(" ", date.ToString("dd MMM yyyy", culture).Replace(".", "").ToUpper(culture).Select(c => c.ToString()));

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                Select(_selected - 1, animate: true);
                break;
            case Key.Right:
                Select(_selected + 1, animate: true);
                break;
            case Key.Home:
                Select(0, animate: true);
                break;
            case Key.End:
                Select(_items.Count - 1, animate: true);
                break;
            case Key.Enter when _selected >= 0:
                Apply(_items[_selected], allMonitors: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
            case Key.Escape:
                HidePicker();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Touchpads liefern viele kleine Schritte; erst nach einer vollen Mausrad-Raste weiterblättern.
        _wheelDelta += e.Delta;
        while (Math.Abs(_wheelDelta) >= Mouse.MouseWheelDeltaForOneLine)
        {
            int step = _wheelDelta > 0 ? -1 : 1;
            Select(_selected + step, animate: true);
            _wheelDelta += step * Mouse.MouseWheelDeltaForOneLine;
        }
        e.Handled = true;
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WallpaperItem item })
            Apply(item, allMonitors: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e) => HidePicker();

    private void OnDeactivated(object? sender, EventArgs e) => HidePicker();
}
