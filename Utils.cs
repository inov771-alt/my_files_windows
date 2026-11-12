using System.Globalization;

namespace MyFiles;

internal static class Utils
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string number = unit == 0 ? value.ToString("0", Ru) : value.ToString("0.#", Ru);
        return number + " " + units[unit];
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 0 || double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond))
        {
            bytesPerSecond = 0;
        }
        return FormatSize((long)bytesPerSecond) + "/с";
    }
}
