using System.ComponentModel.DataAnnotations.Schema;
using SmartPS.Services.Localization;

namespace SmartPS.Models.Parking;

public class MonthlyTicket
{
    public int TicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty; // Mã vé tháng

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string RegisteredLicensePlate { get; set; } = string.Empty; // Biển số xe đăng ký vé tháng

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int DurationMonths { get; set; }

    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal MonthlyPrice { get; set; }
    public MonthlyTicketStatus Status { get; set; } = MonthlyTicketStatus.Active;

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsCurrentlyValid => Status == MonthlyTicketStatus.Active && DateTime.UtcNow >= StartDate && DateTime.UtcNow <= EndDate;

    [NotMapped]
    public bool IsActiveValid => Status == MonthlyTicketStatus.Active && DateTime.UtcNow <= EndDate;

    [NotMapped]
    public string StatusDisplay
    {
        get
        {
            if (Status == MonthlyTicketStatus.Cancelled) return AppText.Get("Str_Ticket_Locked");
            if (Status == MonthlyTicketStatus.Suspended) return AppText.Get("Str_Ticket_Suspended");
            if (DateTime.UtcNow > EndDate) return AppText.Get("Str_Ticket_Expired");
            if (Status == MonthlyTicketStatus.Active) return AppText.Get("Str_Ticket_Active");
            return AppText.Get("Str_Ticket_Unknown");
        }
    }
}
