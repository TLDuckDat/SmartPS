using SmartPS.Models.Auth;

namespace SmartPS.Models.Parking;

public class ParkingSession
{
    public int SessionId { get; set; }
    public string TicketCode { get; set; } = string.Empty; // Mã thẻ hoặc chuỗi sinh QR
    public string LicensePlate { get; set; } = string.Empty; // Biển số xe

    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    public int? SlotId { get; set; }
    public ParkingSlot? Slot { get; set; }

    public DateTime CheckInTime { get; set; } = DateTime.UtcNow;
    public DateTime? CheckOutTime { get; set; }

    public string? CheckInImagePath { get; set; }
    public string? CheckOutImagePath { get; set; }

    public decimal TotalFee { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public bool IsMonthlyPass { get; set; } = false;
    public CustomerType CustomerType { get; set; } = CustomerType.Regular;

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
