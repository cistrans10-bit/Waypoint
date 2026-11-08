namespace Waypoint.Services;

internal sealed record DriveData(string Root, string Label, string TypeName, long Total, long Free)
{
    public double UsedPercent => Total > 0 ? 100.0 * (Total - Free) / Total : 0;

    public string CapacityText => Total > 0 ? $"{Format.Size(Free)} free of {Format.Size(Total)}" : "";

    /// <summary>Reads every drive. Can block on slow or disconnected drives, so call it off the UI thread.</summary>
    public static List<DriveData> ReadAll() => DriveInfo.GetDrives().Select(Read).ToList();

    static DriveData Read(DriveInfo drive)
    {
        string letter = drive.Name.TrimEnd('\\');
        string type = drive.DriveType switch
        {
            DriveType.Fixed => "Local Disk",
            DriveType.Removable => "USB Drive",
            DriveType.Network => "Network Drive",
            DriveType.CDRom => "CD Drive",
            DriveType.Ram => "RAM Disk",
            _ => "Drive",
        };
        try
        {
            if (drive.IsReady)
            {
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? type : drive.VolumeLabel;
                return new(drive.Name, $"{label} ({letter})", type, drive.TotalSize, drive.AvailableFreeSpace);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return new(drive.Name, $"{type} ({letter})", type, 0, 0);
    }
}
