// Bộ lọc lịch sử thanh toán theo nhân viên checkout, ngày và trạng thái.

using SmartPS.Models.Payment;

namespace SmartPS.DTOs.Payment;

public sealed class PaymentHistoryFilterRequest
{
    public int? CheckoutUserId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public PaymentStatus? Status { get; init; }
    public int Limit { get; init; } = 200;
}
