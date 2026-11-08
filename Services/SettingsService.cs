using System.Text.Json;
using System.Text.Json.Serialization;
using Waypoint.Models;

namespace Waypoint.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

internal static class SettingsService
{
    static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Waypoint");
    static readonly string SettingsPath = Path.Combine(DataFolder, "settings.json");
    static readonly string LogPath = Path.Combine(DataFolder, "error.log");

    public static AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(SettingsPath))
                settings = JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), SettingsJsonContext.Default.AppSettings);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogError(ex);
        }
        settings ??= new AppSettings();
        if (!settings.QuickAccessSeeded)
        {
            settings.PinnedFolders = DefaultQuickAccess();
            settings.QuickAccessSeeded = true;
        }
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            string temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogError(ex);
        }
    }

    public static void LogError(Exception? ex)
    {
        if (ex is null)
            return;
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    static List<string> DefaultQuickAccess()
    {
        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Locations.Downloads,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        ];
        return candidates.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
