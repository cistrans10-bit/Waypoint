using System.Runtime.InteropServices;

namespace Waypoint.Interop;

internal static unsafe partial class NativeMethods
{
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_TYPENAME = 0x400;
    public const uint SHGFI_SYSICONINDEX = 0x4000;

    public const int SHIL_EXTRALARGE = 2;
    public const int SHIL_JUMBO = 4;
    public const uint ILD_TRANSPARENT = 0x1;
    public static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    public const uint FO_MOVE = 1;
    public const uint FO_COPY = 2;
    public const uint FO_DELETE = 3;
    public const ushort FOF_RENAMEONCOLLISION = 0x8;
    public const ushort FOF_ALLOWUNDO = 0x40;
    public const ushort FOF_NOCONFIRMMKDIR = 0x200;
    public const ushort FOF_WANTNUKEWARNING = 0x4000;

    public const uint SEE_MASK_INVOKEIDLIST = 0xC;
    public const int SW_SHOW = 5;

    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public char* pFrom;
        public char* pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public char* lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHELLEXECUTEINFOW
    {
        public uint cbSize;
        public uint fMask;
        public nint hwnd;
        public char* lpVerb;
        public char* lpFile;
        public char* lpParameters;
        public char* lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public char* lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfo(string pszPath, uint dwFileAttributes, SHFILEINFOW* psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    /// <summary>Returns the system image list as an IImageList pointer, which can be used directly as an HIMAGELIST.</summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHGetImageList(int iImageList, in Guid riid, out nint ppvObj);

    [LibraryImport("comctl32.dll")]
    public static partial nint ImageList_GetIcon(nint himl, int i, uint flags);

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    public static partial int SHFileOperation(SHFILEOPSTRUCTW* lpFileOp);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellExecuteEx(SHELLEXECUTEINFOW* pExecInfo);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out char* ppszPath);

    [LibraryImport("shlwapi.dll", EntryPoint = "StrCmpLogicalW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int StrCmpLogical(string psz1, string psz2);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, int* pvAttribute, uint cbAttribute);
}
