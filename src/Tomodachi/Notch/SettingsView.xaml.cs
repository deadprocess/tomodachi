using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Tomodachi.Core;
using Tomodachi.Interop;
using Tomodachi.Localization;

namespace Tomodachi.Notch;

/// <summary>
/// Einstellungs-Reiter. Ändert die gemeinsame <see cref="TomodachiConfig"/> und meldet per <see cref="SettingChanged"/>,
/// was sich geändert hat. App speichert und wendet an. Win-Taste und Taskleiste wirken erst nach einem Neustart.
/// </summary>
public partial class SettingsView : UserControl
{
    private TomodachiConfig _config = new();
    private (bool InterceptWinKey, bool HideTaskbar, string Language) _atStartup;
    private bool _syncing;

    public SettingsView() => InitializeComponent();

    /// <summary>Name der geänderten Config-Eigenschaft, z. B. <c>NotchMonitor</c>.</summary>
    internal event Action<string>? SettingChanged;

    internal event Action? AvatarChanged;
    internal event Action? RestartRequested;
    internal event Action? QuitRequested;

    /// <summary>Ein Datei-Dialog wurde geschlossen. Der Fokuswechsel danach soll die Notch nicht zuklappen.</summary>
    internal event Action? DialogClosed;

    internal void Initialize(TomodachiConfig config)
    {
        _config = config;
        _atStartup = (config.InterceptWinKey, config.HideTaskbar, config.Language);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        Version.Text = $"tomodachi {version} · {Loc.Get(Elevation.IsElevated ? "Settings_Elevated" : "Settings_NotElevated")}";
    }

    /// <summary>Beim Einblenden: alles neu vom aktuellen Stand lesen (Monitore können sich geändert haben).</summary>
    public void Refresh()
    {
        _syncing = true;
        HideOnMaximized.IsChecked = _config.HideOnMaximized;
        InterceptWinKey.IsChecked = _config.InterceptWinKey;
        HideTaskbar.IsChecked = _config.HideTaskbar;
        LanguageAuto.IsChecked = _config.Language is not ("de" or "en");
        LanguageDe.IsChecked = _config.Language == "de";
        LanguageEn.IsChecked = _config.Language == "en";
        _syncing = false;

        RefreshAvatar();
        RefreshMonitors();
        RefreshWallpaperFolder();
        RefreshRestartHint();
        _ = RefreshAutostartAsync();
    }

