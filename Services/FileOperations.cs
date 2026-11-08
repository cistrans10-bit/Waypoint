using System.ComponentModel;
using System.Diagnostics;
using Waypoint.Interop;
using static Waypoint.Interop.NativeMethods;

namespace Waypoint.Services;

internal static class FileOperations
{
    const int ErrorNoAssociation = 1155;

    public static void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
        {
            OpenWith(path);
        }
    }

    public static void OpenWith(string path) =>
        Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}") { UseShellExecute = false });

    public static void OpenInExplorer(string folder) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false });

    public static void OpenTerminal(string folder)
    {
        // A trailing backslash would escape the closing quote, so "C:\" becomes "C:\.".
        string dir = folder.EndsWith('\\') ? folder + "." : folder;
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir}\"") { UseShellExecute = false });
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = folder, UseShellExecute = true });
        }
    }

    public static unsafe void ShowProperties(string path, nint owner)
    {
        fixed (char* verb = "properties")
        fixed (char* file = path)
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                fMask = SEE_MASK_INVOKEIDLIST,
                hwnd = owner,
                lpVerb = verb,
                lpFile = file,
                nShow = SW_SHOW,
            };
            ShellExecuteEx(&info);
        }
    }

    public static bool Copy(IReadOnlyCollection<string> sources, string destination, nint owner)
    {
        ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR;
        if (sources.Any(s => Locations.PathEquals(Path.GetDirectoryName(s), destination)))
            flags |= FOF_RENAMEONCOLLISION;
        return Run(FO_COPY, sources, destination, flags, owner);
    }

    public static bool Move(IReadOnlyCollection<string> sources, string destination, nint owner)
    {
        var moving = sources.Where(s => !Locations.PathEquals(Path.GetDirectoryName(s), destination)).ToList();
        return moving.Count == 0 || Run(FO_MOVE, moving, destination, FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR, owner);
    }

    public static bool Delete(IReadOnlyCollection<string> paths, bool permanent, nint owner) =>
        Run(FO_DELETE, paths, null, permanent ? (ushort)0 : (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING), owner);

    public static string Rename(string path, string newName)
    {
        string target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (Directory.Exists(path))
            Directory.Move(path, target);
        else
            File.Move(path, target);
        return target;
    }

    public static string UniqueName(string folder, string baseName)
    {
        string name = baseName;
        for (int i = 2; Directory.Exists(Path.Combine(folder, name)) || File.Exists(Path.Combine(folder, name)); i++)
            name = $"{baseName} ({i})";
        return name;
    }

    /// <summary>Returns a message describing why <paramref name="name"/> can't be used, or null.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A name can't contain any of these characters: \\ / : * ? \" < > |";
        if (name.EndsWith('.'))
            return "A name can't end with a period.";
        return null;
    }

    static unsafe bool Run(uint operation, IEnumerable<string> sources, string? destination, ushort flags, nint owner)
    {
        // The shell expects double-null-terminated lists.
        string from = string.Join('\0', sources) + "\0\0";
        string? to = destination is null ? null : destination + "\0\0";
        fixed (char* pFrom = from)
        fixed (char* pTo = to)
        {
            var op = new SHFILEOPSTRUCTW { hwnd = owner, wFunc = operation, pFrom = pFrom, pTo = pTo, fFlags = flags };
            return SHFileOperation(&op) == 0 && op.fAnyOperationsAborted == 0;
        }
    }
}
