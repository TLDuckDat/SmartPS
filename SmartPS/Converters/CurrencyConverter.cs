using System;
using System.Globalization;
using System.Windows.Data;

namespace SmartPS.Converters;

public class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            if (d == 0) return "0";
            return d.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            s = s.Replace(".", "").Replace(",", "");
            if (decimal.TryParse(s, out decimal d))
                return d;
        }
        return 0m;
    }
}
