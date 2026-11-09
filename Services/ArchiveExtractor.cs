using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Waypoint.Services;

public enum ExtractMode
{
    /// <summary>Into a new folder named after the archive, next to it.</summary>
    ToNewFolder,
    Here,
    ToChosenFolder,
}

internal sealed record ExtractResult(int Files, int Skipped);

/// <summary>Extracts zip, 7z, rar and tar/gz/bz2/xz archives.</summary>
internal static class ArchiveExtractor
{
    // Formats with a central directory, which can be listed without reading the whole file.
    static readonly string[] SeekableSuffixes = [".zip", ".7z", ".rar"];

    // Longer suffixes first so "x.tar.gz" becomes "x", not "x.tar".
    static readonly string[] StreamSuffixes = [".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".tbz2", ".txz", ".tar", ".gz", ".bz2", ".xz"];

    public static bool IsArchive(string path) => IsSeekable(path) || StreamSuffixes.Any(s => EndsWith(path, s));

    /// <summary>The archive's name without its archive extension, used for "Extract to folder".</summary>
    public static string BaseName(string path)
    {
        string name = Path.GetFileName(path);
        foreach (var suffix in StreamSuffixes.Concat(SeekableSuffixes))
        {
            if (name.Length > suffix.Length && EndsWith(name, suffix))
                return name[..^suffix.Length];
        }
        return Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>Counts files that already exist at the destination. Only zip, 7z and rar can be checked up front.</summary>
    public static int CountConflicts(string archivePath, string destination)
    {
        if (!IsSeekable(archivePath))
            return 0;
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        int count = 0;
        foreach (var entry in archive.Entries)
        {
            if (!entry.IsDirectory && entry.Key is { } key && TryGetTarget(destination, key, out var target) && File.Exists(target))
                count++;
        }
        return count;
    }

    public static ExtractResult Extract(string archivePath, string destination, bool overwrite, IProgress<double>? progress)
    {
        Directory.CreateDirectory(destination);
        var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };
        using var stream = File.OpenRead(archivePath);
        using var archive = IsSeekable(archivePath) ? ArchiveFactory.OpenArchive(stream) : null;
        if (archive is not null && archive.Entries.Any(e => e.IsEncrypted))
            throw new NotSupportedException($"\"{Path.GetFileName(archivePath)}\" is password-protected. Waypoint can't extract password-protected archives yet.");

        int files = 0;
        int skipped = 0;
        foreach (var (key, isDirectory, write) in EnumerateEntries(archivePath, stream, archive, options))
        {
            if (!TryGetTarget(destination, key, out var target))
                continue;
            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (!overwrite && File.Exists(target))
            {
                skipped++;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            write(target);
            files++;
            if (stream.Length > 0)
                progress?.Report((double)stream.Position / stream.Length);
        }
        return new ExtractResult(files, skipped);
    }

    /// <summary>
    /// Yields entries in archive order. Solid archives and 7z must be read front to back in one pass;
    /// zip and non-solid rar entries are written individually; tar/gz/bz2/xz come from a forward-only reader.
    /// </summary>
    static IEnumerable<(string Key, bool IsDirectory, Action<string> Write)> EnumerateEntries(
        string archivePath, Stream stream, IArchive? archive, ExtractionOptions options)
    {
        if (archive is not null && !archive.IsSolid && archive.Type != ArchiveType.SevenZip)
        {
            foreach (var entry in archive.Entries)
                yield return (entry.Key ?? BaseName(archivePath), entry.IsDirectory, target => entry.WriteToFile(target, options));
            yield break;
        }

        using var reader = archive is not null ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            // A plain .gz holds one unnamed file.
            yield return (entry.Key ?? BaseName(archivePath), entry.IsDirectory, target => reader.WriteEntryToFile(target, options));
        }
    }

    public static string Describe(Exception ex, string archiveName) => ex switch
    {
        NotSupportedException => ex.Message,
        _ when ex is CryptographicException || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) =>
            $"\"{archiveName}\" is password-protected. Waypoint can't extract password-protected archives yet.",
        _ => $"Couldn't extract \"{archiveName}\".\n\n{ex.Message}",
    };

    /// <summary>
    /// Maps an entry name to a path inside <paramref name="destination"/>, replacing characters Windows
    /// doesn't allow and rejecting names that would escape the folder (such as "../").
    /// </summary>
    static bool TryGetTarget(string destination, string key, out string target)
    {
        string root = Path.GetFullPath(destination).TrimEnd('\\') + "\\";
        var invalid = Path.GetInvalidFileNameChars();
        var parts = key.Replace('/', '\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => string.Concat(part.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)));
        target = Path.GetFullPath(Path.Combine(root, string.Join('\\', parts)));
        return target.Length > root.Length && target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSeekable(string path) => SeekableSuffixes.Any(s => EndsWith(path, s));

    static bool EndsWith(string path, string suffix) => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
