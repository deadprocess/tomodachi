using System.Collections.ObjectModel;

namespace Tomodachi.Widgets;

public sealed class AudioDeviceItem(string id, string name, bool isDefault)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool IsDefault { get; } = isDefault;
}

public sealed class WifiNetworkItem(string ssid, int bars, bool secured, bool connected)
{
    public string Ssid { get; } = ssid;
    public bool Secured { get; } = secured;
    public bool Connected { get; } = connected;

    /// <summary>Segoe-Fluent-Symbol für die Signalstärke (0–4 Balken).</summary>
    public string SignalIcon { get; } = bars switch
    {
        <= 1 => "",
        2 => "",
        3 => "",
        _ => "",
    };
}

/// <summary>
/// Karte „Schnelleinstellungen“ im Widgets-Reiter: WLAN, Bluetooth, Stumm, Lautstärke und Ausgabegerät.
/// WLAN und Bluetooth erscheinen nur, wenn die Hardware da ist.
/// </summary>
public sealed class QuickSettingsViewModel : Observable
{
    private double _volume;
    private bool _fromSystem;
    private bool _muted, _hasAudio, _showDevices;
    private string _deviceName = "";
    private bool _hasRadios, _hasWifi, _wifiOn, _hasBluetooth, _bluetoothOn, _showNetworks, _noNetworks;

    /// <summary>Der Regler wurde bewegt (nicht: Windows hat die Lautstärke gemeldet).</summary>
    internal event Action<double>? VolumeRequested;

    /// <summary>0–100. Zwei-Wege-Bindung an den Regler.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.5) return;
            Set(ref _volume, value);
            OnVolumeTextChanged();
            if (!_fromSystem) VolumeRequested?.Invoke(value);
        }
    }

    public string VolumeText => $"{_volume:0} %";
    public bool Muted { get => _muted; private set => Set(ref _muted, value); }
    public bool HasAudio { get => _hasAudio; private set => Set(ref _hasAudio, value); }
    public string DeviceName { get => _deviceName; private set => Set(ref _deviceName, value); }
    public ObservableCollection<AudioDeviceItem> Devices { get; } = [];
    public bool ShowDevices { get => _showDevices; set => Set(ref _showDevices, value); }

    /// <summary>WLAN oder Bluetooth vorhanden, sonst bleibt die Kachelreihe weg (wie auf dem Entwicklungs-PC).</summary>
    public bool HasRadios { get => _hasRadios; private set => Set(ref _hasRadios, value); }
    public bool HasWifi { get => _hasWifi; private set => Set(ref _hasWifi, value); }
    public bool WifiOn { get => _wifiOn; private set => Set(ref _wifiOn, value); }
    public bool HasBluetooth { get => _hasBluetooth; private set => Set(ref _hasBluetooth, value); }
    public bool BluetoothOn { get => _bluetoothOn; private set => Set(ref _bluetoothOn, value); }
    public ObservableCollection<WifiNetworkItem> Networks { get; } = [];
    public bool ShowNetworks { get => _showNetworks; set => Set(ref _showNetworks, value); }
    public bool NoNetworks { get => _noNetworks; private set => Set(ref _noNetworks, value); }

    internal void UpdateAudio(AudioState? state)
    {
        HasAudio = state is not null;
        if (state is null) return;
        _fromSystem = true;
        Volume = state.Volume;
        _fromSystem = false;
        Muted = state.Muted;
        DeviceName = state.DeviceName;
    }

    internal void UpdateDevices(IReadOnlyList<AudioDevice> devices, string defaultId)
    {
        Devices.Clear();
        foreach (var device in devices) Devices.Add(new AudioDeviceItem(device.Id, device.Name, device.Id == defaultId));
    }

    internal void UpdateRadios(RadioState state)
    {
        HasWifi = state.WifiOn is not null;
        WifiOn = state.WifiOn == true;
        HasBluetooth = state.BluetoothOn is not null;
        BluetoothOn = state.BluetoothOn == true;
        HasRadios = HasWifi || HasBluetooth;
    }

    internal void UpdateNetworks(IReadOnlyList<WifiNetwork> networks)
    {
        Networks.Clear();
        foreach (var n in networks.Take(5)) Networks.Add(new WifiNetworkItem(n.Ssid, n.Bars, n.Secured, n.Connected));
        NoNetworks = networks.Count == 0;
    }

    private void OnVolumeTextChanged()
    {
        // VolumeText hängt an Volume; Set() meldet nur Volume selbst
        var text = VolumeText;
        Set(ref _volumeTextCache, text, nameof(VolumeText));
    }

    private string _volumeTextCache = "";
}
