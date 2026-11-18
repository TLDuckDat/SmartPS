namespace SmartPS.Models.Parking;

public enum TicketHistoryAction
{
    Issue = 1,       // Cấp mới vé tháng
    Renew = 2,       // Gia hạn vé tháng
    ChangePlate = 3, // Đổi biển số / phương tiện
    Lock = 4,        // Khóa vé (Suspended)
    Unlock = 5,      // Mở khóa vé (Active)
    Cancel = 6       // Hủy vé tháng (Cancelled)
}

public class MonthlyTicketHistory
{
    public int HistoryId { get; set; }
    public int TicketId { get; set; }
    public TicketHistoryAction Action { get; set; }

    // Phương tiện cũ và mới khi đổi xe / biển số
    public int? OldVehicleId { get; set; }
    public int? NewVehicleId { get; set; }
    public string? OldLicensePlate { get; set; }
    public string? NewLicensePlate { get; set; }

    // Thời hạn cũ và mới khi gia hạn
    public DateTime? OldEndDate { get; set; }
    public DateTime? NewEndDate { get; set; }
    public int? AddedMonths { get; set; }

    // Tài chính (nếu có thu tiền trong hành động này)
    public decimal Amount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }

    // Ghi chú, lý do và người thực hiện
    public string? Reason { get; set; }
    public int? PerformedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Property
    public MonthlyTicket Ticket { get; set; } = null!;
}
