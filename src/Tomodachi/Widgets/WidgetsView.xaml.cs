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

    // ===== Schnelleinstellungen =====

    private void OnMute(object sender, RoutedEventArgs e) => _controller?.ToggleMute();

    private void OnDevices(object sender, RoutedEventArgs e) => _controller?.ToggleDevices();

    private void OnDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) _controller?.SetDefaultDevice(id);
    }

    private async void OnWifi(object sender, RoutedEventArgs e)
    {
        if (_controller is not null) await _controller.ToggleRadioAsync(wifi: true);
    }

    private async void OnBluetooth(object sender, RoutedEventArgs e)
    {
        if (_controller is not null) await _controller.ToggleRadioAsync(wifi: false);
    }

    private async void OnNetworks(object sender, RoutedEventArgs e)
    {
        if (_controller is not null) await _controller.ToggleNetworksAsync();
    }

    private async void OnNetworkClicked(object sender, RoutedEventArgs e)
    {
        if (_controller is not null && sender is FrameworkElement { Tag: string ssid }) await _controller.ConnectAsync(ssid);
    }
}
