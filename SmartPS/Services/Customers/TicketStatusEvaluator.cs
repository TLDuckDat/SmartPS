using SmartPS.Models.Parking;

namespace SmartPS.Services.Customers;

/// <summary>Trạng thái hiển thị của vé tháng, tính theo thời gian (không cần job đổi trạng thái Expired).</summary>
public static class TicketStatusEvaluator
{
    public static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(7);

    public static TicketDisplayStatus Evaluate(MonthlyTicketStatus status, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        if (status == MonthlyTicketStatus.Suspended)
        {
            return TicketDisplayStatus.Suspended;
        }

        if (status == MonthlyTicketStatus.Expired || endUtc <= nowUtc)
        {
            return TicketDisplayStatus.Expired;
        }

        if (startUtc > nowUtc)
        {
            return TicketDisplayStatus.NotStarted;
        }

        return endUtc - nowUtc <= ExpiringSoonWindow ? TicketDisplayStatus.ExpiringSoon : TicketDisplayStatus.Active;
    }

    /// <summary>Vé đại diện của khách: ưu tiên vé đang hiệu lực, rồi chưa bắt đầu, tạm ngưng, hết hạn; trong cùng nhóm lấy vé kết thúc muộn nhất.</summary>
    public static MonthlyTicketDto? PickPrimary(IEnumerable<MonthlyTicketDto> tickets)
    {
        return tickets
            .OrderBy(t => Rank(t.DisplayStatus))
            .ThenByDescending(t => t.EndDateUtc)
            .ThenByDescending(t => t.TicketId)
            .FirstOrDefault();
    }

    private static int Rank(TicketDisplayStatus status) => status switch
    {
        TicketDisplayStatus.Active or TicketDisplayStatus.ExpiringSoon => 0,
        TicketDisplayStatus.NotStarted => 1,
        TicketDisplayStatus.Suspended => 2,
        _ => 3
    };
}
