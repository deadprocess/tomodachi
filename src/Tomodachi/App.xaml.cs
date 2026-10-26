using System.Diagnostics;
using System.Windows;
using Tomodachi.Core;
using Tomodachi.Input;
using Tomodachi.Interop;
using Tomodachi.Launcher;
using Tomodachi.Localization;
using Tomodachi.Notch;
using Tomodachi.Safety;
using Tomodachi.Shell;
using Tomodachi.Wallpaper;
using Tomodachi.Widgets;

namespace Tomodachi;

public partial class App : Application
{
    private readonly StartupOptions _options;
    private readonly SingleInstance _instance;
    private WatchdogSupervisor? _watchdog;
    private EmergencyHotkey? _emergencyHotkey;
    private WinKeyHook? _winKeyHook;
    private GlobalHotkey? _tapHotkey;
    private TaskbarHider? _taskbarHider;
    private FullscreenWatcher? _fullscreenWatcher;
    private NotchWindow? _notch;
    private AppIndex? _appIndex;
    private SensorService? _sensors;
    private WallpaperPicker? _wallpaper;
    private TomodachiConfig _config = new();

    internal App(StartupOptions options, SingleInstance instance)
    {
        _options = options;
        _instance = instance;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = _config = ConfigStore.Load();
        Log.MinLevel = config.LogLevel;
        Loc.Apply(config.Language); // vor jedem Fenster, die Texte werden beim Laden aufgelöst
        Log.Info($"Config geladen (LogLevel {config.LogLevel}, Sprache {Loc.Culture.Name})");

        // Notausstiege zuerst, vor jeder Änderung am System
        _emergencyHotkey = new EmergencyHotkey(RequestShutdown);
        _instance.Listen(ShellCommands.Quit, () => Dispatcher.BeginInvoke(RequestShutdown));
        _instance.Listen(ShellCommands.ToggleWidgets, () => Dispatcher.BeginInvoke(() => _notch?.Toggle(NotchTab.Widgets)));
        _instance.Listen(ShellCommands.ToggleWindows, () => Dispatcher.BeginInvoke(() => _notch?.Toggle(NotchTab.Windows)));
        _instance.Listen(ShellCommands.TogglePower, () => Dispatcher.BeginInvoke(() => _notch?.Toggle(NotchTab.Power)));
        _instance.Listen(ShellCommands.ToggleSettings, () => Dispatcher.BeginInvoke(() => _notch?.Toggle(NotchTab.Settings)));

        if (!_options.Safe)
        {
            _watchdog = new WatchdogSupervisor();
            _watchdog.Start();

            if (config.HideTaskbar) _taskbarHider = new TaskbarHider();

            if (config.InterceptWinKey)
            {
                // Win-Tipp → F24 injizieren → WM_HOTKEY. Nur wer einen Hotkey empfängt, darf sich ohne Tricks in den
                // Vordergrund holen. Alt-Injektion und SwitchToThisWindow öffneten nachweislich das Startmenü.
                _tapHotkey = new GlobalHotkey("shell-tap", 0, Native.VK_F24, ToggleShell);
                Action onTap = config.DiagnoseWinTapNoAction ? () => Log.Info("Diagnose: Win-Tipp erkannt, keine Aktion")
                    : _tapHotkey.IsRegistered ? SendTapHotkey
                    : () => Dispatcher.BeginInvoke(ToggleShell);
                _winKeyHook = new WinKeyHook(onTap, action => Dispatcher.BeginInvoke(() => OnShellAction(action)));
                Log.Info($"Win-Taste wird abgefangen{(config.DiagnoseWinTapNoAction ? " (Diagnose: keine Aktion)" : "")}");
            }
        }

        Monitors.NotchSpec = config.NotchMonitor;

        _appIndex = new AppIndex();
        _appIndex.RefreshIfStale();

        _sensors = new SensorService();

        var widgets = new WidgetsController(_sensors, id =>
            _appIndex?.Apps.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name);

        _notch = new NotchWindow();
        _notch.Initialize(_appIndex, LaunchHistory.Load(), widgets, config);
        _notch.Settings.SettingChanged += OnSettingChanged;
        _notch.Settings.RestartRequested += RequestRestart;
        _notch.Settings.QuitRequested += RequestShutdown;
        // Ohne eigenen Fokus (Widgets, Fenster) sieht der Hook Esc und muss es für uns abfangen
        _notch.NotchStateChanged += () => WinKeyHook.PanelOpen = _notch.IsOpen && !_notch.Activatable;
        _notch.AvatarClicked += () => _notch.Toggle(NotchTab.Widgets);
        // Erst nach dem Zuklappen, sonst steht die Notch noch offen auf dem Sperrbildschirm bzw. beim Aufwachen
        _notch.PowerRequested += async action =>
        {
            await Task.Delay(300);
            PowerActions.Run(action);
        };
        _notch.Show();

        _wallpaper = CreateWallpaperPicker();
        _instance.Listen(ShellCommands.ToggleWallpaper, () => Dispatcher.BeginInvoke(() => _wallpaper?.Toggle()));

        _fullscreenWatcher = new FullscreenWatcher { HideOnMaximized = config.HideOnMaximized };
        _fullscreenWatcher.Changed += _notch.SetSuppressed;
    }

