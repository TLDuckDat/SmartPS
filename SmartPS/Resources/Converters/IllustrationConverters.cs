using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartPS.Resources.Converters;

/// <summary>
/// Chọn hình minh họa cho mục menu / thẻ theo Id phân hệ: "Illust_" + Id (ví dụ Illust_GateControl).
/// Tra cứu tài nguyên ở code-behind thay vì đặt {StaticResource} trong DataTrigger:
/// cách sau làm WPF ném XamlParseException (NamedObject không ép được sang ImageSource) khi trigger chạy.
/// </summary>
public class ModuleIllustrationConverter : IValueConverter
{
    private const string KeyPrefix = "Illust_";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;
        return FindImage(KeyPrefix + value);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static ImageSource? FindImage(string key)
    {
        return Application.Current?.TryFindResource(key) as ImageSource;
    }
}

/// <summary>
/// Chọn hình minh họa loại xe. Nhận VehicleTypeId (1 = xe máy, 2 = ô tô, 3 = xe đạp)
/// hoặc biểu tượng emoji (🏍, 🚗, 🚲). ConverterParameter là key dự phòng khi không khớp.
/// </summary>
public class VehicleIllustrationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            int id when id == 1 => "Illust_Motorbike",
            int id when id == 2 => "Illust_Car",
            int id when id == 3 => "Illust_Bicycle",
            string icon when icon == "🏍" => "Illust_Motorbike",
            string icon when icon == "🚗" => "Illust_Car",
            string icon when icon == "🚲" => "Illust_Bicycle",
            _ => parameter as string ?? "Illust_Car"
        };

        return ModuleIllustrationConverter.FindImage(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
