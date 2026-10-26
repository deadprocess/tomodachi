using System.IO;
using System.Windows.Threading;

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
    private readonly DispatcherTimer _timer;
    private int _tick;

    /// <param name="appName">Übersetzt App-Kennungen der Mediensteuerung (z. B. Firefox' „308046B0AF4A39CB“) in Namen.</param>
    public WidgetsController(SensorService sensors, Func<string, string?> appName)
    {
        _sensors = sensors;
        _media = new MediaService(appName);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(),
            Dispatcher.CurrentDispatcher);
    }

    public WidgetViewModel ViewModel { get; } = new();

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _tick = 0;
        Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public async Task MediaAsync(Func<MediaService, Task> command)
    {
        await command(_media);
        await Task.Delay(300); // die App braucht einen Moment, bis der neue Zustand gemeldet wird
        ViewModel.UpdateMedia(await _media.PollAsync());
    }

    /// <summary>Uhr, Netzwerk und Sensoren jede Sekunde, Media und Fenster alle 2 s, Laufwerke alle 5 s.</summary>
    private async void Refresh()
    {
        var tick = _tick++;
        ViewModel.UpdateClock(DateTime.Now);
        ViewModel.UpdateNetwork(_network.Poll());

        if (tick % 2 == 0)
        {
            ViewModel.UpdateWindows(_windowList.Snapshot(), _windowList.Icon);
            ViewModel.UpdateMedia(await _media.PollAsync());
        }
        if (tick % 5 == 0) ViewModel.UpdateVolumes(ReadVolumes());

        ViewModel.UpdateSystem(await _sensors.PollAsync());
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
