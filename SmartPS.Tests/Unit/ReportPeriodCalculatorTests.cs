namespace SmartPS.Tests.Unit;

/// <summary>R1, R6, AC-1, E4, AC-5 (previous period), N2: Vietnam-day boundaries, presets and range validation as pure functions.</summary>
public class ReportPeriodCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public void AC1_E4_23_30_VN_on_01_10_belongs_to_01_10()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), ReportPeriodCalculator.ToVietnamDate(Utc(2026, 10, 1, 16, 30)));
        Assert.Equal(23, ReportPeriodCalculator.ToVietnamHour(Utc(2026, 10, 1, 16, 30)));
    }

    [Fact]
    public void VN_midnight_is_17_00Z_of_the_previous_UTC_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 2), ReportPeriodCalculator.ToVietnamDate(Utc(2026, 10, 1, 17, 0)));
        Assert.Equal(0, ReportPeriodCalculator.ToVietnamHour(Utc(2026, 10, 1, 17, 0)));
        Assert.Equal(new DateOnly(2026, 10, 1), ReportPeriodCalculator.ToVietnamDate(Utc(2026, 10, 1, 16, 59, 59)));
        Assert.Equal(23, ReportPeriodCalculator.ToVietnamHour(Utc(2026, 10, 1, 16, 59, 59)));
    }

    [Fact]
    public void Early_UTC_morning_maps_to_the_same_VN_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), ReportPeriodCalculator.ToVietnamDate(Utc(2026, 10, 1, 0, 0)));
        Assert.Equal(7, ReportPeriodCalculator.ToVietnamHour(Utc(2026, 10, 1, 0, 0)));
    }

    [Fact]
    public void TodayVn_uses_the_VN_offset()
    {
        Assert.Equal(new DateOnly(2026, 10, 8), ReportPeriodCalculator.TodayVn(Utc(2026, 10, 7, 17, 0)));
        Assert.Equal(new DateOnly(2026, 10, 7), ReportPeriodCalculator.TodayVn(Utc(2026, 10, 7, 16, 59)));
    }

    [Fact]
    public void ToUtcRange_of_one_VN_day_is_half_open_and_Kind_Utc()
    {
        var (start, end) = ReportPeriodCalculator.ToUtcRange(new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));

        Assert.Equal(Utc(2026, 9, 30, 17, 0), start);
        Assert.Equal(Utc(2026, 10, 1, 17, 0), end);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(DateTimeKind.Utc, end.Kind);

        // 16:30Z (23:30 VN on 01/10) is inside, 17:00Z (00:00 VN on 02/10) is outside.
        var lateEvening = Utc(2026, 10, 1, 16, 30);
        Assert.True(lateEvening >= start && lateEvening < end);
    }

    [Fact]
    public void ToUtcRange_of_several_days_ends_at_the_VN_midnight_after_ToVn()
    {
        var (start, end) = ReportPeriodCalculator.ToUtcRange(new ReportDateRange(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 8)));

        Assert.Equal(AuditTime.VietnamDateStartUtc(new DateOnly(2026, 10, 2)), start);
        Assert.Equal(AuditTime.VietnamDateStartUtc(new DateOnly(2026, 10, 9)), end);
    }

    public static TheoryData<ReportPeriodPreset, DateOnly, DateOnly> Presets() => new()
    {
        { ReportPeriodPreset.Today, new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 8) },
        { ReportPeriodPreset.Yesterday, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 7) },
        { ReportPeriodPreset.Last7Days, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 8) },
        { ReportPeriodPreset.Last30Days, new DateOnly(2026, 9, 9), new DateOnly(2026, 10, 8) },
        { ReportPeriodPreset.ThisMonth, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8) },
        { ReportPeriodPreset.LastMonth, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30) },
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void Presets_resolve_relative_to_today_VN(ReportPeriodPreset preset, DateOnly from, DateOnly to)
    {
        var range = ReportPeriodCalculator.Resolve(preset, Today);

        Assert.Equal(new ReportDateRange(from, to), range);
    }

    [Fact]
    public void Last7Days_and_Last30Days_have_7_and_30_days()
    {
        Assert.Equal(7, ReportPeriodCalculator.Resolve(ReportPeriodPreset.Last7Days, Today).DayCount);
        Assert.Equal(30, ReportPeriodCalculator.Resolve(ReportPeriodPreset.Last30Days, Today).DayCount);
    }

    [Fact]
    public void LastMonth_in_January_is_December_of_the_previous_year()
    {
        Assert.Equal(new ReportDateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)),
            ReportPeriodCalculator.Resolve(ReportPeriodPreset.LastMonth, new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public void LastMonth_handles_February_of_a_leap_year()
    {
        Assert.Equal(new ReportDateRange(new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)),
            ReportPeriodCalculator.Resolve(ReportPeriodPreset.LastMonth, new DateOnly(2028, 3, 31)));
    }

    [Fact]
    public void ThisMonth_on_the_first_is_a_single_day()
    {
        Assert.Equal(new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)),
            ReportPeriodCalculator.Resolve(ReportPeriodPreset.ThisMonth, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Custom_uses_the_given_dates()
    {
        var range = ReportPeriodCalculator.Resolve(ReportPeriodPreset.Custom, Today, new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 20));

        Assert.Equal(new ReportDateRange(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 20)), range);
    }

    [Fact]
    public void Custom_invalid_throws_ArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => ReportPeriodCalculator.Resolve(ReportPeriodPreset.Custom, Today, null, Today));
        Assert.ThrowsAny<ArgumentException>(() => ReportPeriodCalculator.Resolve(ReportPeriodPreset.Custom, Today, Today, Today.AddDays(-1)));
        Assert.ThrowsAny<ArgumentException>(() => ReportPeriodCalculator.Resolve(ReportPeriodPreset.Custom, Today, Today.AddDays(-366), Today));
    }

    [Fact]
    public void PreviousOf_is_the_same_length_immediately_before()
    {
        var range = new ReportDateRange(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 8));

        var previous = ReportPeriodCalculator.PreviousOf(range);

        Assert.Equal(new ReportDateRange(new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 1)), previous);
        Assert.Equal(range.DayCount, previous.DayCount);
    }

    [Fact]
    public void PreviousOf_a_single_day_is_the_day_before()
    {
        Assert.Equal(new ReportDateRange(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30)),
            ReportPeriodCalculator.PreviousOf(new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1))));
    }

    [Fact]
    public void PreviousOf_a_full_month_has_the_same_day_count_not_the_previous_month()
    {
        var october = new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal(new ReportDateRange(new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30)), ReportPeriodCalculator.PreviousOf(october));
    }

    [Fact]
    public void Validate_reports_missing_dates()
    {
        Assert.Equal(ReportRangeValidation.Missing, ReportPeriodCalculator.Validate(null, Today));
        Assert.Equal(ReportRangeValidation.Missing, ReportPeriodCalculator.Validate(Today, null));
        Assert.Equal(ReportRangeValidation.Missing, ReportPeriodCalculator.Validate(null, null));
    }

    [Fact]
    public void Validate_rejects_from_after_to()
    {
        Assert.Equal(ReportRangeValidation.FromAfterTo, ReportPeriodCalculator.Validate(Today, Today.AddDays(-1)));
    }

    [Fact]
    public void Validate_accepts_one_day_and_exactly_366_days_but_not_367()
    {
        Assert.Equal(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(Today, Today));
        Assert.Equal(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(Today.AddDays(-365), Today));
        Assert.Equal(ReportRangeValidation.TooLong, ReportPeriodCalculator.Validate(Today.AddDays(-366), Today));
    }

    [Fact]
    public void Sql_VN_offset_matches_AuditTime()
    {
        Assert.Equal(ReportSql.VietnamOffsetHours, AuditTime.VietnamOffset.TotalHours);
    }

    [Fact]
    public void ReportDateRange_DayCount_Key_and_Days()
    {
        var range = new ReportDateRange(new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 2));

        Assert.Equal(4, range.DayCount);
        Assert.Equal("20260929-20261002", range.Key);
        Assert.Equal(
            new[] { new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2) },
            range.Days().ToArray());
    }

    [Fact]
    public void Default_export_file_name_follows_the_spec_pattern()
    {
        Assert.Equal("BaoCao_SmartPS_20261001-20261008.xlsx",
            ReportFileNames.Default(new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8))));
    }

    [Fact]
    public void Limits_are_the_spec_values()
    {
        Assert.Equal(366, ReportLimits.MaxRangeDays);
        Assert.Equal(100_000, ReportLimits.MaxExportSessionRows);
        Assert.Equal(10, ReportLimits.TopPlateCount);
        Assert.Equal(300, ReportLimits.DebounceMilliseconds);
    }

    [Fact]
    public void Sheet_names_are_the_six_spec_names_in_order()
    {
        Assert.Equal(new[] { "Tổng quan", "Theo ngày", "Theo giờ", "Theo ca", "Theo loại xe", "Chi tiết lượt gửi" }, ReportSheetNames.All);
        Assert.Equal("Tổng quan", ReportSheetNames.Overview);
        Assert.Equal("Chi tiết lượt gửi", ReportSheetNames.Sessions);
    }

    [Fact]
    public void Filter_HasSessionFilter_only_when_vehicle_zone_or_group_is_set()
    {
        var range = new ReportDateRange(Today, Today);

        Assert.False(new ReportFilter(range).HasSessionFilter);
        Assert.True(new ReportFilter(range, VehicleTypeId: 1).HasSessionFilter);
        Assert.True(new ReportFilter(range, ZoneId: 2).HasSessionFilter);
        Assert.True(new ReportFilter(range, CustomerGroup: ReportCustomerGroup.Visitor).HasSessionFilter);
    }

    [Fact]
    public void Customer_group_enum_values_match_the_SQL_parameter_contract()
    {
        Assert.Equal(0, (int)ReportCustomerGroup.All);
        Assert.Equal(1, (int)ReportCustomerGroup.Resident);
        Assert.Equal(2, (int)ReportCustomerGroup.MonthlyPass);
        Assert.Equal(3, (int)ReportCustomerGroup.Visitor);
    }
}
