using System.Windows.Controls;

namespace Waypoint.Services;

internal static class MenuFactory
{
    public static MenuItem Item(string header, string glyph, Action action, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Icon = glyph, InputGestureText = gesture ?? "", IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Adds a separator unless the menu is empty or already ends with one.</summary>
    public static void Separator(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not System.Windows.Controls.Separator)
            menu.Items.Add(new System.Windows.Controls.Separator());
    }
}
