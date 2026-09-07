namespace SmartPS.Models.Parking;

public class MonthlyTicket
{
    public int TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty; // Mã vé tháng (e.g. MT-202609-001)

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string RegisteredLicensePlate { get; set; } = string.Empty; // Biển số xe đăng ký vé tháng

    public int? PlanId { get; set; }
    public MonthlyTicketPlan? Plan { get; set; }

    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal MonthlyPrice { get; set; }
    public MonthlyTicketStatus Status { get; set; } = MonthlyTicketStatus.Active;

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsCurrentlyValid => Status == MonthlyTicketStatus.Active && DateTime.UtcNow >= StartDate && DateTime.UtcNow <= EndDate;
}
