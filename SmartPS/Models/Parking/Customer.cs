namespace SmartPS.Models.Parking;

public class Customer
{
    public int CustomerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? IdentityCard { get; set; } // CCCD / CMND
    public string DefaultLicensePlate { get; set; } = string.Empty; // Biển số xe mặc định
    public CustomerType Type { get; set; } = CustomerType.Regular; // Khách lạ / Thân quen / VIP
    public int? VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public ICollection<MonthlyTicket> MonthlyTickets { get; set; } = new List<MonthlyTicket>();
    public ICollection<ParkingSession> ParkingSessions { get; set; } = new List<ParkingSession>();
}
