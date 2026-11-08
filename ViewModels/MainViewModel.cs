using System.Collections.ObjectModel;
using Waypoint.Models;
using Waypoint.Services;

namespace Waypoint.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    const int MaxRecents = 8;

    TabViewModel? selectedTab;
    string selectionText = "";
    bool hasSelection;

    public MainViewModel(AppSettings settings, string? startPath)
    {
        Settings = settings;
        Sidebar = new SidebarViewModel(settings);

        var paths = string.IsNullOrWhiteSpace(startPath) ? settings.OpenTabs : [startPath];
        foreach (var path in paths)
            OpenTab(path, select: false);
        if (Tabs.Count == 0)
            OpenTab(Locations.ThisPc, select: false);
        int index = string.IsNullOrWhiteSpace(startPath) ? settings.SelectedTab : 0;
        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
    }

    public event EventHandler<TabLoadedEventArgs>? TabLoaded;

    public AppSettings Settings { get; }
    public SidebarViewModel Sidebar { get; }
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public TabViewModel? SelectedTab
    {
        get => selectedTab;
        set
        {
            var previous = selectedTab;
            if (!SetProperty(ref selectedTab, value))
                return;
            if (previous is not null)
                previous.IsSelected = false;
            if (value is not null)
            {
                value.IsSelected = true;
                Sidebar.SetActivePath(value.CurrentPath);
            }
            SetSelection([]);
        }
    }

    public bool ShowHidden
    {
        get => Settings.ShowHidden;
        set
        {
            if (Settings.ShowHidden == value)
                return;
            Settings.ShowHidden = value;
            OnPropertyChanged();
            foreach (var tab in Tabs)
                tab.Refresh();
        }
    }

    public string SelectionText { get => selectionText; private set => SetProperty(ref selectionText, value); }
    public bool HasSelection { get => hasSelection; private set => SetProperty(ref hasSelection, value); }

    public TabViewModel OpenTab(string path, bool select = true)
    {
        var sort = Enum.IsDefined((SortColumn)Settings.SortColumn) ? (SortColumn)Settings.SortColumn : SortColumn.Name;
        var tab = new TabViewModel(() => Settings.ShowHidden, sort, Settings.SortDescending);
        tab.Loaded += (_, e) => TabLoaded?.Invoke(this, e);
        tab.Navigated += OnTabNavigated;
        Tabs.Add(tab);
        if (select)
            SelectedTab = tab;
        _ = OpenInitialAsync(tab, path);
        return tab;
    }

    public void CloseTab(TabViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        if (tab == SelectedTab)
            SelectedTab = Tabs.Count > 1 ? Tabs[index == Tabs.Count - 1 ? index - 1 : index + 1] : null;
        Tabs.RemoveAt(index);
        tab.Dispose();
    }

    /// <summary>Makes the tab's sort order the default for new tabs and future sessions.</summary>
    public void RememberSort(TabViewModel tab)
    {
        Settings.SortColumn = (int)tab.SortColumn;
        Settings.SortDescending = tab.SortDescending;
    }

    public void CycleTab(int delta)
    {
        if (Tabs.Count < 2 || SelectedTab is null)
            return;
        int index = (Tabs.IndexOf(SelectedTab) + delta + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[index];
    }

    public bool IsPinned(string path) => Settings.PinnedFolders.Any(p => Locations.PathEquals(p, path));

    public void Pin(string path)
    {
        if (IsPinned(path))
            return;
        Settings.PinnedFolders.Add(path);
        Sidebar.RefreshPinned();
        SettingsService.Save(Settings);
    }

    public void Unpin(string path)
    {
        Settings.PinnedFolders.RemoveAll(p => Locations.PathEquals(p, path));
        Sidebar.RefreshPinned();
        SettingsService.Save(Settings);
    }

    public void RemoveRecent(string path)
    {
        Settings.RecentFolders.RemoveAll(p => Locations.PathEquals(p, path));
        Sidebar.RefreshRecents();
    }

    public void ClearRecents()
    {
        Settings.RecentFolders.Clear();
        Sidebar.RefreshRecents();
    }

    public void SetSelection(IReadOnlyList<FileItem> items)
    {
        HasSelection = items.Count > 0;
        if (items.Count == 0)
        {
            SelectionText = "";
            return;
        }
        long bytes = items.Where(i => !i.IsFolder).Sum(i => i.Size);
        string count = items.Count == 1 ? "1 item selected" : $"{items.Count:N0} items selected";
        SelectionText = bytes > 0 ? $"{count}  ·  {Format.Size(bytes)}" : count;
    }

    public void SaveState()
    {
        Settings.OpenTabs = Tabs.Select(t => t.CurrentPath).Where(p => p.Length > 0).ToList();
        Settings.SelectedTab = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab);
        SettingsService.Save(Settings);
    }

    static async Task OpenInitialAsync(TabViewModel tab, string path)
    {
        if (!await tab.NavigateAsync(path))
            await tab.NavigateAsync(Locations.ThisPc);
    }

    void OnTabNavigated(object? sender, string path)
    {
        if (sender == SelectedTab)
            Sidebar.SetActivePath(path);
        if (path == Locations.ThisPc)
            return;
        var recents = Settings.RecentFolders;
        recents.RemoveAll(p => Locations.PathEquals(p, path));
        recents.Insert(0, path);
        if (recents.Count > MaxRecents)
            recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);
        Sidebar.RefreshRecents();
    }
}
