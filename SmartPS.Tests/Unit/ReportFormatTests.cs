namespace SmartPS.Tests.Unit;

/// <summary>R3 / R4 / N3: Vietnamese number formats (1.250.000 ₫, 45,3%), hour ranges and day labels, independent of the thread culture.</summary>
public class ReportFormatTests
{
    [Theory]
    [InlineData(1_250_000, "1.250.000 ₫")]
    [InlineData(-10_000, "-10.000 ₫")]
    [InlineData(0, "0 ₫")]
    [InlineData(500, "500 ₫")]
    public void Currency(int amount, string expected)
    {
        Assert.Equal(expected, ReportFormat.Currency(amount));
    }

    [Fact]
    public void Currency_does_not_depend_on_the_current_culture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("1.250.000 ₫", ReportFormat.Currency(1_250_000m));
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ja-JP");
            Assert.Equal("1.250.000 ₫", ReportFormat.Currency(1_250_000m));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Number_uses_dot_grouping_and_comma_decimals()
    {
        Assert.Equal("1.234.567", ReportFormat.Number(1_234_567));
        Assert.Equal("12,5", ReportFormat.Number(12.5, 1));
        Assert.Equal(".", ReportFormat.VietnameseNumbers.NumberGroupSeparator);
        Assert.Equal(",", ReportFormat.VietnameseNumbers.NumberDecimalSeparator);
    }

    [Fact]
    public void Percent()
    {
        Assert.Equal("45,3%", ReportFormat.Percent(45.3));
        Assert.Equal("—", ReportFormat.Percent(null));
    }

    [Theory]
    [InlineData(23, "23:00–24:00")]
    [InlineData(17, "17:00–18:00")]
    public void HourRange(int hour, string expected)
    {
        Assert.Equal(expected, ReportFormat.HourRange(hour));
    }

    [Fact]
    public void HourRange_without_a_peak_is_a_dash()
    {
        Assert.Equal("—", ReportFormat.HourRange(null));
    }

    [Fact]
    public void DayLabel_is_dd_MM()
    {
        Assert.Equal("01/10", ReportFormat.DayLabel(new DateOnly(2026, 10, 1)));
        Assert.Equal("31/12", ReportFormat.DayLabel(new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void Dash_is_the_em_dash()
    {
        Assert.Equal("—", ReportFormat.Dash);
    }

    [Fact]
    public void Duration_without_value_is_a_dash_and_does_not_localize()
    {
        var calls = 0;
        Assert.Equal("—", ReportFormat.Duration(null, (_, _) => { calls++; return "x"; }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Duration_with_value_is_formatted_through_a_report_text_key()
    {
        var keys = new List<string>();

        var text = ReportFormat.Duration(95, (key, args) => { keys.Add(key); return key + ":" + string.Join(",", args); });

        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.Contains(k, ReportTextKeys.All));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
