using System.Globalization;
using System.Windows.Data;

namespace Vidora.Features.Download;

public sealed class TransferValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (parameter as string) switch
        {
            "Bytes" when value is long bytes && bytes >= 0 => FormatSize(bytes, culture),
            "Speed" when value is double speed && double.IsFinite(speed) && speed >= 0 => FormatSize(speed, culture) + "/s",
            "Duration" when value is TimeSpan time && time >= TimeSpan.Zero =>
                $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}",
            _ => "—"
        };
    }

    private static string FormatSize(double value, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return value.ToString(unit == 0 ? "0" : "0.#", culture) + " " + units[unit];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
