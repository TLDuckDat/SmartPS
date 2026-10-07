using SmartPS.Models.Parking;

namespace SmartPS.Models.GateControl;

public class GateCheckOutCalculationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ParkingSession? ActiveSession { get; set; }
    public DateTime CheckOutTime { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public decimal RawFee { get; set; }
    public double DiscountPercentage { get; set; }
    public decimal TotalFee { get; set; }
    public bool IsMonthlyTicket { get; set; }
    public string? CustomerName { get; set; }

    // Vé tháng hết hạn trong lúc gửi (R15) và cảnh báo danh sách đen khi ra (R16)
    public bool TicketExpiredDuringStay { get; set; }
    public bool TicketNoLongerValid { get; set; }
    public DateTime? TicketValidUntilUtc { get; set; }
    public DateTime? ChargeFromUtc { get; set; }
    public bool IsBlacklisted { get; set; }
    public string? BlacklistReason { get; set; }
    public int? BlacklistEntryId { get; set; }
    public string DurationFormatted => $"{(int)Duration.TotalHours}h {Duration.Minutes}m {Duration.Seconds}s";
}
