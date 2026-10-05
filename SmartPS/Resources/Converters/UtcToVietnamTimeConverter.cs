using System.Globalization;
using System.Windows.Data;

namespace SmartPS.Resources.Converters;

public class UtcToVietnamTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dateTime)
            return string.Empty;

        // Dữ liệu trong database được lưu dưới dạng UTC.
        var utc = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

        // Việt Nam = UTC+7
        var vietnamTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            utc,
            "SE Asia Standard Time");

        return vietnamTime.ToString("dd/MM/yyyy HH:mm:ss");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}