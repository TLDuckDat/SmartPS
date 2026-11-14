using SmartPS.Models.Parking;
using SmartPS.Services.Pricing;

namespace SmartPS.Tests.Unit;

/// <summary>KAN-29b: công thức cước thuần (không DB, không đồng hồ). Giờ Việt Nam = UTC+7.</summary>
public class PricingEngineTests
{
    private static readonly PricingEngine Engine = new();

    private static readonly PricingPolicy Policy = new()
    {
        VehicleTypeId = 1,
        Block4hPrice = 5000m,
        DailyPrice = 25000m,
        GracePeriodMinutes = 15,
        OvernightSurcharge = 2000m,
        OverQuotaDailyPrice = 30000m,
        PolicyVersionId = 7,
        PolicyVersion = 3
    };

    /// <summary>Giờ Việt Nam (ngày 10/11/2026 + offset) đổi sang UTC.</summary>
    private static DateTime Vn(int day, int hour, int minute = 0)
        => new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified).AddHours(-7);

    private static PricingResult Calc(DateTime entry, DateTime exit, PricingPolicy? policy = null,
        MonthlyPassInfo? pass = null, bool overQuota = false)
        => Engine.Calculate(new PricingRequest
        {
            VehicleTypeId = 1,
            EntryUtc = entry,
            ExitUtc = exit,
            MonthlyPass = pass,
            IsOverQuota = overQuota,
            Policy = policy ?? Policy
        });

    [Fact]
    public void Below_grace_is_free_with_grace_line()
    {
        var r = Calc(Vn(10, 10), Vn(10, 10, 10));
        Assert.Equal(0m, r.Total);
        Assert.Single(r.Lines);
        Assert.Equal(FeeLineCodes.Grace, r.Lines[0].Code);
        Assert.False(r.IsMonthlyTicket);
    }

    [Fact]
    public void Exactly_grace_is_still_free()
    {
        Assert.Equal(0m, Calc(Vn(10, 10), Vn(10, 10, 15)).Total);
    }

    [Fact]
    public void One_minute_past_grace_charges_one_block()
    {
        Assert.Equal(5000m, Calc(Vn(10, 10), Vn(10, 10, 16)).Total);
    }

    [Fact]
    public void Exactly_four_hours_is_one_block_and_a_minute_more_is_two()
    {
        Assert.Equal(5000m, Calc(Vn(10, 8), Vn(10, 12)).Total);
        Assert.Equal(10000m, Calc(Vn(10, 8), Vn(10, 12, 1)).Total);
    }

    [Fact]
    public void Multiple_blocks_are_summed()
    {
        Assert.Equal(15000m, Calc(Vn(10, 6), Vn(10, 15)).Total);
    }

    [Fact]
    public void Cycle_is_capped_at_daily_price()
    {
        Assert.Equal(25000m, Calc(Vn(10, 1), Vn(10, 22)).Total); // 21h = 6 khung = 30000, trần 25000
        Assert.Equal(25000m, Calc(Vn(10, 1), Vn(10, 21)).Total); // 20h = 5 khung = 25000
    }

    [Fact]
    public void Day_rolls_over_24_hours_after_entry_not_at_midnight()
    {
        // Vào 10:00, ra 10:00 ngày sau + 10 phút: một chu kỳ đầy (25000) + chu kỳ 10 phút (1 khung) + 1 lần qua 00:00
        var r = Calc(Vn(10, 10), Vn(11, 10, 10));
        Assert.Equal(25000m + 5000m + 2000m, r.Total);
        Assert.Equal(2, r.Lines.Count(l => l.Code == FeeLineCodes.Cycle));
    }

    [Fact]
    public void Exactly_24_hours_is_a_single_cycle()
    {
        var r = Calc(Vn(10, 10), Vn(11, 10));
        Assert.Equal(25000m + 2000m, r.Total);
        Assert.Single(r.Lines, l => l.Code == FeeLineCodes.Cycle);
    }

    [Fact]
    public void Crossing_midnight_within_one_cycle_adds_surcharge_without_splitting_cycle()
    {
        // 23:00 -> 02:00 là 3 giờ: một khung 5000 (không cắt tại 00:00) + 1 lần qua đêm
        var r = Calc(Vn(10, 23), Vn(11, 2));
        Assert.Equal(7000m, r.Total);
        Assert.Single(r.Lines, l => l.Code == FeeLineCodes.Cycle);
        Assert.Single(r.Lines, l => l.Code == FeeLineCodes.Overnight);
    }

    [Fact]
    public void Multiple_nights_are_counted_one_by_one()
    {
        // 10/11 10:00 -> 13/11 12:00 = 74h: 3 chu kỳ đầy + 2h; qua 00:00 ngày 11, 12, 13 = 3 lần
        var r = Calc(Vn(10, 10), Vn(13, 12));
        Assert.Equal(3 * 25000m + 5000m + 3 * 2000m, r.Total);
        Assert.Equal(6000m, r.Lines.Single(l => l.Code == FeeLineCodes.Overnight).Amount);
    }

    [Fact]
    public void Midnight_exactly_at_entry_or_exit_is_not_crossed()
    {
        Assert.Equal(5000m, Calc(Vn(11, 0), Vn(11, 3)).Total);
        Assert.Equal(5000m, Calc(Vn(10, 21), Vn(11, 0)).Total);
    }

    [Fact]
    public void Pass_covering_whole_stay_is_free_and_flagged_monthly()
    {
        var pass = new MonthlyPassInfo(Vn(1, 0), Vn(30, 0), MonthlyTicketStatus.Active);
        var r = Calc(Vn(10, 8), Vn(12, 20), pass: pass);
        Assert.Equal(0m, r.Total);
        Assert.True(r.IsMonthlyTicket);
        Assert.Equal(FeeLineCodes.MonthlyFree, r.Lines[0].Code);
    }

    [Fact]
    public void Pass_expiring_mid_stay_charges_only_the_part_after_expiry()
    {
        var pass = new MonthlyPassInfo(Vn(1, 0), Vn(10, 10), MonthlyTicketStatus.Active);
        var r = Calc(Vn(10, 8), Vn(10, 15), pass: pass); // 5 giờ sau hạn = 2 khung
        Assert.Equal(10000m, r.Total);
        Assert.False(r.IsMonthlyTicket);
        Assert.Contains(r.Lines, l => l.Code == FeeLineCodes.MonthlyFree);
    }

    [Fact]
    public void Pass_expiring_mid_stay_applies_grace_to_the_chargeable_part()
    {
        var pass = new MonthlyPassInfo(Vn(1, 0), Vn(10, 10), MonthlyTicketStatus.Active);
        var r = Calc(Vn(10, 8), Vn(10, 10, 10), pass: pass);
        Assert.Equal(0m, r.Total);
        Assert.Contains(r.Lines, l => l.Code == FeeLineCodes.Grace);
    }

    [Fact]
    public void Pass_expired_before_entry_is_charged_as_normal_customer()
    {
        var pass = new MonthlyPassInfo(Vn(1, 0), Vn(9, 23), MonthlyTicketStatus.Active);
        var r = Calc(Vn(10, 8), Vn(10, 15), pass: pass);
        Assert.Equal(10000m, r.Total);
        Assert.DoesNotContain(r.Lines, l => l.Code == FeeLineCodes.MonthlyFree);
    }

    [Theory]
    [InlineData(MonthlyTicketStatus.Suspended)]
    [InlineData(MonthlyTicketStatus.Cancelled)]
    [InlineData(MonthlyTicketStatus.Expired)]
    public void Pass_that_is_not_active_is_ignored(MonthlyTicketStatus status)
    {
        var pass = new MonthlyPassInfo(Vn(1, 0), Vn(30, 0), status);
        Assert.Equal(10000m, Calc(Vn(10, 8), Vn(10, 15), pass: pass).Total);
    }

    [Fact]
    public void Pass_not_started_at_entry_is_ignored()
    {
        var pass = new MonthlyPassInfo(Vn(11, 0), Vn(30, 0), MonthlyTicketStatus.Active);
        Assert.Equal(10000m, Calc(Vn(10, 8), Vn(10, 15), pass: pass).Total);
    }

    [Fact]
    public void Over_quota_vehicle_pays_daily_price_per_24h_cycle_without_grace_or_blocks()
    {
        Assert.Equal(30000m, Calc(Vn(10, 10), Vn(10, 10, 5), overQuota: true).Total);
        Assert.Equal(30000m, Calc(Vn(10, 8), Vn(10, 11), overQuota: true).Total);
    }

    [Fact]
    public void Over_quota_counts_each_24h_cycle_and_still_adds_overnight()
    {
        // 26h = 2 chu kỳ; qua 00:00 một lần
        var r = Calc(Vn(10, 10), Vn(11, 12), overQuota: true);
        Assert.Equal(2 * 30000m + 2000m, r.Total);
        Assert.Equal(2, r.Lines.Count(l => l.Code == FeeLineCodes.OverQuota));
    }

    [Fact]
    public void Over_quota_vehicle_with_valid_pass_is_still_free()
    {
        var pass = new MonthlyPassInfo(Vn(1, 0), Vn(30, 0), MonthlyTicketStatus.Active);
        var r = Calc(Vn(10, 8), Vn(11, 20), pass: pass, overQuota: true);
        Assert.Equal(0m, r.Total);
        Assert.True(r.IsMonthlyTicket);
    }

    [Fact]
    public void Same_input_gives_same_result()
    {
        var a = Calc(Vn(10, 10), Vn(13, 12));
        var b = Calc(Vn(10, 10), Vn(13, 12));
        Assert.Equal(a.Total, b.Total);
        Assert.Equal(a.Lines, b.Lines);
    }

    [Fact]
    public void Changing_policy_changes_new_calculations_only()
    {
        var before = Calc(Vn(10, 8), Vn(10, 15));
        var newPolicy = Policy with { Block4hPrice = 8000m, PolicyVersionId = 8, PolicyVersion = 4 };
        var after = Calc(Vn(10, 8), Vn(10, 15), newPolicy);

        Assert.Equal(10000m, before.Total);
        Assert.Equal(16000m, after.Total);
        Assert.Equal(7, before.PolicyVersionId);
        Assert.Equal(8, after.PolicyVersionId);
        Assert.Equal(4, after.PolicyVersion);
    }

    [Fact]
    public void Exit_before_entry_counts_one_second_and_is_free()
    {
        var r = Calc(Vn(10, 10), Vn(10, 9));
        Assert.Equal(0m, r.Total);
        Assert.Equal(TimeSpan.FromSeconds(1), r.Duration);
    }

    [Fact]
    public void Total_is_rounded_to_whole_dong_and_never_negative()
    {
        var odd = Policy with { Block4hPrice = 1000.4m, DailyPrice = 99999m, OvernightSurcharge = 0m };
        var r = Calc(Vn(10, 8), Vn(10, 10), odd);
        Assert.Equal(1000m, r.Total);
        Assert.Equal(1000.4m, r.RawFee);
        Assert.True(Calc(Vn(10, 8), Vn(10, 10), Policy with { Block4hPrice = 0m }).Total >= 0m);
    }

    [Fact]
    public void Grace_period_is_configurable()
    {
        var none = Policy with { GracePeriodMinutes = 0 };
        Assert.Equal(5000m, Calc(Vn(10, 10), Vn(10, 10, 1), none).Total);
    }
}
