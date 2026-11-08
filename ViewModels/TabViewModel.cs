using System.Collections;
using System.Collections.ObjectModel;
using System.Security;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Waypoint.Interop;
using Waypoint.Models;
using Waypoint.Services;

namespace Waypoint.ViewModels;

public enum SortColumn
{
    Name,
    Modified,
    Type,
    Size,
    Created,
}

public sealed record Crumb(string Name, string Path);

public sealed class TabLoadedEventArgs(TabViewModel tab, IReadOnlySet<string> selectPaths, bool isNavigation) : EventArgs
{
    public TabViewModel Tab { get; } = tab;
    public IReadOnlySet<string> SelectPaths { get; } = selectPaths;
    public bool IsNavigation { get; } = isNavigation;
}

public sealed class TabViewModel : ObservableObject, IDisposable
{
    static readonly IReadOnlySet<string> NoSelection = new HashSet<string>();

    readonly Stack<string> back = new();
    readonly Stack<string> forward = new();
    readonly Func<bool> showHidden;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer syncTimer;
    FileSystemWatcher? watcher;
    int loadVersion;
    string currentPath = "";
    string title = "";
    ImageSource? icon;
    string filterText = "";
    string statusText = "";
    string emptyText = "";
    string? errorText;
    bool showEmptyText;
    bool isLoading;
    bool isSelected;

    public TabViewModel(Func<bool> showHidden, SortColumn sortColumn = SortColumn.Name, bool sortDescending = false)
    {
        this.showHidden = showHidden;
        SortColumn = sortColumn;
        SortDescending = sortDescending;
        View = new ListCollectionView(Items) { CustomSort = new FileItemComparer(this), Filter = MatchesFilter };
        // Coalesce bursts of file system events into at most one refresh every half second.
        syncTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        syncTimer.Tick += async (_, _) =>
        {
            syncTimer.Stop();
            await SyncAsync();
        };
    }

    public event EventHandler<TabLoadedEventArgs>? Loaded;
    public event EventHandler<string>? Navigated;

    public RangeObservableCollection<FileItem> Items { get; } = [];
    public ListCollectionView View { get; }
    public ObservableCollection<Crumb> Crumbs { get; } = [];

    public string CurrentPath { get => currentPath; private set => SetProperty(ref currentPath, value); }
    public string Title { get => title; private set => SetProperty(ref title, value); }
    public ImageSource? Icon { get => icon; private set => SetProperty(ref icon, value); }
    public string DisplayPath => CurrentPath == Locations.ThisPc ? "This PC" : CurrentPath;
    public string SearchPlaceholder => $"Search {Title}";
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string EmptyText { get => emptyText; private set => SetProperty(ref emptyText, value); }
    public bool ShowEmptyText { get => showEmptyText; private set => SetProperty(ref showEmptyText, value); }
    public bool CanGoBack => back.Count > 0;
    public bool CanGoForward => forward.Count > 0;
    public bool CanGoUp => CurrentPath.Length > 0 && CurrentPath != Locations.ThisPc;
    public SortColumn SortColumn { get; private set; }
    public bool SortDescending { get; private set; }

    public string FilterText
    {
        get => filterText;
        set
        {
            if (!SetProperty(ref filterText, value))
                return;
            View.Refresh();
            UpdateStatus();
        }
    }

    public static bool Exists(string path) => path == Locations.ThisPc || Directory.Exists(path);

    /// <summary>Turns typed or stored text into a normalized folder path.</summary>
    public string ResolveInput(string input)
    {
        string text = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
        if (text.Length == 0 || text == Locations.ThisPc || text.Equals("This PC", StringComparison.OrdinalIgnoreCase))
            return Locations.ThisPc;
        if (text.Length == 2 && text[1] == ':')
            text += "\\";
        try
        {
            text = !Path.IsPathRooted(text) && CanGoUp ? Path.GetFullPath(text, CurrentPath) : Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return text;
        }
        int rootLength = Path.GetPathRoot(text)?.Length ?? 0;
        return text.Length > rootLength ? text.TrimEnd('\\') : text;
    }

