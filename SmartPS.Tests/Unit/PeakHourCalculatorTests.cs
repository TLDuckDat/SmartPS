namespace SmartPS.Tests.Unit;

/// <summary>AC-6: the peak hour is the VN hour with the most check-ins; ties go to the earlier hour; no data → null.</summary>
public class PeakHourCalculatorTests
{
    private static int[] Hours(params (int Hour, int Count)[] values)
    {
        var hours = new int[24];
        foreach (var (hour, count) in values)
        {
            hours[hour] = count;
        }

        return hours;
    }

    [Fact]
    public void The_hour_with_the_most_check_ins_wins()
    {
        Assert.Equal(17, PeakHourCalculator.FindPeakHour(Hours((7, 4), (17, 9), (23, 1))));
    }

    [Fact]
    public void AC6_tie_goes_to_the_earlier_hour()
    {
        Assert.Equal(7, PeakHourCalculator.FindPeakHour(Hours((7, 5), (17, 5))));
        Assert.Equal(8, PeakHourCalculator.FindPeakHour(Hours((8, 3), (17, 3), (9, 1))));
    }

    [Fact]
    public void Hour_zero_and_hour_23_are_valid_peaks()
    {
        Assert.Equal(0, PeakHourCalculator.FindPeakHour(Hours((0, 2))));
        Assert.Equal(23, PeakHourCalculator.FindPeakHour(Hours((23, 2), (5, 1))));
    }

    [Fact]
    public void All_zero_is_null()
    {
        Assert.Null(PeakHourCalculator.FindPeakHour(new int[24]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(25)]
    public void Input_must_have_24_hours(int length)
    {
        Assert.ThrowsAny<ArgumentException>(() => PeakHourCalculator.FindPeakHour(new int[length]));
    }
}
