namespace SmartPS.Models.Payment;

public class PaymentTransaction
{
    public int PaymentTransactionId { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public string TransactionReference { get; set; } = string.Empty;
    public string? GatewayTransactionId { get; set; }
    public string? GatewayOrderCode { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Created;

    public string? QrCodePayload { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountName { get; set; }
    public string? BankCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<PaymentAttempt> Attempts { get; set; } = new List<PaymentAttempt>();
    public ICollection<PaymentWebhook> Webhooks { get; set; } = new List<PaymentWebhook>();
}
