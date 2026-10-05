using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartPS.Services.Payment.Mock;

public class MockPaymentGateway : IPaymentGateway
{
    public const string MockProvider = "PayOS";

    public string ProviderName => MockProvider;
    public string MockChecksumKey { get; set; } = "mock-smartps-checksum-key-123456789";

    // Test flags
    public bool ShouldFailCreate { get; set; }
    public string? CreateErrorMessage { get; set; }
    public bool? ForceSignatureValid { get; set; }
    public PaymentGatewayVerifyResult? CustomVerifyResult { get; set; }

    public Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFailCreate)
        {
            return Task.FromResult(new PaymentGatewayCreateResult
            {
                Success = false,
                ErrorCode = "MOCK_ERROR",
                ErrorMessage = CreateErrorMessage ?? "Mô phỏng tạo giao dịch thất bại.",
                SanitizedRequestPayload = $"{{\"amount\":{request.Amount},\"orderCode\":{request.OrderCode}}}",
                SanitizedResponsePayload = $"{{\"error\":\"{CreateErrorMessage}\"}}"
            });
        }

        var qrCodePayload = $"00020101021238540010A000000727012600069704220112{request.OrderCode}5303704540{request.Amount:0}5802VN62190815{request.TransactionReference}6304ABCD";

        return Task.FromResult(new PaymentGatewayCreateResult
        {
            Success = true,
            GatewayTransactionId = $"mock_link_{request.OrderCode}",
            GatewayOrderCode = request.OrderCode.ToString(CultureInfo.InvariantCulture),
            QrCodePayload = qrCodePayload,
            CheckoutUrl = $"https://pay.mock.vn/checkout/{request.OrderCode}",
            AccountNumber = "999988887777",
            AccountName = "SMARTPS PARKING CORP",
            BankCode = "MB",
            SanitizedRequestPayload = $"{{\"amount\":{request.Amount},\"orderCode\":{request.OrderCode},\"reference\":\"{request.TransactionReference}\"}}",
            SanitizedResponsePayload = $"{{\"code\":\"00\",\"data\":{{\"qrCode\":\"{qrCodePayload}\"}}}}"
        });
    }

    public Task<PaymentGatewayVerifyResult> VerifyPaymentAsync(
        PaymentGatewayVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CustomVerifyResult != null)
        {
            return Task.FromResult(CustomVerifyResult);
        }

        return Task.FromResult(new PaymentGatewayVerifyResult
        {
            Success = true,
            IsPaid = true,
            Amount = request.ExpectedAmount,
            Currency = "VND",
            GatewayTransactionId = request.GatewayTransactionId ?? "mock_tx_verified"
        });
    }

    public Task<bool> VerifyWebhookSignatureAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken = default)
    {
        if (ForceSignatureValid.HasValue)
        {
            return Task.FromResult(ForceSignatureValid.Value);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Task.FromResult(false);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(false);
            }

            var actualSignature = signature;
            if (string.IsNullOrWhiteSpace(actualSignature) &&
                doc.RootElement.TryGetProperty("signature", out var sigEl))
            {
                actualSignature = sigEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(actualSignature))
            {
                return Task.FromResult(false);
            }

            var computed = ComputeDataSignature(dataEl, MockChecksumKey);
            var match = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed.ToLowerInvariant()),
                Encoding.UTF8.GetBytes(actualSignature.ToLowerInvariant()));

            return Task.FromResult(match);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public PaymentGatewayWebhookParseResult ParseWebhook(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var signature = root.TryGetProperty("signature", out var sigEl) ? sigEl.GetString() : null;
            var success = root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True;
            var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return new PaymentGatewayWebhookParseResult
                {
                    Success = false,
                    ErrorMessage = "Payload không có đối tượng data."
                };
            }

            var orderCode = ReadString(data, "orderCode");
            var description = ReadString(data, "description");
            var paymentLinkId = ReadString(data, "paymentLinkId");
            var reference = ReadString(data, "reference");
            var transactionDateTime = ReadString(data, "transactionDateTime");

            decimal amount = 0m;
            if (data.TryGetProperty("amount", out var amountEl))
            {
                if (amountEl.ValueKind == JsonValueKind.Number && amountEl.TryGetDecimal(out var amt))
                {
                    amount = amt;
                }
                else if (decimal.TryParse(amountEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    amount = parsed;
                }
            }

            var eventId = $"{paymentLinkId}|{orderCode}|{reference}|{transactionDateTime}";
            if (string.IsNullOrWhiteSpace(eventId.Replace("|", "", StringComparison.Ordinal)))
            {
                eventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            }

            var providerReportsSuccess = success || code == "00" || string.Equals(ReadString(data, "code"), "00", StringComparison.Ordinal);

            return new PaymentGatewayWebhookParseResult
            {
                Success = true,
                EventId = eventId,
                TransactionReference = description,
                GatewayOrderCode = orderCode,
                GatewayTransactionId = paymentLinkId,
                Amount = amount,
                Currency = ReadString(data, "currency") ?? "VND",
                Signature = signature,
                ProviderReportsSuccess = providerReportsSuccess
            };
        }
        catch (Exception ex)
        {
            return new PaymentGatewayWebhookParseResult
            {
                Success = false,
                ErrorMessage = $"Lỗi phân tích payload: {ex.Message}",
                EventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty)))
            };
        }
    }

    public string CreateSignedWebhookPayload(
        long orderCode,
        decimal amount,
        string description,
        string? paymentLinkId = null,
        string? reference = null,
        bool corruptSignature = false,
        bool simulateProviderFailure = false)
    {
        var linkId = paymentLinkId ?? $"link_{orderCode}";
        var refCode = reference ?? $"REF_{orderCode}";
        var dt = "2026-09-28 14:30:00";

        var dataDict = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["accountNumber"] = "999988887777",
            ["amount"] = (int)Math.Round(amount, 0),
            ["description"] = description,
            ["orderCode"] = orderCode,
            ["paymentLinkId"] = linkId,
            ["reference"] = refCode,
            ["transactionDateTime"] = dt
        };

        var canonical = string.Join("&", dataDict.Select(p => $"{p.Key}={p.Value}"));
        var signature = ComputeHmac(canonical, MockChecksumKey);
        if (corruptSignature)
        {
            signature = "invalid_signature_mock_corrupted_123456";
        }

        var root = new Dictionary<string, object?>
        {
            ["code"] = simulateProviderFailure ? "01" : "00",
            ["desc"] = simulateProviderFailure ? "Transaction failed at bank" : "success",
            ["success"] = !simulateProviderFailure,
            ["data"] = dataDict,
            ["signature"] = signature
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    }

    private static string ComputeDataSignature(JsonElement data, string key)
    {
        var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in data.EnumerateObject())
        {
            pairs[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText()
            };
        }

        var canonical = string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));
        return ComputeHmac(canonical, key);
    }

    private static string ComputeHmac(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? ReadString(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => el.ToString()
        };
    }
}
