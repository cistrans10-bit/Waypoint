using Waypoint.Services;

namespace Waypoint.Models;

/// <summary>A snapshot of one folder entry, safe to create off the UI thread.</summary>
public sealed record FileEntry(
    string Name,
    string Path,
    bool IsFolder,
    long Size,
    DateTime? Modified,
    bool Hidden,
    bool IsDrive = false,
    string? TypeName = null,
    string? SizeText = null,
    double UsedPercent = 0,
    DateTime? Created = null)
{
    public static List<FileEntry> List(string folder, bool includeHidden)
    {
        if (folder == Locations.ThisPc)
            return Drives();

        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };
        var entries = new List<FileEntry>();
        foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
        {
            var attributes = info.Attributes;
            bool hidden = (attributes & FileAttributes.Hidden) != 0;
            // Hidden + system marks protected OS files, which Explorer never shows either.
            if (hidden && (!includeHidden || (attributes & FileAttributes.System) != 0))
                continue;
            bool isFolder = (attributes & FileAttributes.Directory) != 0;
            long size = info is FileInfo file ? file.Length : -1;
            entries.Add(new FileEntry(info.Name, info.FullName, isFolder, size, info.LastWriteTime, hidden, Created: info.CreationTime));
        }
        return entries;
    }

    static List<FileEntry> Drives() =>
        DriveData.ReadAll()
            .Select(d => new FileEntry(d.Label, d.Root, true, d.Total, null, false, true, d.TypeName, d.CapacityText, d.UsedPercent))
            .ToList();
}
