namespace SmartPS.Services.Payment;

public class PaymentGatewayCreateRequest
{
    public string TransactionReference { get; set; } = string.Empty;
    public long OrderCode { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Description { get; set; } = string.Empty;
    public string? BuyerName { get; set; }
}

public class PaymentGatewayCreateResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? GatewayTransactionId { get; set; }
    public string? GatewayOrderCode { get; set; }
    public string? QrCodePayload { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountName { get; set; }
    public string? BankCode { get; set; }
    public string? SanitizedRequestPayload { get; set; }
    public string? SanitizedResponsePayload { get; set; }
}

public class PaymentGatewayVerifyRequest
{
    public string TransactionReference { get; set; } = string.Empty;

    public decimal ExpectedAmount { get; set; }

    public string? GatewayTransactionId { get; set; }

    public string? GatewayOrderCode { get; set; }
}

public class PaymentGatewayVerifyResult
{
    public bool Success { get; set; }
    public bool IsPaid { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? GatewayTransactionId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentGatewayWebhookParseResult
{
    public bool Success { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public string? GatewayOrderCode { get; set; }
    public string? GatewayTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string? Signature { get; set; }
    public bool ProviderReportsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IPaymentGateway
{
    string ProviderName { get; }

    Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentGatewayVerifyResult> VerifyPaymentAsync(
        PaymentGatewayVerifyRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyWebhookSignatureAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken = default);

    PaymentGatewayWebhookParseResult ParseWebhook(string payload);
}
