using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tomodachi.Launcher;

/// <summary>Paket 3.3: Suchfeld + Trefferliste. Komplett per Tastatur bedienbar, der Fokus bleibt immer im Suchfeld.</summary>
public partial class LauncherView : UserControl
{
    private const int MaxResults = 8;

    private readonly ObservableCollection<AppResult> _results = [];
    private AppIndex? _index;
    private LaunchHistory? _history;
    private AppEntry? _menuApp;

    public LauncherView()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
    }

    /// <summary>Gewählte App und ob sie mit Adminrechten starten soll.</summary>
    internal event Action<AppEntry, bool>? LaunchRequested;

    public event Action? CloseRequested;

    internal void Initialize(AppIndex index, LaunchHistory history)
    {
        _index = index;
        _history = history;
        index.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) UpdateResults();
        });
    }

    /// <summary>Beim Öffnen: leeres Suchfeld, zuletzt genutzte Apps.</summary>
    public void Reset()
    {
        Query.Text = "";
        UpdateResults();
    }

    /// <summary>
    /// Direkt nach dem Sichtbarschalten hat das Suchfeld noch kein Layout, und Focus() schlägt still fehl.
    /// Deshalb erst das Layout erzwingen und notfalls nach dem nächsten Layout-Durchlauf erneut versuchen.
    /// </summary>
    public void FocusQuery()
    {
        UpdateLayout();
        if (Keyboard.Focus(Query) == Query) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => Keyboard.Focus(Query));
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateResults();
    }

    private void UpdateResults()
    {
        if (_index is null || _history is null) return;

        var known = _results.ToDictionary(r => r.App.Id);
        var apps = LauncherSearch.Search(Query.Text, _index.Apps, _history, DateTime.UtcNow, MaxResults);

        _results.Clear();
        foreach (var app in apps)
        {
            var result = known.GetValueOrDefault(app.Id) ?? new AppResult(app);
            _results.Add(result);
            if (result.Icon is null) LoadIcon(result);
        }

        Results.SelectedIndex = _results.Count > 0 ? 0 : -1;
        NoResults.Visibility = _results.Count == 0 && Query.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LoadIcon(AppResult result)
    {
        if (_index is null) return;
        result.Icon = await _index.GetIconAsync(result.App);
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                var elevated = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                LaunchSelected(elevated);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_results.Count == 0) return;
        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, _results.Count - 1);
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void LaunchSelected(bool elevated)
    {
        if (Results.SelectedItem is AppResult result) LaunchRequested?.Invoke(result.App, elevated);
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (ResultAt(e) is { } result)
        {
            LaunchRequested?.Invoke(result.App, false);
            e.Handled = true;
        }
    }

    /// <summary>Rechtsklick: Treffer auswählen und das Menü (Starten, Als Administrator starten) an der Maus öffnen.</summary>
    private void OnResultRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (ResultAt(e) is not { } result) return;
        e.Handled = true;
        Results.SelectedItem = result;
        _menuApp = result.App;

        var menu = (ContextMenu)Resources["ResultMenu"];
        menu.PlacementTarget = Results;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void OnMenuLaunch(object sender, RoutedEventArgs e)
    {
        if (_menuApp is { } app) LaunchRequested?.Invoke(app, false);
    }

    /// <summary>
    /// Für jede App erlaubt. Viele Store-Apps (z. B. Windows Terminal) sind verpackte Desktop-Apps und laufen als Admin.
    /// URLs (steam://) starten normal, siehe <see cref="AppLauncher"/>. Lehnt Windows es ab (reine UWP-Apps),
    /// steht das im Log und die Notch bleibt offen.
    /// </summary>
    private void OnMenuLaunchElevated(object sender, RoutedEventArgs e)
    {
        if (_menuApp is { } app) LaunchRequested?.Invoke(app, true);
    }

    /// <summary>Das Menü hatte den Tastaturfokus. Danach wieder ins Suchfeld, damit man weitertippen kann.</summary>
    private void OnResultMenuClosed(object sender, RoutedEventArgs e)
    {
        _menuApp = null;
        if (IsVisible) FocusQuery();
    }

    private AppResult? ResultAt(MouseButtonEventArgs e) =>
        ItemsControl.ContainerFromElement(Results, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: AppResult result }
            ? result
            : null;
}
