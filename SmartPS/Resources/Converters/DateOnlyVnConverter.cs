using System.Globalization;
using System.Windows.Data;

namespace SmartPS.Resources.Converters;

/// <summary>Shows a Vietnam calendar day (<see cref="DateOnly"/>) as dd/MM/yyyy.</summary>
public class DateOnlyVnConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateOnly day ? day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