    public async Task<bool> NavigateAsync(string path, string? selectAfter = null, bool pushHistory = true)
    {
        path = ResolveInput(path);
        if (!await Task.Run(() => Exists(path)))
            return false;
        if (pushHistory && CurrentPath.Length > 0 && !Locations.PathEquals(CurrentPath, path))
        {
            back.Push(CurrentPath);
            forward.Clear();
        }
        SetLocation(path);
        filterText = "";
        OnPropertyChanged(nameof(FilterText));
        var select = selectAfter is null ? NoSelection : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selectAfter };
        await LoadAsync(select, isNavigation: true);
        Navigated?.Invoke(this, path);
        return true;
    }

    public async Task<bool> GoBackAsync()
    {
        if (back.Count == 0)
            return false;
        string from = CurrentPath;
        if (!await NavigateAsync(back.Pop(), pushHistory: false))
        {
            NotifyHistory();
            return false;
        }
        forward.Push(from);
        NotifyHistory();
        return true;
    }

    public async Task<bool> GoForwardAsync()
    {
        if (forward.Count == 0)
            return false;
        string from = CurrentPath;
        if (!await NavigateAsync(forward.Pop(), pushHistory: false))
        {
            NotifyHistory();
            return false;
        }
        back.Push(from);
        NotifyHistory();
        return true;
    }

    public Task<bool> GoUpAsync()
    {
        if (!CanGoUp)
            return Task.FromResult(false);
        string parent = Directory.GetParent(CurrentPath)?.FullName ?? Locations.ThisPc;
        return NavigateAsync(parent, CurrentPath);
    }

    public Task ReloadAsync(string? select = null) =>
        LoadAsync(select is null ? NoSelection : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { select }, isNavigation: false);

    public void Refresh() => _ = ReloadAsync();

    /// <summary>Sorts by a column, toggling direction when it's already the sort column. Dates start newest first.</summary>
    public void ToggleSort(SortColumn column) =>
        SetSort(column, SortColumn == column ? !SortDescending : column is SortColumn.Modified or SortColumn.Created);

    public void SetSort(SortColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;
        View.Refresh();
    }

    public void Dispose()
    {
        loadVersion++;
        syncTimer.Stop();
        StopWatching();
    }

    void SetLocation(string path)
    {
        CurrentPath = path;
        Title = Locations.DisplayName(path);
        Icon = path == Locations.ThisPc
            ? ShellIcons.ThisPcIcon
            : ShellIcons.GetForPath(path) ?? ShellIcons.GetGeneric(path, true);
        OnPropertyChanged(nameof(DisplayPath));
        OnPropertyChanged(nameof(SearchPlaceholder));
        BuildCrumbs();
        NotifyHistory();
    }

    void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    async Task LoadAsync(IReadOnlySet<string> select, bool isNavigation)
    {
        int version = ++loadVersion;
        string path = CurrentPath;
        bool hidden = showHidden();
        StopWatching();
        IsLoading = true;
        UpdateStatus();

        List<FileEntry> entries = [];
        string? error = null;
        try
        {
            entries = await Task.Run(() => FileEntry.List(path, hidden));
        }
        catch (UnauthorizedAccessException)
        {
            error = "You don't have permission to view this folder.";
        }
        catch (Exception ex) when (ex is IOException or SecurityException)
        {
            error = ex.Message;
        }
        if (version != loadVersion)
            return;

        Items.Reset(entries.Select(FileItem.Create));
        errorText = error;
        IsLoading = false;
        UpdateStatus();
        if (error is null && path != Locations.ThisPc)
            StartWatching(path);
        Loaded?.Invoke(this, new TabLoadedEventArgs(this, select, isNavigation));
        QueueOwnIcons(version);
    }

    /// <summary>Applies folder changes in place so scroll position and selection survive.</summary>
    async Task SyncAsync()
    {
        if (IsLoading || CurrentPath.Length == 0 || CurrentPath == Locations.ThisPc)
            return;
        int version = loadVersion;
        string path = CurrentPath;
        bool hidden = showHidden();
        List<FileEntry> entries;
        try
        {
            entries = await Task.Run(() => FileEntry.List(path, hidden));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return;
        }
        if (version != loadVersion)
            return;

        var fresh = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            fresh.TryAdd(entry.Path, entry);
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (fresh.Remove(Items[i].FullPath, out var entry))
                Items[i].Update(entry);
            else
                Items.RemoveAt(i);
        }
        foreach (var entry in fresh.Values)
        {
            var item = FileItem.Create(entry);
            if (item.NeedsOwnIcon)
                item.Icon = ShellIcons.GetForPath(item.FullPath) ?? item.Icon;
            Items.Add(item);
        }
        UpdateStatus();
    }

    // Program and shortcut icons need a per-file lookup, so they load in small batches while the UI is idle.
    void QueueOwnIcons(int version)
    {
        var pending = Items.Where(i => i.NeedsOwnIcon).ToList();
        if (pending.Count == 0)
            return;
        int index = 0;
        void Step()
        {
            if (version != loadVersion)
                return;
            for (int n = 0; n < 24 && index < pending.Count; n++, index++)
                pending[index].Icon = ShellIcons.GetForPath(pending[index].FullPath) ?? pending[index].Icon;
            if (index < pending.Count)
                dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Step));
        }
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Step));
    }

    void StartWatching(string path)
    {
        try
        {
            watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
                    | NotifyFilters.LastWrite | NotifyFilters.Attributes,
            };
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Changed += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;
            watcher.Error += (_, _) => ScheduleSync();
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            StopWatching();
        }
    }

    void OnFileSystemEvent(object sender, FileSystemEventArgs e) => ScheduleSync();

    void ScheduleSync() => dispatcher.InvokeAsync(() =>
    {
        if (!syncTimer.IsEnabled)
            syncTimer.Start();
    });

    void StopWatching()
    {
        if (watcher is null)
            return;
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        watcher = null;
    }

    void UpdateStatus()
    {
        int count = View.Count;
        StatusText = IsLoading ? "Loading…" : count == 1 ? "1 item" : $"{count:N0} items";
        EmptyText = errorText
            ?? (Items.Count > 0 ? "No items match your search."
            : CurrentPath == Locations.ThisPc ? "No drives found." : "This folder is empty.");
        ShowEmptyText = !IsLoading && (errorText is not null || count == 0);
    }

    void BuildCrumbs()
    {
        Crumbs.Clear();
        Crumbs.Add(new Crumb("This PC", Locations.ThisPc));
        if (CurrentPath == Locations.ThisPc)
            return;
        string? root = Path.GetPathRoot(CurrentPath);
        if (string.IsNullOrEmpty(root))
            return;
        string current = root.EndsWith('\\') ? root : root + "\\";
        Crumbs.Add(new Crumb(root.TrimEnd('\\'), current));
        foreach (var part in CurrentPath[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            Crumbs.Add(new Crumb(part, current));
        }
    }

    bool MatchesFilter(object obj) =>
        filterText.Length == 0 || (obj is FileItem item && item.Name.Contains(filterText, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Folders first, then the chosen column, with natural ("file2" before "file10") name order.</summary>
    sealed class FileItemComparer(TabViewModel tab) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (FileItem)x!;
            var b = (FileItem)y!;
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            int result = tab.SortColumn switch
            {
                SortColumn.Modified => Nullable.Compare(a.Modified, b.Modified),
                SortColumn.Created => Nullable.Compare(a.Created, b.Created),
                SortColumn.Type => string.Compare(a.TypeName, b.TypeName, StringComparison.CurrentCultureIgnoreCase),
                SortColumn.Size => a.Size.CompareTo(b.Size),
                _ => 0,
            };
            // Drives go in drive letter order, like File Explorer.
            if (result == 0 && a.IsDrive && b.IsDrive)
                result = string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
            if (result == 0)
                result = NativeMethods.StrCmpLogical(a.Name, b.Name);
            return tab.SortDescending ? -result : result;
        }
    }
}
