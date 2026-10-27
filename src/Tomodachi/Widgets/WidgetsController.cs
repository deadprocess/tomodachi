using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Tomodachi.Core;

namespace Tomodachi.Widgets;

/// <summary>
/// Datenquellen und Takt für die Notch-Reiter Widgets und Fenster. Läuft nur zwischen <see cref="Start"/> und
/// <see cref="Stop"/>, also solange einer dieser Reiter sichtbar ist. Muss auf dem UI-Thread erzeugt werden.
/// </summary>
internal sealed class WidgetsController
{
    private readonly SensorService _sensors;
    private readonly NetworkService _network = new();
    private readonly MediaService _media;
    private readonly WindowListService _windowList = new();
    private readonly AudioService _audio = new();
    private readonly RadioService _radios = new();
    private readonly DispatcherTimer _timer;
    private int _tick;

    /// <param name="appName">Übersetzt App-Kennungen der Mediensteuerung (z. B. Firefox' „308046B0AF4A39CB“) in Namen.</param>
    public WidgetsController(SensorService sensors, Func<string, string?> appName)
    {
        _sensors = sensors;
        _media = new MediaService(appName);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(),
            Dispatcher.CurrentDispatcher);
        ViewModel.Quick.VolumeRequested += _audio.SetVolume;
    }

    public WidgetViewModel ViewModel { get; } = new();

    private QuickSettingsViewModel Quick => ViewModel.Quick;

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _tick = 0;
        Refresh();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Quick.ShowDevices = false;
        Quick.ShowNetworks = false;
    }

    public async Task MediaAsync(Func<MediaService, Task> command)
    {
        await command(_media);
        await Task.Delay(300); // die App braucht einen Moment, bis der neue Zustand gemeldet wird
        ViewModel.UpdateMedia(await _media.PollAsync());
    }

    public void ToggleMute()
    {
        _audio.SetMuted(!Quick.Muted);
        Quick.UpdateAudio(_audio.Poll());
    }

    public void ToggleDevices()
    {
        Quick.ShowDevices = !Quick.ShowDevices;
        if (Quick.ShowDevices) RefreshDevices();
    }

    public void SetDefaultDevice(string deviceId)
    {
        _audio.SetDefault(deviceId);
        Quick.UpdateAudio(_audio.Poll());
        RefreshDevices();
        Quick.ShowDevices = false;
    }

    public async Task ToggleRadioAsync(bool wifi)
    {
        await _radios.SetAsync(wifi, !(wifi ? Quick.WifiOn : Quick.BluetoothOn));
        Quick.UpdateRadios(await _radios.PollAsync());
    }

    public async Task ToggleNetworksAsync()
    {
        Quick.ShowNetworks = !Quick.ShowNetworks;
        if (Quick.ShowNetworks) Quick.UpdateNetworks(await _radios.ScanAsync());
    }

    /// <summary>Unbekanntes Netz mit Passwort: Die WLAN-Einstellungen von Windows übernehmen das Eintippen.</summary>
    public async Task ConnectAsync(string ssid)
    {
        if (await _radios.ConnectAsync(ssid))
        {
            Quick.UpdateNetworks(await _radios.ScanAsync());
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:network-wifi") { UseShellExecute = false })?.Dispose();
    }

    /// <summary>
    /// Uhr, Netzwerk, Sensoren und Lautstärke jede Sekunde, Media, Fenster und Funk alle 2 s, Laufwerke und
    /// Audiogeräte alle 5 s, WLAN-Liste (nur offen) alle 10 s.
    /// </summary>
    private async void Refresh()
    {
        var tick = _tick++;
        ViewModel.UpdateClock(DateTime.Now);
        ViewModel.UpdateNetwork(_network.Poll());
        Quick.UpdateAudio(_audio.Poll());

        if (tick % 2 == 0)
        {
            ViewModel.UpdateWindows(_windowList.Snapshot(), _windowList.Icon);
            ViewModel.UpdateMedia(await _media.PollAsync());
            Quick.UpdateRadios(await _radios.PollAsync());
        }
        if (tick % 5 == 0)
        {
            ViewModel.UpdateVolumes(ReadVolumes());
            if (Quick.ShowDevices) RefreshDevices();
        }
        if (tick % 10 == 0 && Quick.ShowNetworks) Quick.UpdateNetworks(await _radios.ScanAsync());

        ViewModel.UpdateSystem(await _sensors.PollAsync());
    }

    private void RefreshDevices()
    {
        var state = _audio.Poll();
        Quick.UpdateDevices(_audio.Devices(), state?.DeviceId ?? "");
    }

    private static IEnumerable<(string, string, long, long)> ReadVolumes()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            yield return (drive.Name.TrimEnd('\\'), drive.VolumeLabel, drive.TotalSize, drive.TotalFreeSpace);
        }
    }
}
