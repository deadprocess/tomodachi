using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tomodachi.Launcher;

/// <summary>
/// Paket 3.3: Suchfeld + Trefferliste. Komplett per Tastatur bedienbar, der Fokus bleibt immer im Suchfeld.
/// Above the results a row of up to <see cref="PinnedApps.Max"/> pinned apps as tiles (Ctrl+1 … Ctrl+5 launches them).
/// </summary>
public partial class LauncherView : UserControl
{
    private const int MaxResults = 8;

    private readonly ObservableCollection<AppResult> _results = [];
    private readonly ObservableCollection<AppResult> _pinnedTiles = [];
    private AppIndex? _index;
    private LaunchHistory? _history;
    private PinnedApps? _pinned;
    private AppEntry? _menuApp;

    public LauncherView()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
        Pinned.ItemsSource = _pinnedTiles;
    }

    /// <summary>Gewählte App und ob sie mit Adminrechten starten soll.</summary>
    internal event Action<AppEntry, bool>? LaunchRequested;

    public event Action? CloseRequested;

    internal void Initialize(AppIndex index, LaunchHistory history, PinnedApps pinned)
    {
        _index = index;
        _history = history;
        _pinned = pinned;
        index.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) return;
            UpdateResults();
            UpdatePinned();
        });
        pinned.Changed += UpdatePinned;
    }

    /// <summary>Beim Öffnen: leeres Suchfeld, zuletzt genutzte Apps.</summary>
    public void Reset()
    {
        Query.Text = "";
        UpdateResults();
        UpdatePinned();
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
        UpdatePinnedVisibility();
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

    /// <summary>Rebuilds the tiles from the pinned ids that exist in the index, in pin order.</summary>
    private void UpdatePinned()
    {
        if (_index is null || _pinned is null) return;

        var known = _pinnedTiles.ToDictionary(r => r.App.Id);
        _pinnedTiles.Clear();
        foreach (var app in _pinned.Resolve(_index.Apps))
        {
            var tile = known.GetValueOrDefault(app.Id) ?? new AppResult(app);
            _pinnedTiles.Add(tile);
            if (tile.Icon is null) LoadIcon(tile);
        }
        UpdatePinnedVisibility();
    }

    /// <summary>The tiles are for the no-typing case; they make room for the results as soon as the user types.</summary>
    private void UpdatePinnedVisibility() =>
        Pinned.Visibility = _pinnedTiles.Count > 0 && Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

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
            default:
                if (Keyboard.Modifiers == ModifierKeys.Control && PinnedSlot(e.Key) is { } slot && slot < _pinnedTiles.Count)
                {
                    LaunchRequested?.Invoke(_pinnedTiles[slot].App, false);
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>Ctrl+1 … Ctrl+5 (top row or numpad) → index of the pinned tile, else null.</summary>
    private static int? PinnedSlot(Key key) => key switch
    {
        >= Key.D1 and <= Key.D5 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad5 => key - Key.NumPad1,
        _ => null,
    };

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

    /// <summary>Rechtsklick: Treffer auswählen und das Menü (Starten, Als Administrator starten, Anpinnen) an der Maus öffnen.</summary>
    private void OnResultRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (ResultAt(e) is not { } result) return;
        e.Handled = true;
        Results.SelectedItem = result;
        OpenMenu(result.App, Results);
    }

    private void OnPinnedClicked(object sender, MouseButtonEventArgs e)
    {
        if (TileAt(e) is { } tile)
        {
            LaunchRequested?.Invoke(tile.App, false);
            e.Handled = true;
        }
    }

    private void OnPinnedRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (TileAt(e) is not { } tile) return;
        e.Handled = true;
        OpenMenu(tile.App, Pinned);
    }

    /// <summary>Shows either "Pin" (disabled when the row is full) or "Unpin", depending on the app.</summary>
    private void OpenMenu(AppEntry app, UIElement target)
    {
        _menuApp = app;
        var menu = (ContextMenu)Resources["ResultMenu"];
        var pinned = _pinned?.IsPinned(app.Id) ?? false;
        var pinItem = (MenuItem)menu.Items[2];
        var unpinItem = (MenuItem)menu.Items[3];
        pinItem.Visibility = pinned ? Visibility.Collapsed : Visibility.Visible;
        pinItem.IsEnabled = _pinned is { IsFull: false };
        unpinItem.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;

        menu.PlacementTarget = target;
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

    private void OnMenuPin(object sender, RoutedEventArgs e)
    {
        if (_menuApp is { } app) _pinned?.Pin(app.Id);
    }

    private void OnMenuUnpin(object sender, RoutedEventArgs e)
    {
        if (_menuApp is { } app) _pinned?.Unpin(app.Id);
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

    private AppResult? TileAt(MouseButtonEventArgs e) =>
        ItemsControl.ContainerFromElement(Pinned, (DependencyObject)e.OriginalSource) is ContentPresenter { Content: AppResult tile }
            ? tile
            : null;
}
