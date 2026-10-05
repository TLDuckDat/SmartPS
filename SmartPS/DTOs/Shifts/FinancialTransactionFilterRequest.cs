using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.DTOs.Shifts;

public class FinancialTransactionFilterRequest
{
    public int? ShiftId { get; set; }
    public int? CreatedByUserId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public FinancialTransactionType? Type { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Limit { get; set; } = 500;
}
