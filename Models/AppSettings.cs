namespace Waypoint.Models;

public sealed class AppSettings
{
    public bool QuickAccessSeeded { get; set; }
    public List<string> PinnedFolders { get; set; } = [];
    public List<string> RecentFolders { get; set; } = [];
    public List<string> CollapsedSections { get; set; } = [];
    public List<string> OpenTabs { get; set; } = [];
    public int SelectedTab { get; set; }
    public bool ShowHidden { get; set; }
    public int SortColumn { get; set; }
    public bool SortDescending { get; set; }
    public double SidebarWidth { get; set; } = 250;
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}
