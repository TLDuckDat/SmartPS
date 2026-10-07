using System.Globalization;
using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

/// <summary>Vietnamese number formats (1.250.000 ₫, 45,3%) independent of the thread culture.</summary>
public static class ReportFormat
{
    public const string Dash = "—";

    public static NumberFormatInfo VietnameseNumbers { get; } = CreateNumberFormat();

    public static string Currency(decimal amount)
        => amount.ToString("#,##0", VietnameseNumbers) + " ₫";

    public static string Number(double value, int decimals = 0)
        => value.ToString("N" + Math.Max(decimals, 0).ToString(CultureInfo.InvariantCulture), VietnameseNumbers);

    public static string Percent(double? value)
        => value is null ? Dash : value.Value.ToString("0.#", VietnameseNumbers) + "%";

    /// <summary>"▲20%" | "▼12,5%" | "0%" | "—".</summary>
    public static string Change(KpiComparison? comparison)
    {
        if (comparison?.ChangePercent is not { } change)
        {
            return Dash;
        }

        var magnitude = Math.Abs(change).ToString("0.#", VietnameseNumbers);
        return comparison.Trend switch
        {
            KpiTrend.Up => "▲" + magnitude + "%",
            KpiTrend.Down => "▼" + magnitude + "%",
            _ => "0%"
        };
    }

    public static string DayLabel(DateOnly day)
        => day.ToString("dd/MM", CultureInfo.InvariantCulture);

    public static string HourRange(int? hourVn)
        => hourVn is null
            ? Dash
            : string.Create(CultureInfo.InvariantCulture, $"{hourVn.Value:00}:00–{hourVn.Value + 1:00}:00");

    /// <summary>Formats minutes through the localized duration keys; <paramref name="format"/> receives a key from <see cref="ReportTextKeys"/>.</summary>
    public static string Duration(double? minutes, Func<string, object[], string> format)
    {
        if (minutes is null)
        {
            return Dash;
        }

        var total = (int)Math.Round(minutes.Value, MidpointRounding.AwayFromZero);
        return total >= 60
            ? format(ReportTextKeys.DurationFormat, new object[] { total / 60, total % 60 })
            : format(ReportTextKeys.DurationMinutes, new object[] { total });
    }

    private static NumberFormatInfo CreateNumberFormat()
    {
        var info = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        info.NumberGroupSeparator = ".";
        info.NumberDecimalSeparator = ",";
        info.NegativeSign = "-";
        info.NumberGroupSizes = new[] { 3 };
        return NumberFormatInfo.ReadOnly(info);
    }
}
