using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Waypoint.Interop;

namespace Waypoint.Services;

/// <summary>Follows the Windows light/dark app setting.</summary>
internal static class ThemeService
{
    public static bool IsDark { get; private set; }

    public static void Initialize() => Apply(ReadSystemDark());

    /// <summary>Re-reads the system setting, for example after WM_SETTINGCHANGE.</summary>
    public static void Refresh()
    {
        bool dark = ReadSystemDark();
        if (dark != IsDark)
            Apply(dark);
    }

    public static unsafe void ApplyWindowFrame(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
            return;
        int dark = IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        int corners = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(int));
    }

    static void Apply(bool dark)
    {
        IsDark = dark;
        var theme = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = theme };
        foreach (Window window in Application.Current.Windows)
            ApplyWindowFrame(window);
    }

    static bool ReadSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
