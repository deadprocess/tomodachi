using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Tomodachi.Localization;
using Tomodachi.Shell;
using Tomodachi.Widgets;

namespace Tomodachi.Notch;

public sealed class PowerItem(PowerAction action, string icon, string label) : Observable
{
    private readonly string _label = label;
    private bool _isArmed;
    private string _caption = label;

    public PowerAction Action { get; } = action;
    public string Icon { get; } = icon;

    /// <summary>Scharf = einmal geklickt, wartet auf die Bestätigung.</summary>
    public bool IsArmed
    {
        get => _isArmed;
        set
        {
            Set(ref _isArmed, value);
            Caption = value ? Loc.Get("Power_Confirm") : _label;
        }
    }

    public string Caption
    {
        get => _caption;
        private set => Set(ref _caption, value);
    }
}

public partial class PowerView : UserControl
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly ConfirmGate _gate = new(ConfirmWindow);
    private readonly DispatcherTimer _disarm;
    private readonly PowerItem[] _items =
    [
        new(PowerAction.Lock, "", Loc.Get("Power_Lock")),
        new(PowerAction.Sleep, "", Loc.Get("Power_Sleep")),
        new(PowerAction.SignOut, "", Loc.Get("Power_SignOut")),
        new(PowerAction.Restart, "", Loc.Get("Power_Restart")),
        new(PowerAction.Shutdown, "", Loc.Get("Power_Shutdown")),
    ];

    public PowerView()
    {
        InitializeComponent();
        Actions.ItemsSource = _items;
        _disarm = new DispatcherTimer(ConfirmWindow, DispatcherPriority.Normal, (_, _) => Reset(), Dispatcher);
        _disarm.Stop();
    }

    /// <summary>Eine Aktion soll laufen. Die Notch klappt erst zu, dann führt App sie aus.</summary>
    internal event Action<PowerAction>? ActionRequested;

    /// <summary>Beim Einblenden: nichts scharf, Laufzeit aktualisieren.</summary>
    public void Reset()
    {
        _disarm.Stop();
        _gate.Disarm();
        foreach (var item in _items) item.IsArmed = false;
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        Uptime.Text = Loc.Format("Power_Uptime", WidgetMath.Uptime(uptime));
    }

    private void OnTileClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PowerItem item }) return;
        e.Handled = true;

        if (_gate.Press(item.Action, DateTime.UtcNow))
        {
            Reset();
            ActionRequested?.Invoke(item.Action);
            return;
        }

        foreach (var other in _items) other.IsArmed = other == item;
        _disarm.Stop();
        _disarm.Start();
    }
}
