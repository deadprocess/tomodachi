using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Tomodachi.Core;
using Tomodachi.Localization;

namespace Tomodachi.Widgets;

/// <summary>
/// Rechtsklick-Menü auf ein Fenster, gemeinsam für den Fenster-Reiter und die Fensterleiste:
/// Nach vorn holen, Schließen und, falls in den Einstellungen eingeschaltet, Task beenden.
/// </summary>
internal static class WindowMenu
{
    /// <param name="activated">Nach „Nach vorn holen“, z. B. um die Notch zuzuklappen.</param>
    public static void Show(FrameworkElement target, nint hwnd, TomodachiConfig? config, Action? activated = null)
    {
        var menu = new ContextMenu
        {
            Style = (Style)target.FindResource("NotchContextMenu"),
            PlacementTarget = target,
            Placement = PlacementMode.MousePoint,
        };
        menu.Items.Add(Item("Windows_BringToFront", "", () =>
        {
            WindowListService.Activate(hwnd);
            activated?.Invoke();
        }));
        menu.Items.Add(Item("Windows_Close", "", () => WindowListService.Close(hwnd)));
        if (config?.EnableEndTask == true)
        {
            var endTask = Item("Windows_EndTask", "", () => WindowListService.EndTask(hwnd));
            endTask.IsEnabled = WindowListService.CanEndTask(hwnd); // ausgegraut beim Datei-Explorer
            menu.Items.Add(endTask);
        }
        menu.IsOpen = true;
    }

    private static MenuItem Item(string textKey, string icon, Action onClick)
    {
        var item = new MenuItem { Header = Loc.Get(textKey), Tag = icon };
        item.Click += (_, _) => onClick();
        return item;
    }
}
