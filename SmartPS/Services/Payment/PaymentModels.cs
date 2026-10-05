using System.Security.Cryptography;
using QRCoder;
using SmartPS.Models.Parking;
using SmartPS.Models.Payment;

namespace SmartPS.Services.Payment;

public class PaymentCreationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public int PaymentTransactionId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentStatus Status { get; set; }
    public string? QrCodePayload { get; set; }
    public byte[]? QrImagePng { get; set; }
    public string? CheckoutUrl { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PaymentStatusResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public PaymentStatus Status { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool CheckoutCompleted { get; set; }
}

public class WebhookProcessResult
{
    public bool Accepted { get; set; }
    public bool IsDuplicate { get; set; }
    public bool PaymentPaid { get; set; }
    public bool CheckoutCompleted { get; set; }
    public int HttpStatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
}

public class PaymentHistoryItem
{
    public int PaymentId { get; set; }
    public int SessionId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string Gateway { get; set; } = string.Empty;
}

public class PaymentDetailsResult
{
    public PaymentHistoryItem Payment { get; set; } = new();
    public IReadOnlyList<PaymentAttempt> Attempts { get; set; } = Array.Empty<PaymentAttempt>();
    public IReadOnlyList<PaymentWebhook> Webhooks { get; set; } = Array.Empty<PaymentWebhook>();
    public string? QrCodePayload { get; set; }
    public string? GatewayTransactionId { get; set; }
}

public class CreatePaymentRequest
{
    public int SessionId { get; set; }
    public int ActorUserId { get; set; }
    public string? CheckoutImagePath { get; set; }
}

public static class TransactionReferenceGenerator
{
    public static string Create(int sessionId)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var suffix = RandomNumberGenerator.GetInt32(100, 1000);
        return $"SP{stamp}{sessionId % 10000:D4}{suffix:D3}";
    }

    public static long CreateOrderCode()
    {
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rand = RandomNumberGenerator.GetInt32(100, 1000);
        return seconds * 1000 + rand;
    }

    public static byte[]? CreateQrPng(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(8);
    }
}
