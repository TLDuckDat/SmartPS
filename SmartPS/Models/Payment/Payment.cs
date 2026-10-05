using SmartPS.Models.Parking;

namespace SmartPS.Models.Payment;

public class Payment
{
    public int PaymentId { get; set; }
    public int SessionId { get; set; }
    public int? ShiftId { get; set; }
    public int? CheckoutUserId { get; set; }
    public ParkingSession? Session { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.VietQR;
    public string Description { get; set; } = string.Empty;
    public string? CheckoutImagePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? ExpiredAt { get; set; }

    public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
}
