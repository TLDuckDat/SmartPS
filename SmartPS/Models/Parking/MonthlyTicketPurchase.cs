namespace SmartPS.Models.Parking;

/// <summary>Một kỳ vé tháng đã thanh toán (mua mới hoặc gia hạn). Chỉ ghi thêm ở tầng ứng dụng.</summary>
public class MonthlyTicketPurchase
{
    public long MonthlyTicketPurchaseId { get; set; }
    public int TicketId { get; set; }
    public MonthlyTicket? Ticket { get; set; }
    public TicketPurchaseKind Kind { get; set; }
    public int? PlanId { get; set; }
    public MonthlyTicketPlan? Plan { get; set; }
    public decimal Price { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }
    public SmartPS.Models.Auth.User? CreatedByUser { get; set; }
}
