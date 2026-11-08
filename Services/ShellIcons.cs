using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Waypoint.Interop;
using static Waypoint.Interop.NativeMethods;

namespace Waypoint.Services;

/// <summary>Small shell icons and type names, cached. UI thread only.</summary>
internal static class ShellIcons
{
    const string FolderKey = "<folder>";
    const string NoExtensionKey = "<none>";

    static readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> typeNames = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> ownIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".cur", ".ani", ".scr", ".msc", ".cpl", ".appref-ms",
    };

    public static ImageSource? ThisPcIcon => GetForPath("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");

    /// <summary>True for file types whose icon depends on the file itself, such as programs and shortcuts.</summary>
    public static bool HasOwnIcon(string path) => ownIconExtensions.Contains(Path.GetExtension(path));

    /// <summary>The icon for a type of item, looked up by extension only.</summary>
    public static ImageSource? GetGeneric(string path, bool isFolder)
    {
        string key = isFolder ? FolderKey : Path.GetExtension(path) is { Length: > 0 } ext ? ext : NoExtensionKey;
        if (!icons.TryGetValue(key, out var icon))
        {
            string probe = isFolder ? "folder" : key == NoExtensionKey ? "file" : "file" + key;
            icon = Load(probe, isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, SHGFI_USEFILEATTRIBUTES);
            icons[key] = icon;
        }
        return icon;
    }

    /// <summary>The icon for a specific item, such as a drive, a known folder or a shortcut.</summary>
    public static ImageSource? GetForPath(string path)
    {
        // Network paths can stall the UI while the shell resolves them.
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return null;
        string key = "|" + path;
        if (!icons.TryGetValue(key, out var icon))
        {
            icon = Load(path, 0, 0);
            icons[key] = icon;
        }
        return icon;
    }

    /// <summary>A large icon (from the 256 px system list when available) for tiles.</summary>
    public static ImageSource? GetLarge(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return null;
        string key = "L|" + path;
        if (!icons.TryGetValue(key, out var icon))
        {
            icon = LoadFromImageList(path, SHIL_JUMBO) ?? LoadFromImageList(path, SHIL_EXTRALARGE);
            icons[key] = icon;
        }
        return icon;
    }

    public static string GetTypeName(string path, bool isFolder)
    {
        if (isFolder)
            return "File folder";
        string ext = Path.GetExtension(path);
        if (ext.Length == 0)
            return "File";
        if (!typeNames.TryGetValue(ext, out var name))
        {
            name = LoadTypeName("file" + ext) ?? $"{ext.TrimStart('.').ToUpperInvariant()} File";
            typeNames[ext] = name;
        }
        return name;
    }

    static unsafe ImageSource? Load(string path, uint attributes, uint extraFlags)
    {
        SHFILEINFOW info = default;
        nint result = SHGetFileInfo(path, attributes, &info, (uint)sizeof(SHFILEINFOW), SHGFI_ICON | SHGFI_SMALLICON | extraFlags);
        if (result == 0 || info.hIcon == 0)
            return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    static unsafe ImageSource? LoadFromImageList(string path, int list)
    {
        SHFILEINFOW info = default;
        if (SHGetFileInfo(path, 0, &info, (uint)sizeof(SHFILEINFOW), SHGFI_SYSICONINDEX) == 0)
            return null;
        if (SHGetImageList(list, IID_IImageList, out nint imageList) != 0 || imageList == 0)
            return null;
        try
        {
            nint hIcon = ImageList_GetIcon(imageList, info.iIcon, ILD_TRANSPARENT);
            if (hIcon == 0)
                return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.Release(imageList);
        }
    }

    static unsafe string? LoadTypeName(string probe)
    {
        SHFILEINFOW info = default;
        if (SHGetFileInfo(probe, FILE_ATTRIBUTE_NORMAL, &info, (uint)sizeof(SHFILEINFOW), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES) == 0)
            return null;
        var name = new string(info.szTypeName);
        return name.Length > 0 ? name : null;
    }
}
