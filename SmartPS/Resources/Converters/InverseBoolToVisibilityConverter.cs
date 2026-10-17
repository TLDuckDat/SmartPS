using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmartPS.Resources.Converters;

/// <summary>Visible when the bound boolean is false (used for "no data" overlays).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
