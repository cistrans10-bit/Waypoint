using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Waypoint.Models;
using Waypoint.Services;

namespace Waypoint.ViewModels;

public enum SidebarItemKind
{
    Pinned,
    Recent,
    ThisPc,
    Drive,
}

public sealed class SidebarItem : ObservableObject
{
    bool isActive;

    public required string Label { get; init; }
    public required string Path { get; init; }
    public SidebarItemKind Kind { get; init; }
    public ImageSource? Icon { get; init; }
    public string Glyph { get; init; } = "";
    public bool HasIcon => Icon is not null;
    public bool HasCapacity { get; init; }
    public double UsedPercent { get; init; }
    public bool IsNearlyFull => HasCapacity && UsedPercent >= 90;
    public string CapacityText { get; init; } = "";
    public string ToolTip => Kind is SidebarItemKind.ThisPc ? "This PC" : Path;
    public bool IsActive { get => isActive; set => SetProperty(ref isActive, value); }
}

public sealed class SidebarSection : ObservableObject
{
    bool isExpanded = true;

    public SidebarSection(string title, string key)
    {
        Title = title;
        Key = key;
        View = new ListCollectionView(Items);
    }

    public string Title { get; }
    public string Key { get; }
    public ObservableCollection<SidebarItem> Items { get; } = [];
    public ListCollectionView View { get; }
    public bool HasVisibleItems => !View.IsEmpty;
    public bool IsExpanded { get => isExpanded; set => SetProperty(ref isExpanded, value); }

    public void ReplaceItems(IEnumerable<SidebarItem> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(nameof(HasVisibleItems));
    }

    public void ApplyFilter(string text)
    {
        View.Filter = text.Length == 0
            ? null
            : obj => obj is SidebarItem item
                && (item.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                    || item.Path.Contains(text, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(HasVisibleItems));
    }
}

public sealed class SidebarViewModel : ObservableObject
{
    readonly AppSettings settings;
    readonly DispatcherTimer driveTimer;
    string filterText = "";
    string activePath = "";
    string driveSignature = "";
    bool refreshingDrives;

    public SidebarViewModel(AppSettings settings)
    {
        this.settings = settings;
        QuickAccess = new SidebarSection("Quick access", "quick");
        Recents = new SidebarSection("Recent folders", "recent");
        Storage = new SidebarSection("Storage", "storage");
        Sections = [QuickAccess, Recents, Storage];
        foreach (var section in Sections)
        {
            section.IsExpanded = !settings.CollapsedSections.Contains(section.Key);
            section.PropertyChanged += OnSectionChanged;
        }

        RefreshPinned();
        RefreshRecents();
        Storage.ReplaceItems([CreateThisPcItem()]);
        _ = RefreshDrivesAsync();

        // Picks up free space changes; drive arrival and removal come from WM_DEVICECHANGE.
        driveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        driveTimer.Tick += async (_, _) => await RefreshDrivesAsync();
        driveTimer.Start();
    }

    public SidebarSection QuickAccess { get; }
    public SidebarSection Recents { get; }
    public SidebarSection Storage { get; }
    public IReadOnlyList<SidebarSection> Sections { get; }

    public string FilterText
    {
        get => filterText;
        set
        {
            if (!SetProperty(ref filterText, value))
                return;
            foreach (var section in Sections)
                section.ApplyFilter(value.Trim());
        }
    }

    public void RefreshPinned()
    {
        QuickAccess.ReplaceItems(settings.PinnedFolders.Select(p => CreateFolderItem(p, SidebarItemKind.Pinned)));
        ApplyActive();
    }

    public void RefreshRecents() =>
        Recents.ReplaceItems(settings.RecentFolders.Select(p => CreateFolderItem(p, SidebarItemKind.Recent)));

    public async Task RefreshDrivesAsync()
    {
        if (refreshingDrives)
            return;
        refreshingDrives = true;
        try
        {
            var drives = await Task.Run(() => DriveData.ReadAll());
            // Ignore free space changes under 64 MB so the list doesn't rebuild constantly.
            string signature = string.Join('|', drives.Select(d => $"{d.Root}:{d.Label}:{d.Total}:{d.Free >> 26}"));
            if (signature == driveSignature)
                return;
            driveSignature = signature;
            var items = drives.Select(d => new SidebarItem
            {
                Label = d.Label,
                Path = d.Root,
                Kind = SidebarItemKind.Drive,
                Icon = ShellIcons.GetForPath(d.Root),
                Glyph = "",
                HasCapacity = d.Total > 0,
                UsedPercent = d.UsedPercent,
                CapacityText = d.CapacityText,
            });
            Storage.ReplaceItems(items.Prepend(CreateThisPcItem()));
            ApplyActive();
        }
        finally
        {
            refreshingDrives = false;
        }
    }

    public void SetActivePath(string path)
    {
        activePath = path;
        ApplyActive();
    }

    void ApplyActive()
    {
        bool found = false;
        foreach (var item in QuickAccess.Items.Concat(Storage.Items))
        {
            item.IsActive = !found && Locations.PathEquals(item.Path, activePath);
            found |= item.IsActive;
        }
    }

    void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SidebarSection.IsExpanded) || sender is not SidebarSection section)
            return;
        settings.CollapsedSections.Remove(section.Key);
        if (!section.IsExpanded)
            settings.CollapsedSections.Add(section.Key);
    }

    static SidebarItem CreateFolderItem(string path, SidebarItemKind kind) => new()
    {
        Label = Locations.DisplayName(path),
        Path = path,
        Kind = kind,
        Icon = ShellIcons.GetForPath(path) ?? ShellIcons.GetGeneric(path, true),
    };

    static SidebarItem CreateThisPcItem() => new()
    {
        Label = "This PC",
        Path = Locations.ThisPc,
        Kind = SidebarItemKind.ThisPc,
        Icon = ShellIcons.ThisPcIcon,
        Glyph = "",
    };
}
