namespace SmartPS.Models.Payment;

public class PaymentAttempt
{
    public int PaymentAttemptId { get; set; }
    public int PaymentTransactionId { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }

    public int AttemptNumber { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Started;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
