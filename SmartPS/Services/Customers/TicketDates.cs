using SmartPS.Models.Parking;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Customers;

/// <summary>
/// Mốc thời gian vé tháng: ranh giới là 00:00 giờ Việt Nam (UTC+7) lưu dưới dạng UTC, hiệu lực theo khoảng nửa mở [Start, End).
/// </summary>
public static class TicketDates
{
    public static DateOnly TodayVn(DateTime nowUtc)
        => DateOnly.FromDateTime(AuditTime.ToVietnamTime(nowUtc));

    public static DateTime StartOfVnDayUtc(DateOnly vnDate)
        => AuditTime.VietnamDateStartUtc(vnDate);

    public static (DateTime StartUtc, DateTime EndUtc) ForNewTicket(DateOnly startVn, int durationMonths)
        => (StartOfVnDayUtc(startVn), StartOfVnDayUtc(startVn.AddMonths(durationMonths)));

    /// <summary>
    /// Vé đã hết hạn (trạng thái Expired hoặc EndDate &lt;= hiện tại) bắt đầu lại từ hôm nay (giờ VN);
    /// còn hạn thì giữ ngày bắt đầu và nối tiếp từ ngày kết thúc cũ.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) ForRenewal(
        DateTime currentStartUtc,
        DateTime currentEndUtc,
        MonthlyTicketStatus status,
        int durationMonths,
        DateTime nowUtc)
    {
        if (status == MonthlyTicketStatus.Expired || currentEndUtc <= nowUtc)
        {
            return ForNewTicket(TodayVn(nowUtc), durationMonths);
        }

        var endVn = AuditTime.ToVietnamTime(currentEndUtc).AddMonths(durationMonths);
        return (currentStartUtc, DateTime.SpecifyKind(endVn - AuditTime.VietnamOffset, DateTimeKind.Utc));
    }

    /// <summary>Kỳ thanh toán của lần gia hạn: hết hạn thì [ngày bắt đầu mới, ngày kết thúc mới), còn hạn thì [kết thúc cũ, kết thúc mới).</summary>
    public static (DateTime StartUtc, DateTime EndUtc) RenewalPurchasePeriod(
        DateTime currentEndUtc,
        bool wasExpired,
        DateTime newStartUtc,
        DateTime newEndUtc)
        => wasExpired ? (newStartUtc, newEndUtc) : (currentEndUtc, newEndUtc);

    /// <summary>Ngày (giờ VN) cuối cùng vé còn hiệu lực, vì ngày kết thúc là giới hạn loại trừ.</summary>
    public static DateOnly LastValidDateVn(DateTime endUtc)
        => DateOnly.FromDateTime(AuditTime.ToVietnamTime(endUtc).AddTicks(-1));

    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
        => aStart < bEnd && bStart < aEnd;
}
