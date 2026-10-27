using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Tomodachi.Localization;

namespace Tomodachi.Widgets;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class VolumeItem(string name, string usage, double fraction)
{
    public string Name { get; } = name;
    public string Usage { get; } = usage;
    public double Fraction { get; } = fraction;
}

public sealed class DiskItem(string name, string detail)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
}

public sealed class DayItem(string text, bool inMonth, bool isToday, bool isWeekend)
{
    public string Text { get; } = text;
    public bool InMonth { get; } = inMonth;
    public bool IsToday { get; } = isToday;
    public bool IsWeekend { get; } = isWeekend;
}

public sealed class WindowItem(nint handle) : Observable
{
    private string _title = "";
    private ImageSource? _icon;
    private bool _minimized;

    public nint Handle { get; } = handle;
    public string Title { get => _title; set => Set(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool Minimized { get => _minimized; set => Set(ref _minimized, value); }
}

/// <summary>Alles, was das Widget-Center anzeigt. Wird vom Panel im Sekundentakt befüllt, nur solange es offen ist.</summary>
public sealed class WidgetViewModel : Observable
{
    private const int HistoryLength = 60;

    /// <summary>Sprache der Oberfläche: Wochentage, Monatsnamen und Datumsformat (Zahlen bleiben in der Windows-Region).</summary>
    private static CultureInfo Culture => Loc.Culture;

    private readonly History _cpu = new(HistoryLength), _gpu = new(HistoryLength), _ram = new(HistoryLength);
    private readonly History _down = new(HistoryLength), _up = new(HistoryLength);
    private DateOnly _calendarDate;

    private string _time = "", _date = "", _calendarTitle = "";
    private IReadOnlyList<DayItem> _days = [];
    private string _cpuName = "CPU", _cpuValue = "–", _cpuDetail = "";
    private string _gpuName = "GPU", _gpuValue = "–", _gpuDetail = "";
    private string _ramValue = "–", _ramDetail = "";
    private double[] _cpuHistory = [], _gpuHistory = [], _ramHistory = [], _downHistory = [], _upHistory = [];
    private string _netTitle = Loc.Get("Widgets_Offline"), _netDown = "", _netUp = "";
    private double _netScale = 1;
    private bool _hasMedia;
    private bool _hasNoMedia = true;
    private string _mediaTitle = "", _mediaArtist = "", _mediaSource = "", _mediaTime = "";
    private ImageSource? _mediaCover;
    private bool _mediaPlaying;
    private double _mediaProgress;

    public string Time { get => _time; private set => Set(ref _time, value); }
    public string Date { get => _date; private set => Set(ref _date, value); }
    public string CalendarTitle { get => _calendarTitle; private set => Set(ref _calendarTitle, value); }
    public IReadOnlyList<DayItem> Days { get => _days; private set => Set(ref _days, value); }

    /// <summary>
    /// „Mo Di Mi …“ bzw. „Mo Tu We …“, Woche ab Montag wie <see cref="WidgetMath.MonthGrid"/>. Zwei Buchstaben aus den
    /// abgekürzten Namen, denn die kürzesten sind auf Englisch nur einer („M T W T“).
    /// </summary>
    public IReadOnlyList<string> WeekdayNames { get; } = Enumerable.Range(1, 7)
        .Select(i => Culture.DateTimeFormat.AbbreviatedDayNames[i % 7])
        .Select(name => name.Length > 2 ? name[..2] : name)
        .ToList();

    public string CpuName { get => _cpuName; private set => Set(ref _cpuName, value); }
    public string CpuValue { get => _cpuValue; private set => Set(ref _cpuValue, value); }
    public string CpuDetail { get => _cpuDetail; private set => Set(ref _cpuDetail, value); }
    public double[] CpuHistory { get => _cpuHistory; private set => Set(ref _cpuHistory, value); }
    public string GpuName { get => _gpuName; private set => Set(ref _gpuName, value); }
    public string GpuValue { get => _gpuValue; private set => Set(ref _gpuValue, value); }
    public string GpuDetail { get => _gpuDetail; private set => Set(ref _gpuDetail, value); }
    public double[] GpuHistory { get => _gpuHistory; private set => Set(ref _gpuHistory, value); }
    public string RamValue { get => _ramValue; private set => Set(ref _ramValue, value); }
    public string RamDetail { get => _ramDetail; private set => Set(ref _ramDetail, value); }
    public double[] RamHistory { get => _ramHistory; private set => Set(ref _ramHistory, value); }

    public ObservableCollection<VolumeItem> Volumes { get; } = [];
    public ObservableCollection<DiskItem> Disks { get; } = [];

    public string NetTitle { get => _netTitle; private set => Set(ref _netTitle, value); }
    public string NetDown { get => _netDown; private set => Set(ref _netDown, value); }
    public string NetUp { get => _netUp; private set => Set(ref _netUp, value); }
    public double[] DownHistory { get => _downHistory; private set => Set(ref _downHistory, value); }
    public double[] UpHistory { get => _upHistory; private set => Set(ref _upHistory, value); }
    public double NetScale { get => _netScale; private set => Set(ref _netScale, value); }

    public bool HasMedia
    {
        get => _hasMedia;
        private set
        {
            Set(ref _hasMedia, value);
            HasNoMedia = !value;
        }
    }

    public bool HasNoMedia { get => _hasNoMedia; private set => Set(ref _hasNoMedia, value); }
    public string MediaTitle { get => _mediaTitle; private set => Set(ref _mediaTitle, value); }
    public string MediaArtist { get => _mediaArtist; private set => Set(ref _mediaArtist, value); }
    public string MediaSource { get => _mediaSource; private set => Set(ref _mediaSource, value); }
    public string MediaTime { get => _mediaTime; private set => Set(ref _mediaTime, value); }
    public ImageSource? MediaCover { get => _mediaCover; private set => Set(ref _mediaCover, value); }
    public bool MediaPlaying { get => _mediaPlaying; private set => Set(ref _mediaPlaying, value); }
    public double MediaProgress { get => _mediaProgress; private set => Set(ref _mediaProgress, value); }

    public ObservableCollection<WindowItem> Windows { get; } = [];

    public QuickSettingsViewModel Quick { get; } = new();

    internal void UpdateClock(DateTime now)
    {
        Time = now.ToString("HH:mm", Culture);
        Date = now.ToString(Loc.Get("Widgets_DateFormat"), Culture);

        var today = DateOnly.FromDateTime(now);
        if (today == _calendarDate) return;
        _calendarDate = today;
        CalendarTitle = now.ToString("MMMM yyyy", Culture);
        Days = WidgetMath.MonthGrid(today)
            .Select(d => new DayItem(d.Day.ToString(Culture), d.InMonth, d.IsToday, d.IsWeekend))
            .ToList();
    }

    internal void UpdateSystem(SystemSnapshot? s)
    {
        if (s is null) return;

        if (s.CpuName is not null) CpuName = Shorten(s.CpuName);
        CpuValue = Percent(s.CpuLoad);
        CpuDetail = s.CpuTemperature is { } cpuTemp ? $"{cpuTemp:0} °C" : "";
        _cpu.Add(s.CpuLoad ?? 0);
        CpuHistory = _cpu.ToArray();

        if (s.GpuName is not null) GpuName = Shorten(s.GpuName);
        GpuValue = Percent(s.GpuLoad);
        var gpuParts = new List<string>();
        if (s.GpuTemperature is { } gpuTemp) gpuParts.Add($"{gpuTemp:0} °C");
        if (s.VramUsedGb is { } vramUsed && s.VramTotalGb is { } vramTotal) gpuParts.Add($"VRAM {vramUsed:0.0} / {vramTotal:0} GB");
        GpuDetail = string.Join(" · ", gpuParts);
        _gpu.Add(s.GpuLoad ?? 0);
        GpuHistory = _gpu.ToArray();

        if (s.RamUsedGb is { } used && s.RamTotalGb is { } total && total > 0)
        {
            RamValue = Percent(used / total * 100);
            RamDetail = $"{used:0.0} / {total:0} GB";
            _ram.Add(used / total * 100);
            RamHistory = _ram.ToArray();
        }

        Disks.Clear();
        foreach (var disk in s.Disks)
        {
            var parts = new List<string>();
            if (disk.Temperature is { } t) parts.Add($"{t:0} °C");
            if (disk.ReadBytesPerSec is { } r) parts.Add($"↓ {WidgetMath.Rate(r)}");
            if (disk.WriteBytesPerSec is { } w) parts.Add($"↑ {WidgetMath.Rate(w)}");
            Disks.Add(new DiskItem(Shorten(disk.Name), string.Join("   ", parts)));
        }
    }

    internal void UpdateVolumes(IEnumerable<(string Name, string Label, long Total, long Free)> volumes)
    {
        Volumes.Clear();
        foreach (var v in volumes)
        {
            var used = v.Total - v.Free;
            var name = string.IsNullOrWhiteSpace(v.Label) ? v.Name : $"{v.Name}  {v.Label}";
            Volumes.Add(new VolumeItem(name, $"{WidgetMath.Size(used)} / {WidgetMath.Size(v.Total)}", v.Total > 0 ? (double)used / v.Total : 0));
        }
    }

    internal void UpdateNetwork(NetworkSnapshot? n)
    {
        if (n is null)
        {
            NetTitle = Loc.Get("Widgets_Offline");
            NetDown = NetUp = "";
            return;
        }

        var parts = new List<string> { n.Kind };
        var speed = WidgetMath.LinkSpeed(n.SpeedBitsPerSec);
        if (speed.Length > 0) parts.Add(speed);
        if (n.Address is not null) parts.Add(n.Address);
        NetTitle = string.Join(" · ", parts);

        NetDown = WidgetMath.Rate(n.DownBytesPerSec);
        NetUp = WidgetMath.Rate(n.UpBytesPerSec);
        _down.Add(n.DownBytesPerSec);
        _up.Add(n.UpBytesPerSec);
        DownHistory = _down.ToArray();
        UpHistory = _up.ToArray();
        NetScale = WidgetMath.AutoScale(DownHistory.Concat(UpHistory));
    }

    internal void UpdateMedia(MediaSnapshot? m)
    {
        HasMedia = m is not null && !string.IsNullOrWhiteSpace(m.Title);
        if (!HasMedia || m is null) return;

        MediaTitle = m.Title;
        MediaArtist = m.Artist;
        MediaSource = m.Source;
        MediaCover = m.Cover;
        MediaPlaying = m.IsPlaying;
        MediaProgress = m.Duration > TimeSpan.Zero ? m.Position / m.Duration : 0;
        MediaTime = m.Duration > TimeSpan.Zero ? $"{WidgetMath.Duration(m.Position)} / {WidgetMath.Duration(m.Duration)}" : "";
    }

    /// <summary>Abgleich statt neu aufbauen: Sonst springt die Liste bei jeder Aktualisierung und Hover geht verloren.</summary>
    internal void UpdateWindows(IReadOnlyList<WindowInfo> windows, Func<nint, ImageSource?> icon)
    {
        var current = windows.Select(w => w.Handle).ToHashSet();
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (!current.Contains(Windows[i].Handle)) Windows.RemoveAt(i);
        }

        for (var i = 0; i < windows.Count; i++)
        {
            var info = windows[i];
            var item = Windows.FirstOrDefault(w => w.Handle == info.Handle);
            if (item is null)
            {
                item = new WindowItem(info.Handle) { Icon = icon(info.Handle) };
                Windows.Insert(Math.Min(i, Windows.Count), item);
            }
            item.Title = info.Title;
            item.Minimized = info.Minimized;
        }
    }

    private static string Percent(double? value) => value is { } v ? $"{v:0} %" : "–";

    /// <summary>„AMD Ryzen 9 5950X 16-Core Processor“ → „AMD Ryzen 9 5950X“, „Samsung SSD 970 EVO Plus 1TB“ bleibt.</summary>
    private static string Shorten(string name) =>
        name.Replace(" Processor", "").Replace(" 16-Core", "").Replace(" 8-Core", "").Replace(" 12-Core", "").Replace(" 6-Core", "").Trim();
}