    private void RefreshAvatar()
    {
        AvatarPreview.Source = AvatarStore.Load();
        AvatarHint.Text = Loc.Get(AvatarStore.HasCustom ? "Settings_AvatarCustom" : "Settings_AvatarDefault");
        ResetAvatar.Visibility = AvatarStore.HasCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMonitors()
    {
        var current = _config.NotchMonitor;
        MonitorChoices.Items.Clear();
        foreach (var (spec, width, height, isPrimary) in Monitors.List())
        {
            var selected = string.IsNullOrWhiteSpace(current) ? isPrimary : spec.Contains(current, StringComparison.OrdinalIgnoreCase);
            var choice = new RadioButton
            {
                Style = (Style)FindResource("TabButton"),
                GroupName = "NotchMonitor",
                Content = $"{width}×{height}",
                ToolTip = isPrimary ? $"{spec} · {Loc.Get("Settings_PrimaryMonitor")}" : spec,
                IsChecked = selected,
                Tag = isPrimary ? "" : spec, // Hauptmonitor als „leer“: bleibt richtig, auch wenn der Monitor getauscht wird
            };
            choice.Checked += OnMonitorChosen;
            MonitorChoices.Items.Add(choice);
        }
    }

    private void RefreshWallpaperFolder() =>
        WallpaperFolder.Text = string.IsNullOrWhiteSpace(_config.WallpaperFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Wallpaper")
            : _config.WallpaperFolder;

    private void RefreshRestartHint()
    {
        var needed = (_config.InterceptWinKey, _config.HideTaskbar, _config.Language) != _atStartup;
        RestartButton.Content = Loc.Get(needed ? "Settings_RestartNow" : "Settings_Restart");
        RestartButton.Foreground = (System.Windows.Media.Brush)FindResource(needed ? "AccentBrush" : "TextBrush");
    }

    private async Task RefreshAutostartAsync()
    {
        Autostart.IsEnabled = false;
        var installed = await Task.Run(Core.Autostart.IsInstalledForThisExe);
        Autostart.IsChecked = installed;
        Autostart.IsEnabled = true;
    }

    private void OnMonitorChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string spec } || spec == _config.NotchMonitor) return;
        _config.NotchMonitor = spec;
        SettingChanged?.Invoke(nameof(TomodachiConfig.NotchMonitor));
    }

    private void OnLanguageChosen(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string language } || language == _config.Language) return;
        _config.Language = language;
        SettingChanged?.Invoke(nameof(TomodachiConfig.Language));
        RefreshRestartHint();
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not CheckBox box) return;
        var on = box.IsChecked == true;
        switch (box.Name)
        {
            case nameof(HideOnMaximized): _config.HideOnMaximized = on; break;
            case nameof(InterceptWinKey): _config.InterceptWinKey = on; break;
            case nameof(HideTaskbar): _config.HideTaskbar = on; break;
            default: return;
        }
        SettingChanged?.Invoke(box.Name);
        RefreshRestartHint();
    }

    /// <summary>
    /// Über einen eigenen Prozess (<c>--install</c>/<c>--uninstall</c>): Der holt sich bei Bedarf per UAC Adminrechte,
    /// legt die Aufgabe an und bestätigt mit einer Meldung.
    /// </summary>
    private async void OnAutostart(object sender, RoutedEventArgs e)
    {
        var install = Autostart.IsChecked == true;
        Autostart.IsEnabled = false;
        AutostartHint.Text = Loc.Get(install ? "Settings_AutostartAdding" : "Settings_AutostartRemoving");
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            info.ArgumentList.Add(install ? "--install" : "--uninstall");
            using var process = Process.Start(info);
            if (process is not null) await process.WaitForExitAsync();
        }
        catch (Win32Exception ex)
        {
            Log.Warn("Autostart konnte nicht geändert werden", ex);
        }
        AutostartHint.Text = Loc.Get("Settings_AutostartHint");
        await RefreshAutostartAsync();
    }

    private void OnChooseAvatar(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("Settings_AvatarDialog"),
            Filter = Loc.Get("Settings_ImageFilter"),
        };
        var chosen = dialog.ShowDialog(Window.GetWindow(this)) == true;
        DialogClosed?.Invoke();
        if (!chosen) return;

        try
        {
            AvatarStore.SetCustom(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Warn("Profilbild nicht lesbar", ex);
            AvatarHint.Text = Loc.Get("Settings_AvatarUnreadable");
            return;
        }
        RefreshAvatar();
        AvatarChanged?.Invoke();
    }

    private void OnResetAvatar(object sender, RoutedEventArgs e)
    {
        AvatarStore.ResetToDefault();
        RefreshAvatar();
        AvatarChanged?.Invoke();
    }

    private void OnChooseWallpaperFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.Get("Settings_WallpaperDialog"), InitialDirectory = WallpaperFolder.Text };
        var chosen = dialog.ShowDialog(Window.GetWindow(this)) == true;
        DialogClosed?.Invoke();
        if (!chosen || dialog.FolderName == _config.WallpaperFolder) return;

        _config.WallpaperFolder = dialog.FolderName;
        RefreshWallpaperFolder();
        SettingChanged?.Invoke(nameof(TomodachiConfig.WallpaperFolder));
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.LogDir);
        // Über explorer.exe: öffnet das Fenster in der Explorer-Instanz des Users, auch wenn tomodachi elevated läuft
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.LogDir}\"") { UseShellExecute = false })?.Dispose();
    }

    private void OnRestart(object sender, RoutedEventArgs e) => RestartRequested?.Invoke();

    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
}
