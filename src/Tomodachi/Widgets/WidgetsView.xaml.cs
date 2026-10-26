using System.Windows;
using System.Windows.Controls;

namespace Tomodachi.Widgets;

public partial class WidgetsView : UserControl
{
    private WidgetsController? _controller;

    public WidgetsView() => InitializeComponent();

    internal void Initialize(WidgetsController controller)
    {
        _controller = controller;
        DataContext = controller.ViewModel;
    }

    private async void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (_controller is not null) await _controller.MediaAsync(m => m.TogglePlayPauseAsync());
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_controller is not null) await _controller.MediaAsync(m => m.NextAsync());
    }

    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (_controller is not null) await _controller.MediaAsync(m => m.PreviousAsync());
    }
}
