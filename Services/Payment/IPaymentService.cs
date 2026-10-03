using SmartPS.Models.Payment;

namespace SmartPS.Services.Payment;

public interface IPaymentService
{
    Task<PaymentCreationResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> GetPaymentStatusAsync(
        int paymentId,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> RefreshFromGatewayAsync(
        int paymentId,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> CancelPaymentAsync(
        int paymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác nhận hoàn tiền thủ công cho một giao dịch đã thanh toán.
    /// Việc chuyển tiền thực tế phải được thực hiện qua quy trình hoàn tiền của nhà cung cấp.
    /// </summary>
    Task<PaymentStatusResult> ConfirmManualRefundAsync(
        int paymentId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawPayload,
        string? signature,
        string provider,
        CancellationToken cancellationToken = default);

    Task<List<PaymentHistoryItem>> GetPaymentHistoryAsync(
        CancellationToken cancellationToken = default);

    Task<PaymentDetailsResult?> GetPaymentDetailsAsync(
        int paymentId,
        CancellationToken cancellationToken = default);
}
