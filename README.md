# Waypoint

A tabbed file explorer for Windows, built with WPF on .NET 10. It has no dependency on the Windows App SDK.

## Download

Get the latest `Waypoint-<version>-win-x64.zip` from [Releases](https://github.com/cistrans10-bit/Waypoint/releases/latest), extract it and run `Waypoint.exe`. It runs on 64-bit Windows 10 and 11 without needing .NET installed. The app is unsigned, so Windows SmartScreen may warn on first launch; choose **More info → Run anyway**.

## Features

- Tabs in the title bar (Ctrl+T, Ctrl+W, Ctrl+Tab, middle-click to close)
- Sidebar with **Quick access** (Desktop, Downloads, Documents, Pictures, Music, Videos, plus your pins), **Recent folders**, and **Storage** with capacity bars that turn red above 90% used
- Filter box at the top of the sidebar
- Breadcrumb address bar; click empty space or press Ctrl+L to type a path, click › to jump to a subfolder
- Search box that filters the current folder (Ctrl+F)
- This PC page with drive tiles and usage bars, like File Explorer
- Details view with sortable columns (name, date modified, date created, type, size), natural name sorting, folders first; the chosen sort is remembered
- Cut, copy, paste, rename, delete to Recycle Bin, and new folder, using the Windows shell (undo and progress dialogs included)
- Drag and drop to and from File Explorer, into folders, sidebar items and tabs
- Refreshes automatically when files change
- Follows the Windows light or dark mode setting
- Restores tabs, window size and sidebar width

Settings are stored in `%LOCALAPPDATA%\Waypoint\settings.json` and errors in `error.log` next to it.

## Build

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

Run `publish\Waypoint.exe`.
