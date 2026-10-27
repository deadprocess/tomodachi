using Tomodachi.Widgets;

namespace Tomodachi.Tests;

public class QuickSettingsTests
{
    private readonly QuickSettingsViewModel _vm = new();

    [Fact]
    public void VolumeFromWindows_IsNotSentBack()
    {
        var requested = new List<double>();
        _vm.VolumeRequested += requested.Add;

        _vm.UpdateAudio(new AudioState("id", "Lautsprecher", 42, false));

        Assert.Empty(requested);
        Assert.Equal("42 %", _vm.VolumeText);
    }

    [Fact]
    public void Slider_RequestsVolume()
    {
        var requested = new List<double>();
        _vm.VolumeRequested += requested.Add;

        _vm.Volume = 70;

        Assert.Equal([70d], requested);
    }

    [Fact]
    public void NoAudioDevice_HidesAudioRows()
    {
        _vm.UpdateAudio(null);
        Assert.False(_vm.HasAudio);
    }

    [Fact]
    public void NoRadioHardware_HidesTiles()
    {
        _vm.UpdateRadios(new RadioState(null, null));
        Assert.False(_vm.HasRadios);

        _vm.UpdateRadios(new RadioState(null, false));
        Assert.True(_vm.HasRadios);
        Assert.False(_vm.HasWifi);
        Assert.True(_vm.HasBluetooth);
    }

    [Fact]
    public void Networks_ShowsAtMostFive()
    {
        var networks = Enumerable.Range(1, 8).Select(i => new WifiNetwork($"Netz {i}", 3, true, i == 1)).ToList();
        _vm.UpdateNetworks(networks);
        Assert.Equal(5, _vm.Networks.Count);
        Assert.False(_vm.NoNetworks);

        _vm.UpdateNetworks([]);
        Assert.True(_vm.NoNetworks);
    }

    [Fact]
    public void Devices_MarkDefault()
    {
        _vm.UpdateDevices([new AudioDevice("a", "Headset"), new AudioDevice("b", "Monitor")], "b");
        Assert.Equal(["b"], _vm.Devices.Where(d => d.IsDefault).Select(d => d.Id));
    }
}
