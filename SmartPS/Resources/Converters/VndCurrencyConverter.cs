using System.Globalization;
using System.Windows.Data;
using SmartPS.Services.Reports;

namespace SmartPS.Resources.Converters;

/// <summary>Formats a money amount the Vietnamese way (1.250.000 ₫) regardless of the UI culture.</summary>
public class VndCurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        decimal amount => ReportFormat.Currency(amount),
        double amount => ReportFormat.Currency((decimal)amount),
        int amount => ReportFormat.Currency(amount),
        _ => ReportFormat.Dash
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
