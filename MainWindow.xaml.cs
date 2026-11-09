using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Waypoint.Models;
using Waypoint.Services;
using Waypoint.ViewModels;
using Waypoint.Views;

namespace Waypoint;

public partial class MainWindow : Window
{
    const int WM_SETTINGCHANGE = 0x001A;
    const int WM_DEVICECHANGE = 0x0219;
    const int ConfirmOpenCount = 15;

    readonly MainViewModel vm;
    readonly ViewBase detailsView;
    readonly ItemsPanelTemplate detailsPanel;
    bool showingDrives;
    Point dragStart;
    bool dragArmed;
    FileItem? pendingSingleSelect;

    public MainWindow(MainViewModel viewModel)
    {
        vm = viewModel;
        DataContext = vm;
        InitializeComponent();
        detailsView = FileList.View;
        detailsPanel = FileList.ItemsPanel;
        ApplySavedBounds();
        SidebarColumn.Width = new GridLength(Math.Clamp(vm.Settings.SidebarWidth, SidebarColumn.MinWidth, SidebarColumn.MaxWidth));
        FileList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));
        vm.PropertyChanged += Vm_PropertyChanged;
        vm.TabLoaded += Vm_TabLoaded;
        UpdateSortHeaders();
        UpdateTitle();
        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateMaximizedState();
        Loaded += (_, _) => FileList.Focus();
        Closing += OnClosing;
    }

    string CurrentFolder => vm.SelectedTab?.CurrentPath ?? Locations.ThisPc;
    bool IsRealFolder => CurrentFolder.Length > 0 && CurrentFolder != Locations.ThisPc;
    nint Hwnd => new WindowInteropHelper(this).Handle;

    #region Window

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        ThemeService.ApplyWindowFrame(this);
        HwndSource.FromHwnd(Hwnd)?.AddHook(WndProc);
        UpdateMaximizedState();
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            ThemeService.Refresh();
        else if (msg == WM_DEVICECHANGE)
            _ = vm.Sidebar.RefreshDrivesAsync();
        return 0;
    }

    void ApplySavedBounds()
    {
        var s = vm.Settings;
        var saved = new Rect(s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight);
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight && desktop.IntersectsWith(saved))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        var s = vm.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        s.SidebarWidth = SidebarColumn.ActualWidth;
        vm.SaveState();
        foreach (var tab in vm.Tabs)
            tab.Dispose();
    }

    // A maximized chrome-less window hangs past the screen edge by the resize border.
    void UpdateMaximizedState()
    {
        if (WindowState == WindowState.Maximized)
        {
            var frame = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4);
            MaximizeButton.Content = "";
        }
        else
        {
            RootGrid.Margin = new Thickness(0);
            MaximizeButton.Content = "";
        }
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void UpdateTitle() => Title = vm.SelectedTab is { Title.Length: > 0 } tab ? $"{tab.Title} - Waypoint" : "Waypoint";

    void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedTab))
            return;
        EndEditAddress();
        UpdateViewMode();
        UpdateSortHeaders();
        UpdateTitle();
        ScrollCrumbsToEnd();
    }

    void Vm_TabLoaded(object? sender, TabLoadedEventArgs e)
    {
        if (e.Tab != vm.SelectedTab)
            return;
        UpdateViewMode();
        UpdateSortHeaders();
        UpdateTitle();
        ScrollCrumbsToEnd();
        if (e.SelectPaths.Count == 0)
        {
            if (e.IsNavigation && FileList.Items.Count > 0)
                FileList.ScrollIntoView(FileList.Items[0]);
            return;
        }
        FileList.SelectedItems.Clear();
        FileItem? first = null;
        foreach (var item in e.Tab.Items)
        {
            if (!e.SelectPaths.Contains(item.FullPath))
                continue;
            FileList.SelectedItems.Add(item);
            first ??= item;
        }
        if (first is null)
            return;
        FileList.ScrollIntoView(first);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (FileList.ItemContainerGenerator.ContainerFromItem(first) is ListViewItem container)
                container.Focus();
        });
    }

    /// <summary>This PC shows drive tiles; every other folder shows the details list.</summary>
    void UpdateViewMode()
    {
        bool drives = vm.SelectedTab?.CurrentPath == Locations.ThisPc;
        if (drives == showingDrives)
            return;
        showingDrives = drives;
        DrivesHeader.Visibility = drives ? Visibility.Visible : Visibility.Collapsed;
        DrivesHeader.IsChecked = true;
        FileList.Visibility = Visibility.Visible;
        if (drives)
        {
            FileList.View = null;
            FileList.ItemContainerStyle = (Style)FindResource("DriveTileItem");
            FileList.ItemTemplate = (DataTemplate)FindResource("DriveTileTemplate");
            FileList.ItemsPanel = (ItemsPanelTemplate)FindResource("DriveTilesPanel");
            ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
        }
        else
        {
            FileList.ItemsPanel = detailsPanel;
            FileList.ItemTemplate = null;
            FileList.ItemContainerStyle = (Style)FindResource("FileListItem");
            FileList.View = detailsView;
            FileList.ClearValue(ScrollViewer.HorizontalScrollBarVisibilityProperty);
        }
    }

    void DrivesHeader_Click(object sender, RoutedEventArgs e) =>
        FileList.Visibility = DrivesHeader.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool inText = Keyboard.FocusedElement is TextBox;
        switch (key)
        {
            case Key.T when mods == ModifierKeys.Control:
                vm.OpenTab(Locations.ThisPc);
                break;
            case Key.W when mods == ModifierKeys.Control:
                if (vm.SelectedTab is { } tab)
                    CloseTab(tab);
                break;
            case Key.Tab when mods.HasFlag(ModifierKeys.Control):
                vm.CycleTab(mods.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
            case Key.L when mods == ModifierKeys.Control:
            case Key.D when mods == ModifierKeys.Alt:
            case Key.F4 when mods == ModifierKeys.None:
                BeginEditAddress();
                break;
            case Key.F when mods == ModifierKeys.Control:
            case Key.E when mods == ModifierKeys.Control:
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Key.Left when mods == ModifierKeys.Alt:
                GoBack();
                break;
            case Key.Right when mods == ModifierKeys.Alt:
                GoForward();
                break;
            case Key.Up when mods == ModifierKeys.Alt:
                GoUp();
                break;
            case Key.Back when mods == ModifierKeys.None && !inText:
                GoBack();
                break;
            case Key.F5:
                vm.SelectedTab?.Refresh();
                break;
            case Key.N when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                CreateFolder();
                break;
            case Key.Escape when Keyboard.FocusedElement == SearchBox:
                if (vm.SelectedTab is { } current)
                    current.FilterText = "";
                FileList.Focus();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    #endregion

    #region Tabs

    void TabArea_SizeChanged(object sender, SizeChangedEventArgs e) =>
        TabStrip.MaxWidth = Math.Max(0, e.NewSize.Width - 34 - 72);

    void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is TabViewModel tab)
            vm.SelectedTab = tab;
    }

    void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).Tag is TabViewModel tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    // Dragging files over a tab switches to it, so they can be dropped into that folder.
    void Tab_DragEnter(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is TabViewModel tab)
            vm.SelectedTab = tab;
    }

    void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is TabViewModel tab)
            CloseTab(tab);
    }

    void NewTab_Click(object sender, RoutedEventArgs e) => vm.OpenTab(Locations.ThisPc);

    void CloseTab(TabViewModel tab)
    {
        if (vm.Tabs.Count == 1)
        {
            Close();
            return;
        }
        vm.CloseTab(tab);
    }

    #endregion

    #region Navigation and address bar

    async void NavigateTo(string path, string? select = null)
    {
        if (vm.SelectedTab is not { } tab)
            return;
        if (!await tab.NavigateAsync(path, select))
            ShowError($"Waypoint can't find \"{path}\". Check the spelling and try again.");
    }

    async void GoBack()
    {
        if (vm.SelectedTab is { CanGoBack: true } tab && !await tab.GoBackAsync())
            ShowError("That folder is no longer available.");
    }

    async void GoForward()
    {
        if (vm.SelectedTab is { CanGoForward: true } tab && !await tab.GoForwardAsync())
            ShowError("That folder is no longer available.");
    }

    async void GoUp()
    {
        if (vm.SelectedTab is { CanGoUp: true } tab)
            await tab.GoUpAsync();
    }

    void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    void Forward_Click(object sender, RoutedEventArgs e) => GoForward();
    void Up_Click(object sender, RoutedEventArgs e) => GoUp();
    void Refresh_Click(object sender, RoutedEventArgs e) => vm.SelectedTab?.Refresh();

    void AddressBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BeginEditAddress();

    void BeginEditAddress()
    {
        if (vm.SelectedTab is not { } tab)
            return;
        AddressBox.Text = tab.DisplayPath;
        AddressBorder.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    void EndEditAddress()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        AddressBorder.Visibility = Visibility.Visible;
    }

    void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string text = AddressBox.Text;
            EndEditAddress();
            FileList.Focus();
            OpenAddress(text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEditAddress();
            FileList.Focus();
            e.Handled = true;
        }
    }

    void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndEditAddress();

    void OpenAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || vm.SelectedTab is not { } tab)
            return;
        string path = tab.ResolveInput(text);
        if (path != Locations.ThisPc && File.Exists(path))
            Run(() => FileOperations.Launch(path));
        else
            NavigateTo(path);
    }

    void ScrollCrumbsToEnd() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, CrumbScroller.ScrollToRightEnd);

    void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is Crumb crumb)
            NavigateTo(crumb.Path);
    }

    void CrumbChevron_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not Crumb crumb)
            return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom, MaxHeight = 520 };
        foreach (var (name, path) in ListSubfolders(crumb.Path))
            menu.Items.Add(MenuFactory.Item(name, "", () => NavigateTo(path)));
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No folders", IsEnabled = false });
        menu.IsOpen = true;
    }

    IEnumerable<(string Name, string Path)> ListSubfolders(string folder)
    {
        if (folder == Locations.ThisPc)
            return DriveInfo.GetDrives().Select(d => (d.Name.TrimEnd('\\'), d.Name));
        try
        {
            return FileEntry.List(folder, vm.ShowHidden)
                .Where(f => f.IsFolder)
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(400)
                .Select(f => (f.Name, f.Path))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    #endregion

    #region Sidebar

    void SidebarItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SidebarItem item)
            NavigateTo(item.Path);
    }

    void SidebarItem_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SidebarItem item)
            return;
        if (e.ChangedButton == MouseButton.Middle)
        {
            vm.OpenTab(item.Path, select: false);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            ShowSidebarMenu(item, (UIElement)sender);
            e.Handled = true;
        }
    }

    void ShowSidebarMenu(SidebarItem item, UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        menu.Items.Add(MenuFactory.Item("Open", "", () => NavigateTo(item.Path)));
        menu.Items.Add(MenuFactory.Item("Open in new tab", "", () => vm.OpenTab(item.Path, select: false)));
        if (item.Kind != SidebarItemKind.ThisPc)
        {
            menu.Items.Add(MenuFactory.Item("Open in Terminal", "", () => Run(() => FileOperations.OpenTerminal(item.Path))));
            MenuFactory.Separator(menu);
            menu.Items.Add(vm.IsPinned(item.Path)
                ? MenuFactory.Item("Unpin from Quick access", "", () => vm.Unpin(item.Path))
                : MenuFactory.Item("Pin to Quick access", "", () => vm.Pin(item.Path)));
            if (item.Kind == SidebarItemKind.Recent)
            {
                menu.Items.Add(MenuFactory.Item("Remove from recent folders", "", () => vm.RemoveRecent(item.Path)));
                menu.Items.Add(MenuFactory.Item("Clear recent folders", "", vm.ClearRecents));
            }
            menu.Items.Add(MenuFactory.Item("Copy as path", "", () => FileClipboard.SetText($"\"{item.Path}\"")));
            MenuFactory.Separator(menu);
            menu.Items.Add(MenuFactory.Item("Properties", "", () => FileOperations.ShowProperties(item.Path, Hwnd)));
        }
        menu.IsOpen = true;
    }

    void SidebarItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropEffect(e, (((FrameworkElement)sender).Tag as SidebarItem)?.Path);
        e.Handled = true;
    }

    void SidebarItem_Drop(object sender, DragEventArgs e) =>
        PerformDrop(e, (((FrameworkElement)sender).Tag as SidebarItem)?.Path);

    #endregion

    #region File list

    List<FileItem> Selected() => FileList.SelectedItems.Cast<FileItem>().ToList();

    static FileItem? ItemFromSource(object source) => FindAncestor<ListViewItem>(source as DependencyObject)?.DataContext as FileItem;

    static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    static bool IsChromeOfList(object source) =>
        FindAncestor<ScrollBar>(source as DependencyObject) is not null
        || FindAncestor<GridViewColumnHeader>(source as DependencyObject) is not null;

    void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) => vm.SetSelection(Selected());

    void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ItemFromSource(e.OriginalSource) is { } item)
            OpenItems([item]);
    }

    void FileList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ItemFromSource(e.OriginalSource) is { IsFolder: true } item)
        {
            vm.OpenTab(item.FullPath, select: false);
            e.Handled = true;
        }
    }

    void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var selection = Selected();
        switch (key)
        {
            case Key.Enter when mods == ModifierKeys.Alt:
                ShowProperties(selection.Count > 0 ? selection[0].FullPath : CurrentFolder);
                break;
            case Key.Enter when mods == ModifierKeys.None:
                if (selection.Count > 0)
                    OpenItems(selection);
                break;
            case Key.Delete:
                DeleteItems(selection, permanent: mods.HasFlag(ModifierKeys.Shift));
                break;
            case Key.F2:
                RenameItem(selection);
                break;
            case Key.C when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                CopyPaths(selection);
                break;
            case Key.C when mods == ModifierKeys.Control:
                CopyToClipboard(selection, cut: false);
                break;
            case Key.X when mods == ModifierKeys.Control:
                CopyToClipboard(selection, cut: true);
                break;
            case Key.V when mods == ModifierKeys.Control:
                PasteInto(CurrentFolder);
                break;
            case Key.Apps:
            case Key.F10 when mods == ModifierKeys.Shift:
                ShowListMenu(selection, PlacementMode.Center);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void FileList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsChromeOfList(e.OriginalSource))
            return;
        if (ItemFromSource(e.OriginalSource) is { } item)
        {
            if (!FileList.SelectedItems.Contains(item))
                FileList.SelectedItem = item;
        }
        else
        {
            FileList.SelectedItems.Clear();
        }
        ShowListMenu(Selected(), PlacementMode.MousePoint);
        e.Handled = true;
    }

    void ShowListMenu(List<FileItem> selection, PlacementMode placement)
    {
        var menu = selection.Count > 0 ? BuildItemMenu(selection) : BuildBackgroundMenu();
        menu.PlacementTarget = FileList;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    ContextMenu BuildItemMenu(List<FileItem> selection)
    {
        var menu = new ContextMenu();
        var first = selection[0];
        bool single = selection.Count == 1;
        bool anyDrive = selection.Any(i => i.IsDrive);

        menu.Items.Add(MenuFactory.Item("Open", "", () => OpenItems(selection), "Enter"));
        if (single && first.IsFolder)
        {
            menu.Items.Add(MenuFactory.Item("Open in new tab", "", () => vm.OpenTab(first.FullPath, select: false)));
            menu.Items.Add(MenuFactory.Item("Open in Terminal", "", () => Run(() => FileOperations.OpenTerminal(first.FullPath))));
            menu.Items.Add(vm.IsPinned(first.FullPath)
                ? MenuFactory.Item("Unpin from Quick access", "", () => vm.Unpin(first.FullPath))
                : MenuFactory.Item("Pin to Quick access", "", () => vm.Pin(first.FullPath)));
        }
        else if (single)
        {
            menu.Items.Add(MenuFactory.Item("Open with…", "", () => Run(() => FileOperations.OpenWith(first.FullPath))));
        }

        if (selection.Any(IsArchive))
        {
            MenuFactory.Separator(menu);
            AddExtractItems(menu, selection);
        }

        MenuFactory.Separator(menu);
        if (!anyDrive)
        {
            menu.Items.Add(MenuFactory.Item("Cut", "", () => CopyToClipboard(selection, cut: true), "Ctrl+X"));
            menu.Items.Add(MenuFactory.Item("Copy", "", () => CopyToClipboard(selection, cut: false), "Ctrl+C"));
        }
        if (single && first.IsFolder)
            menu.Items.Add(MenuFactory.Item("Paste into folder", "", () => PasteInto(first.FullPath), enabled: FileClipboard.HasFiles()));
        menu.Items.Add(MenuFactory.Item("Copy as path", "", () => CopyPaths(selection), "Ctrl+Shift+C"));

        if (!anyDrive)
        {
            MenuFactory.Separator(menu);
            if (single)
                menu.Items.Add(MenuFactory.Item("Rename", "", () => RenameItem(selection), "F2"));
            menu.Items.Add(MenuFactory.Item("Delete", "", () => DeleteItems(selection, permanent: false), "Del"));
        }

        MenuFactory.Separator(menu);
        menu.Items.Add(MenuFactory.Item("Properties", "", () => ShowProperties(first.FullPath), "Alt+Enter"));
        return menu;
    }

    ContextMenu BuildBackgroundMenu()
    {
        var menu = new ContextMenu();
        string folder = CurrentFolder;
        if (IsRealFolder)
        {
            menu.Items.Add(MenuFactory.Item("New folder", "", CreateFolder, "Ctrl+Shift+N"));
            menu.Items.Add(MenuFactory.Item("Paste", "", () => PasteInto(folder), "Ctrl+V", FileClipboard.HasFiles()));
            MenuFactory.Separator(menu);
        }
        if (IsRealFolder)
            menu.Items.Add(MenuFactory.Item("Sort by…", "", () => ShowSortMenu(FileList, PlacementMode.MousePoint)));
        menu.Items.Add(MenuFactory.Item("Refresh", "", () => vm.SelectedTab?.Refresh(), "F5"));
        var hidden = MenuFactory.Item("Show hidden items", "", () => vm.ShowHidden = !vm.ShowHidden);
        hidden.IsChecked = vm.ShowHidden;
        menu.Items.Add(hidden);
        if (IsRealFolder)
        {
            MenuFactory.Separator(menu);
            menu.Items.Add(MenuFactory.Item("Open in Terminal", "", () => Run(() => FileOperations.OpenTerminal(folder))));
            menu.Items.Add(MenuFactory.Item("Open in File Explorer", "", () => Run(() => FileOperations.OpenInExplorer(folder))));
            menu.Items.Add(vm.IsPinned(folder)
                ? MenuFactory.Item("Unpin from Quick access", "", () => vm.Unpin(folder))
                : MenuFactory.Item("Pin to Quick access", "", () => vm.Pin(folder)));
            menu.Items.Add(MenuFactory.Item("Copy as path", "", () => FileClipboard.SetText($"\"{folder}\"")));
            MenuFactory.Separator(menu);
            menu.Items.Add(MenuFactory.Item("Properties", "", () => ShowProperties(folder), "Alt+Enter"));
        }
        return menu;
    }

    static readonly (SortColumn Column, string Label)[] SortChoices =
    [
        (SortColumn.Name, "Name"),
        (SortColumn.Modified, "Date modified"),
        (SortColumn.Created, "Date created"),
        (SortColumn.Type, "Type"),
        (SortColumn.Size, "Size"),
    ];

    void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } || vm.SelectedTab is not { } tab)
            return;
        var sort = column == ModifiedColumn ? SortColumn.Modified
            : column == CreatedColumn ? SortColumn.Created
            : column == TypeColumn ? SortColumn.Type
            : column == SizeColumn ? SortColumn.Size
            : SortColumn.Name;
        tab.ToggleSort(sort);
        vm.RememberSort(tab);
        UpdateSortHeaders();
    }

    void Sort_Click(object sender, RoutedEventArgs e) => ShowSortMenu(SortButton, PlacementMode.Bottom);

    void ShowSortMenu(UIElement target, PlacementMode placement)
    {
        if (vm.SelectedTab is not { } tab)
            return;
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement };
        foreach (var (column, label) in SortChoices)
        {
            // Dates default to newest first when picked from the menu.
            var item = MenuFactory.Item(label, "", () => ApplySort(tab, column, column is SortColumn.Modified or SortColumn.Created));
            item.IsChecked = tab.SortColumn == column;
            menu.Items.Add(item);
        }
        MenuFactory.Separator(menu);
        var ascending = MenuFactory.Item("Ascending", "", () => ApplySort(tab, tab.SortColumn, false));
        ascending.IsChecked = !tab.SortDescending;
        menu.Items.Add(ascending);
        var descending = MenuFactory.Item("Descending", "", () => ApplySort(tab, tab.SortColumn, true));
        descending.IsChecked = tab.SortDescending;
        menu.Items.Add(descending);
        menu.IsOpen = true;
    }

    void ApplySort(TabViewModel tab, SortColumn column, bool descending)
    {
        tab.SetSort(column, descending);
        vm.RememberSort(tab);
        UpdateSortHeaders();
    }

    void UpdateSortHeaders()
    {
        var tab = vm.SelectedTab;
        SetHeader(NameColumn, "Name", SortColumn.Name);
        SetHeader(ModifiedColumn, "Date modified", SortColumn.Modified);
        SetHeader(CreatedColumn, "Date created", SortColumn.Created);
        SetHeader(TypeColumn, "Type", SortColumn.Type);
        SetHeader(SizeColumn, "Size", SortColumn.Size);
        SortLabel.Text = tab is null ? "Sort" : SortChoices.First(c => c.Column == tab.SortColumn).Label;
        SortButton.Visibility = tab?.CurrentPath == Locations.ThisPc ? Visibility.Collapsed : Visibility.Visible;

        void SetHeader(GridViewColumn column, string text, SortColumn key)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (tab is not null && tab.SortColumn == key)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = tab.SortDescending ? "" : "",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 9,
                    Margin = new Thickness(8, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            column.Header = panel;
        }
    }

    #endregion

    #region Commands

    void NewFolder_Click(object sender, RoutedEventArgs e) => CreateFolder();
    void Cut_Click(object sender, RoutedEventArgs e) => CopyToClipboard(Selected(), cut: true);
    void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard(Selected(), cut: false);
    void Paste_Click(object sender, RoutedEventArgs e) => PasteInto(CurrentFolder);
    void Rename_Click(object sender, RoutedEventArgs e) => RenameItem(Selected());
    void Delete_Click(object sender, RoutedEventArgs e) => DeleteItems(Selected(), permanent: false);

    void Terminal_Click(object sender, RoutedEventArgs e)
    {
        var selection = Selected();
        string? folder = selection is [{ IsFolder: true } only] ? only.FullPath : IsRealFolder ? CurrentFolder : null;
        if (folder is not null)
            Run(() => FileOperations.OpenTerminal(folder));
    }

    void Properties_Click(object sender, RoutedEventArgs e)
    {
        var selection = Selected();
        if (selection.Count > 0)
            ShowProperties(selection[0].FullPath);
        else if (IsRealFolder)
            ShowProperties(CurrentFolder);
    }

    static bool IsArchive(FileItem item) => !item.IsFolder && ArchiveExtractor.IsArchive(item.FullPath);

    void Extract_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ExtractButton, Placement = PlacementMode.Bottom };
        AddExtractItems(menu, Selected());
        if (menu.Items.Count > 0)
            menu.IsOpen = true;
    }

    void AddExtractItems(ContextMenu menu, IReadOnlyList<FileItem> selection)
    {
        var archives = selection.Where(IsArchive).ToList();
        if (archives.Count == 0)
            return;
        string toFolder = archives.Count == 1
            ? $"Extract to \"{ArchiveExtractor.BaseName(archives[0].FullPath)}\\\""
            : "Extract each to its own folder";
        menu.Items.Add(MenuFactory.Item(toFolder, "", () => ExtractArchives(archives, ExtractMode.ToNewFolder)));
        menu.Items.Add(MenuFactory.Item("Extract here", "", () => ExtractArchives(archives, ExtractMode.Here)));
        menu.Items.Add(MenuFactory.Item("Extract to…", "", () => ExtractArchives(archives, ExtractMode.ToChosenFolder)));
    }

    async void ExtractArchives(IReadOnlyList<FileItem> archives, ExtractMode mode)
    {
        if (vm.ActivityText.Length > 0)
        {
            ShowError("Another extraction is still running. Try again when it finishes.");
            return;
        }
        string? chosen = null;
        if (mode == ExtractMode.ToChosenFolder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Extract to",
                InitialDirectory = Path.GetDirectoryName(archives[0].FullPath),
            };
            if (dialog.ShowDialog(this) != true)
                return;
            chosen = dialog.FolderName;
        }

        string? newFolder = null;
        int skipped = 0;
        try
        {
            foreach (var archive in archives)
            {
                string parent = Path.GetDirectoryName(archive.FullPath)!;
                string destination = mode switch
                {
                    ExtractMode.ToNewFolder => Path.Combine(parent, FileOperations.UniqueName(parent, ArchiveExtractor.BaseName(archive.FullPath))),
                    ExtractMode.Here => parent,
                    _ => chosen!,
                };
                try
                {
                    bool overwrite = true;
                    if (mode != ExtractMode.ToNewFolder)
                    {
                        int conflicts = await Task.Run(() => ArchiveExtractor.CountConflicts(archive.FullPath, destination));
                        if (conflicts > 0)
                        {
                            var answer = MessageBox.Show(this,
                                $"{conflicts:N0} {(conflicts == 1 ? "file" : "files")} from \"{archive.Name}\" already exist in \"{destination}\".\n\n" +
                                "Replace them? Choose No to keep the existing files.",
                                "Waypoint", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                            if (answer == MessageBoxResult.Cancel)
                                return;
                            overwrite = answer == MessageBoxResult.Yes;
                        }
                    }

                    vm.ActivityText = $"Extracting {archive.Name}…";
                    string name = archive.Name;
                    var progress = new Progress<double>(p => vm.ActivityText = $"Extracting {name}… {p:P0}");
                    var result = await Task.Run(() => ArchiveExtractor.Extract(archive.FullPath, destination, overwrite, progress));
                    skipped += result.Skipped;
                    if (mode == ExtractMode.ToNewFolder)
                        newFolder ??= destination;
                }
                catch (Exception ex)
                {
                    SettingsService.LogError(ex);
                    ShowError(ArchiveExtractor.Describe(ex, archive.Name));
                }
            }
        }
        finally
        {
            vm.ActivityText = "";
        }

        if (skipped > 0)
            MessageBox.Show(this, $"Kept {skipped:N0} existing {(skipped == 1 ? "file" : "files")} instead of replacing them.",
                "Waypoint", MessageBoxButton.OK, MessageBoxImage.Information);
        if (newFolder is not null && vm.SelectedTab is { } tab && Locations.PathEquals(Path.GetDirectoryName(newFolder), tab.CurrentPath))
            await tab.ReloadAsync(newFolder);
    }

    void OpenItems(IReadOnlyList<FileItem> items)
    {
        if (items is [{ IsFolder: true } folder])
        {
            NavigateTo(folder.FullPath);
            return;
        }
        int files = items.Count(i => !i.IsFolder);
        if (files > ConfirmOpenCount && !Confirm($"Open {files} files?"))
            return;
        foreach (var item in items)
        {
            if (item.IsFolder)
                vm.OpenTab(item.FullPath, select: false);
            else
                Run(() => FileOperations.Launch(item.FullPath));
        }
    }

    async void CreateFolder()
    {
        if (!IsRealFolder || vm.SelectedTab is not { } tab)
            return;
        string folder = tab.CurrentPath;
        string? name = InputDialog.Prompt(this, "New folder", "Folder name", FileOperations.UniqueName(folder, "New folder"),
            validate: text => FileOperations.ValidateName(text)
                ?? (Directory.Exists(Path.Combine(folder, text)) || File.Exists(Path.Combine(folder, text))
                    ? "An item with this name already exists." : null));
        if (name is null)
            return;
        string path = Path.Combine(folder, name);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
            return;
        }
        await tab.ReloadAsync(path);
    }

    async void RenameItem(IReadOnlyList<FileItem> selection)
    {
        if (selection is not [{ IsDrive: false } item] || vm.SelectedTab is not { } tab)
            return;
        string folder = Path.GetDirectoryName(item.FullPath)!;
        string? name = InputDialog.Prompt(this, "Rename", item.IsFolder ? "Folder name" : "File name", item.Name,
            selectNameOnly: !item.IsFolder,
            validate: text => FileOperations.ValidateName(text)
                ?? (!text.Equals(item.Name, StringComparison.OrdinalIgnoreCase)
                    && (Directory.Exists(Path.Combine(folder, text)) || File.Exists(Path.Combine(folder, text)))
                    ? "An item with this name already exists." : null));
        if (name is null || name == item.Name)
            return;
        try
        {
            string renamed = FileOperations.Rename(item.FullPath, name);
            await tab.ReloadAsync(renamed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
    }

    void DeleteItems(IReadOnlyList<FileItem> selection, bool permanent)
    {
        var paths = selection.Where(i => !i.IsDrive).Select(i => i.FullPath).ToList();
        if (paths.Count > 0)
            FileOperations.Delete(paths, permanent, Hwnd);
    }

    void CopyToClipboard(IReadOnlyList<FileItem> selection, bool cut)
    {
        var paths = selection.Where(i => !i.IsDrive).Select(i => i.FullPath).ToList();
        if (paths.Count > 0)
            Run(() => FileClipboard.SetFiles(paths, cut));
    }

    void CopyPaths(IReadOnlyList<FileItem> selection)
    {
        if (selection.Count > 0)
            Run(() => FileClipboard.SetText(string.Join(Environment.NewLine, selection.Select(i => $"\"{i.FullPath}\""))));
    }

    void PasteInto(string folder)
    {
        if (folder.Length == 0 || folder == Locations.ThisPc || !FileClipboard.TryGetFiles(out var paths, out bool cut))
            return;
        bool done = cut ? FileOperations.Move(paths, folder, Hwnd) : FileOperations.Copy(paths, folder, Hwnd);
        if (done && cut)
            FileClipboard.Clear();
    }

    void ShowProperties(string path)
    {
        if (path != Locations.ThisPc)
            FileOperations.ShowProperties(path, Hwnd);
    }

    #endregion

    #region Drag and drop

    void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(FileList);
        pendingSingleSelect = null;
        if (IsChromeOfList(e.OriginalSource))
        {
            dragArmed = false;
            return;
        }
        var item = ItemFromSource(e.OriginalSource);
        dragArmed = item is not null;
        if (item is null)
        {
            // Clicking empty space clears the selection, like File Explorer.
            if (Keyboard.Modifiers == ModifierKeys.None)
                FileList.SelectedItems.Clear();
            FileList.Focus();
        }
        else if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None
            && FileList.SelectedItems.Count > 1 && FileList.SelectedItems.Contains(item))
        {
            // Keep a multi-selection intact so it can be dragged; collapse it on mouse up instead.
            pendingSingleSelect = item;
            e.Handled = true;
        }
    }

    void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (pendingSingleSelect is { } item)
            FileList.SelectedItem = item;
        pendingSingleSelect = null;
        dragArmed = false;
    }

    void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragArmed || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(FileList) - dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        dragArmed = false;
        pendingSingleSelect = null;
        var paths = Selected().Where(i => !i.IsDrive).Select(i => i.FullPath).ToArray();
        if (paths.Length == 0)
            return;
        DragDrop.DoDragDrop(FileList, new DataObject(DataFormats.FileDrop, paths),
            DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    string? ListDropTarget(DragEventArgs e) =>
        ItemFromSource(e.OriginalSource) is { IsFolder: true } folder ? folder.FullPath
        : IsRealFolder ? CurrentFolder
        : null;

    void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropEffect(e, ListDropTarget(e));
        e.Handled = true;
    }

    void FileList_Drop(object sender, DragEventArgs e) => PerformDrop(e, ListDropTarget(e));

    static string[]? DroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;

    /// <summary>File Explorer rules: move within a drive, copy across drives, Ctrl forces copy and Shift forces move.</summary>
    static DragDropEffects DropEffect(DragEventArgs e, string? target)
    {
        if (target is null || target == Locations.ThisPc || DroppedPaths(e) is not { Length: > 0 } paths)
            return DragDropEffects.None;
        if (paths.Any(p => Locations.PathEquals(p, target) || Locations.IsInside(target, p)))
            return DragDropEffects.None;
        bool copy = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
        bool move = !copy && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey);
        if (!copy && paths.All(p => Locations.PathEquals(Path.GetDirectoryName(p), target)))
            return DragDropEffects.None;
        if (!copy && !move)
            move = Locations.PathEquals(Path.GetPathRoot(paths[0]), Path.GetPathRoot(target));
        var effect = move ? DragDropEffects.Move : DragDropEffects.Copy;
        return (e.AllowedEffects & effect) != 0 ? effect : e.AllowedEffects & DragDropEffects.Copy;
    }

    void PerformDrop(DragEventArgs e, string? target)
    {
        var effect = DropEffect(e, target);
        e.Effects = effect;
        e.Handled = true;
        if (effect == DragDropEffects.None || DroppedPaths(e) is not { } paths || target is null)
            return;
        // Run after the drop returns so the drag source isn't blocked by the copy.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (effect == DragDropEffects.Move)
                FileOperations.Move(paths, target, Hwnd);
            else
                FileOperations.Copy(paths, target, Hwnd);
        });
    }

    #endregion

    void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ExternalException)
        {
            ShowError(ex.Message);
        }
    }

    void ShowError(string message) =>
        MessageBox.Show(this, message, "Waypoint", MessageBoxButton.OK, MessageBoxImage.Warning);

    bool Confirm(string message) =>
        MessageBox.Show(this, message, "Waypoint", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
