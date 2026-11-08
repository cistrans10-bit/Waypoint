namespace Waypoint.Services;

internal static class Format
{
    static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    public static string Size(long bytes)
    {
        if (bytes < 1024)
            return bytes == 1 ? "1 byte" : $"{bytes:N0} bytes";
        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < Units.Length - 1);
        return $"{value.ToString(value < 100 ? "0.#" : "0")} {Units[unit]}";
    }
}
