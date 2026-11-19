using System.Globalization;
using System.Windows.Data;
using SmartPS.Services.Audit;

namespace SmartPS.Resources.Converters;

/// <summary>Mã hành động nhật ký (SHIFT_OPEN...) thành câu dễ hiểu ("Mở ca trực").</summary>
public class AuditActionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AuditDisplayText.Action(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Kết quả nhật ký (Success/Denied/Failed) thành chữ theo ngôn ngữ đang chọn.</summary>
public class AuditOutcomeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AuditDisplayText.Outcome(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Loại đối tượng + mã đối tượng ("Shift", "1") thành "Ca trực 1".</summary>
public class AuditEntityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => AuditDisplayText.Entity(values.ElementAtOrDefault(0) as string, values.ElementAtOrDefault(1)?.ToString());

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
