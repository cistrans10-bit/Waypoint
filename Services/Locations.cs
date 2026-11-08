using System.Runtime.InteropServices;
using Waypoint.Interop;

namespace Waypoint.Services;

public static class Locations
{
    /// <summary>Pseudo path for the drive list.</summary>
    public const string ThisPc = "::ThisPC";

    static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="folder"/>.</summary>
    public static bool IsInside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(string path)
    {
        if (path == ThisPc)
            return "This PC";
        var trimmed = path.TrimEnd('\\');
        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    public static string Downloads => GetKnownFolder(DownloadsFolderId)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    static unsafe string? GetKnownFolder(Guid id)
    {
        if (NativeMethods.SHGetKnownFolderPath(id, 0, 0, out char* path) != 0)
            return null;
        try { return new string(path); }
        finally { Marshal.FreeCoTaskMem((nint)path); }
    }
}
