using SmartPS.Models.Auth;
using SmartPS.Models.Parking;

namespace SmartPS.Models.Shifts;

public class FinancialTransaction
{
    public int TransactionId { get; set; }
    public string TransactionCode { get; set; } = string.Empty;

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public int? ParkingSessionId { get; set; }
    public ParkingSession? ParkingSession { get; set; }

    public int CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public FinancialTransactionType Type { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? ReferenceCode { get; set; }
    public string? Note { get; set; }
}
