using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;

namespace Waypoint.Services;

/// <summary>File copy/cut/paste through the Windows clipboard, compatible with File Explorer.</summary>
internal static class FileClipboard
{
    const string DropEffectFormat = "Preferred DropEffect";
    const int DropEffectCopy = 5;
    const int DropEffectMove = 2;

    public static void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        var data = new DataObject();
        data.SetFileDropList(list);
        data.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));
        Retry(() => Clipboard.SetDataObject(data, true));
    }

    public static void SetText(string text) => Retry(() => Clipboard.SetText(text));

    public static bool TryGetFiles(out string[] paths, out bool cut)
    {
        paths = [];
        cut = false;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
                return false;
            paths = files;
            if (data.GetData(DropEffectFormat) is MemoryStream stream && stream.Length >= 4)
            {
                var bytes = new byte[4];
                stream.ReadExactly(bytes);
                cut = (BitConverter.ToInt32(bytes) & DropEffectMove) != 0;
            }
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    public static bool HasFiles()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch (ExternalException) { return false; }
    }

    public static void Clear()
    {
        try { Clipboard.Clear(); }
        catch (ExternalException) { }
    }

    // The clipboard can be briefly locked by another process.
    static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
        }
    }
}
