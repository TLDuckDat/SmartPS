namespace SmartPS.Models.Payment;

public class PaymentWebhook
{
    public int PaymentWebhookId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public int? PaymentTransactionId { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }

    public string Payload { get; set; } = string.Empty;
    public string? Signature { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public bool IsValid { get; set; }
    public bool IsProcessed { get; set; }
    public bool IsDuplicate { get; set; }
    public string? ErrorMessage { get; set; }
}
