using SmartPS.Models.Parking;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Pricing;

/// <summary>
/// Công thức cước (hàm thuần):
/// 1. Thời lượng tính phí = ra - vào (âm thì 1 giây); không quá thời gian ân hạn thì miễn phí.
/// 2. Chia thành các chu kỳ 24h liên tiếp kể từ lúc vào; mỗi chu kỳ = min(ceil(giờ / BlockHours) x Block4hPrice, DailyPrice).
/// 3. Mỗi lần khoảng gửi đi qua mốc 00:00 giờ Việt Nam cộng một lần phụ phí qua đêm.
/// 4. Vé tháng còn hạn miễn phí phần trong hạn; phần sau khi hết hạn tính như khách thường.
/// 5. Xe vượt định mức: mỗi chu kỳ 24h tính giá vượt định mức (không ân hạn, không khung), vẫn cộng phụ phí qua đêm.
/// 6. Tổng làm tròn Math.Round(..., 0), không âm.
/// </summary>
public sealed class PricingEngine : IPricingEngine
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan CycleLength = TimeSpan.FromHours(24);

    public PricingResult Calculate(PricingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var policy = request.Policy;
        var entry = request.EntryUtc;
        var exit = request.ExitUtc;
        var duration = exit - entry;
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromSeconds(1);
            exit = entry + duration;
        }

        var lines = new List<FeeLine>();

        // Phần được vé tháng bao phủ: [entry, passEnd]. Vé phải còn hiệu lực lúc vào.
        var chargeStart = entry;
        if (IsValidAtEntry(request.MonthlyPass, entry, out var passEnd))
        {
            if (exit <= passEnd)
            {
                lines.Add(new FeeLine(FeeLineCodes.MonthlyFree,
                    AppText.Get("Str_Fee_Line_MonthlyFree", FormatDuration(duration)), 0m));
                return Build(0m, lines, policy, duration, isMonthly: true);
            }

            chargeStart = passEnd;
            lines.Add(new FeeLine(FeeLineCodes.MonthlyFree,
                AppText.Get("Str_Fee_Line_MonthlyFree", FormatDuration(passEnd - entry)), 0m));
        }

        var chargeable = exit - chargeStart;
        decimal raw = 0m;

        if (request.IsOverQuota)
        {
            AddCycles(lines, chargeable, ref raw, (cycleIndex, length) => (
                FeeLineCodes.OverQuota,
                AppText.Get("Str_Fee_Line_OverQuota", cycleIndex, FormatHours(length), policy.OverQuotaDailyPrice),
                policy.OverQuotaDailyPrice));
        }
        else if (chargeable <= TimeSpan.FromMinutes(Math.Max(0, policy.GracePeriodMinutes)))
        {
            lines.Add(new FeeLine(FeeLineCodes.Grace, AppText.Get("Str_Fee_Line_Grace", policy.GracePeriodMinutes), 0m));
            return Build(0m, lines, policy, duration, isMonthly: false);
        }
        else
        {
            var blockTicks = TimeSpan.FromHours(Math.Max(1, policy.BlockHours)).Ticks;
            AddCycles(lines, chargeable, ref raw, (cycleIndex, length) =>
            {
                var blocks = (length.Ticks + blockTicks - 1) / blockTicks;
                var byBlocks = blocks * policy.Block4hPrice;
                var capped = byBlocks > policy.DailyPrice;
                var amount = capped ? policy.DailyPrice : byBlocks;
                var text = capped
                    ? AppText.Get("Str_Fee_Line_CycleCapped", cycleIndex, FormatHours(length), blocks, policy.Block4hPrice, amount)
                    : AppText.Get("Str_Fee_Line_Cycle", cycleIndex, FormatHours(length), blocks, policy.Block4hPrice, amount);
                return (FeeLineCodes.Cycle, text, amount);
            });
        }

        var nights = CountMidnights(chargeStart, exit);
        if (nights > 0 && policy.OvernightSurcharge > 0)
        {
            var surcharge = nights * policy.OvernightSurcharge;
            raw += surcharge;
            lines.Add(new FeeLine(FeeLineCodes.Overnight,
                AppText.Get("Str_Fee_Line_Overnight", nights, policy.OvernightSurcharge, surcharge), surcharge));
        }

        return Build(raw, lines, policy, duration, isMonthly: false);
    }

    private static void AddCycles(List<FeeLine> lines, TimeSpan chargeable, ref decimal raw,
        Func<int, TimeSpan, (string Code, string Text, decimal Amount)> price)
    {
        var remaining = chargeable;
        var index = 1;
        while (remaining > TimeSpan.Zero)
        {
            var length = remaining < CycleLength ? remaining : CycleLength;
            var (code, text, amount) = price(index, length);
            raw += amount;
            lines.Add(new FeeLine(code, text, amount));
            remaining -= length;
            index++;
        }
    }

    private static bool IsValidAtEntry(MonthlyPassInfo? pass, DateTime entry, out DateTime passEnd)
    {
        passEnd = default;
        if (pass is null || pass.Status != MonthlyTicketStatus.Active || pass.EndDateUtc is null)
        {
            return false;
        }

        if (pass.StartDateUtc is { } start && entry < start)
        {
            return false;
        }

        if (entry > pass.EndDateUtc.Value)
        {
            return false;
        }

        passEnd = pass.EndDateUtc.Value;
        return true;
    }

    /// <summary>Số mốc 00:00 giờ Việt Nam nằm trong khoảng mở (start, end).</summary>
    internal static int CountMidnights(DateTime startUtc, DateTime endUtc)
    {
        var vnStart = startUtc + VietnamOffset;
        var vnEnd = endUtc + VietnamOffset;
        var firstMidnight = vnStart.Date.AddDays(1);
        if (firstMidnight >= vnEnd)
        {
            return 0;
        }

        var ticks = (vnEnd - firstMidnight).Ticks;
        return (int)((ticks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay);
    }

    private static PricingResult Build(decimal raw, List<FeeLine> lines, PricingPolicy policy, TimeSpan duration, bool isMonthly)
        => new()
        {
            RawFee = raw,
            Total = Math.Max(0m, Math.Round(raw, 0)),
            Lines = lines,
            PolicyVersionId = policy.PolicyVersionId,
            PolicyVersion = policy.PolicyVersion,
            IsMonthlyTicket = isMonthly,
            Duration = duration
        };

    private static string FormatHours(TimeSpan length) => $"{length.TotalHours:0.##}h";

    internal static string FormatDuration(TimeSpan duration)
    {
        var days = (int)Math.Floor(duration.TotalDays);
        var text = string.Empty;
        if (days > 0) text += AppText.Get("Str_Fee_Days", days);
        if (duration.Hours > 0) text += AppText.Get("Str_Fee_Hours", duration.Hours);
        if (duration.Minutes > 0 || text == string.Empty) text += AppText.Get("Str_Fee_Minutes", duration.Minutes);
        return text.Trim();
    }
}
