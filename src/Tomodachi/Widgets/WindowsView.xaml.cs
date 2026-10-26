using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tomodachi.Widgets;

public partial class WindowsView : UserControl
{
    public WindowsView() => InitializeComponent();

    /// <summary>Ein Fenster wurde angeklickt und nach vorn geholt. Die Notch klappt daraufhin zu.</summary>
    public event Action? WindowActivated;

    internal void Initialize(WidgetsController controller) => DataContext = controller.ViewModel;

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
}