    private WallpaperPicker CreateWallpaperPicker()
    {
        var picker = new WallpaperPicker(_config.WallpaperFolder);
        picker.Opening += () => _notch?.CloseNotch(restoreFocus: false);
        return picker;
    }

    /// <summary>Aus dem Einstellungs-Reiter: speichern und, wo es live geht, sofort anwenden.</summary>
    private void OnSettingChanged(string setting)
    {
        Log.Info($"Einstellung geändert: {setting}");
        ConfigStore.Save(_config);
        switch (setting)
        {
            case nameof(TomodachiConfig.NotchMonitor):
                Monitors.NotchSpec = _config.NotchMonitor;
                Monitors.Invalidate();
                _notch?.MoveToNotchMonitor();
                break;
            case nameof(TomodachiConfig.HideOnMaximized):
                if (_fullscreenWatcher is not null) _fullscreenWatcher.HideOnMaximized = _config.HideOnMaximized;
                break;
            case nameof(TomodachiConfig.WallpaperFolder):
                _wallpaper?.Dispose(); // gibt Win+Strg+W frei
                _wallpaper = CreateWallpaperPicker();
                break;
            // Win-Taste, Taskleiste und Sprache: erst nach Neustart, der Einstellungs-Reiter weist darauf hin
        }
    }

    /// <summary>
    /// Neue Instanz mit <c>--restart</c> starten, dann sauber beenden. Die neue wartet, bis diese den Mutex freigibt.
    /// Als Kind dieses Prozesses erbt sie die Adminrechte.
    /// </summary>
    private void RequestRestart()
    {
        Log.Info("Neustart angefordert");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        info.ArgumentList.Add(StartupOptions.RestartArgument);
        Process.Start(info)?.Dispose();
        Shutdown();
    }

    /// <summary>Läuft auf dem Hook-Thread (außerhalb des Hook-Aufrufs). Die injizierte F24 lässt der eigene Hook durch.</summary>
    private static void SendTapHotkey()
    {
        Native.SendKey(Native.VK_F24, up: false);
        Native.SendKey(Native.VK_F24, up: true);
    }

    /// <summary>Win-Tipp / Ctrl+Esc: offene Notch zuklappen (egal welcher Reiter), sonst im Apps-Reiter öffnen.</summary>
    private void ToggleShell()
    {
        Log.Debug("Win-Tipp");
        if (_notch is null) return;
        if (_notch.IsOpen)
        {
            _notch.CloseNotch(restoreFocus: true);
            return;
        }
        _appIndex?.RefreshIfStale();
        _notch.Toggle(NotchTab.Apps);
    }

    /// <summary>Win+W schaltet den Widgets-Reiter, Esc (ohne eigenen Fokus vom Hook gemeldet) klappt zu.</summary>
    private void OnShellAction(int action)
    {
        switch (action)
        {
            case ShellActions.ToggleWidgets:
                _notch?.Toggle(NotchTab.Widgets);
                break;
            case ShellActions.ClosePanel:
                _notch?.CloseNotch(restoreFocus: true);
                break;
        }
    }

    private void RequestShutdown()
    {
        Log.Info("Shutdown angefordert");
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _taskbarHider?.Dispose();
        SystemRestore.RestoreAll("session ending");
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Erst nicht mehr verstecken, dann wiederherstellen. Sonst blendet der Hider die Taskleiste sofort wieder aus.
        _taskbarHider?.Dispose();
        SystemRestore.RestoreAll("exit");
        _winKeyHook?.Dispose();
        _tapHotkey?.Dispose();
        _fullscreenWatcher?.Dispose();
        _wallpaper?.Dispose();
        _notch?.Close();
        _appIndex?.Dispose();
        _sensors?.Dispose();
        _emergencyHotkey?.Dispose();
        _watchdog?.Dispose();
        base.OnExit(e);
    }
}
