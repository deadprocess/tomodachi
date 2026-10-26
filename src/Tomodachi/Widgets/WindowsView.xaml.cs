using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tomodachi.Core;

namespace Tomodachi.Widgets;

public partial class WindowsView : UserControl
{
    private TomodachiConfig? _config;
    private nint _menuWindow;

    public WindowsView() => InitializeComponent();

    /// <summary>Ein Fenster wurde angeklickt und nach vorn geholt. Die Notch klappt daraufhin zu.</summary>
    public event Action? WindowActivated;

    /// <param name="config">Gelesen beim Öffnen des Menüs, damit „Task beenden“ sofort auf die Einstellung reagiert.</param>
    internal void Initialize(WidgetsController controller, TomodachiConfig config)
    {
        DataContext = controller.ViewModel;
        _config = config;
    }

    private void OnWindowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: nint hwnd }) return;
        WindowListService.Activate(hwnd);
        WindowActivated?.Invoke();
    }

    private void OnWindowClose(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: nint hwnd }) WindowListService.Close(hwnd);
        e.Handled = true;
    }

    /// <summary>Rechtsklick: Nach vorn holen, Schließen und (falls eingeschaltet) Task beenden.</summary>
    private void OnWindowRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: nint hwnd } row) return;
        e.Handled = true;
        _menuWindow = hwnd;

        var menu = (ContextMenu)Resources["WindowMenu"];
        var endTask = (MenuItem)menu.Items[2];
        endTask.Visibility = _config?.EnableEndTask == true ? Visibility.Visible : Visibility.Collapsed;
        endTask.IsEnabled = WindowListService.CanEndTask(hwnd); // ausgegraut beim Datei-Explorer
        menu.PlacementTarget = row;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void OnMenuActivate(object sender, RoutedEventArgs e)
    {
        WindowListService.Activate(_menuWindow);
        WindowActivated?.Invoke();
    }

    private void OnMenuClose(object sender, RoutedEventArgs e) => WindowListService.Close(_menuWindow);

    private void OnMenuEndTask(object sender, RoutedEventArgs e) => WindowListService.EndTask(_menuWindow);
}
