using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace SmartPS.Services.Payment.PayOS;

public class PayOSPaymentGateway : IPaymentGateway
{
    public const string Provider = "PayOS";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayOSPaymentGatewayOptions _options;

    public PayOSPaymentGateway(IHttpClientFactory httpClientFactory, IOptions<PayOSPaymentGatewayOptions> options)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string ProviderName => Provider;

    public async Task<PaymentGatewayCreateResult> CreatePaymentAsync(
        PaymentGatewayCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ChecksumKey))
        {
            return new PaymentGatewayCreateResult
            {
                Success = false,
                ErrorCode = "CONFIG",
                ErrorMessage = "Thiếu cấu hình PayOS (ClientId/ApiKey/ChecksumKey)."
            };
        }

        var amount = (int)Math.Round(request.Amount, 0, MidpointRounding.AwayFromZero);
        var description = TruncateDescription(request.Description, request.TransactionReference);
        var signature = CreatePaymentRequestSignature(amount, _options.CancelUrl, description, request.OrderCode, _options.ReturnUrl);

        var body = new Dictionary<string, object?>
        {
            ["orderCode"] = request.OrderCode,
            ["amount"] = amount,
            ["description"] = description,
            ["returnUrl"] = _options.ReturnUrl,
            ["cancelUrl"] = _options.CancelUrl,
            ["signature"] = signature
        };

        var json = JsonSerializer.Serialize(body);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, CombineUrl(_options.ApiBaseUrl, "/v2/payment-requests"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(httpRequest);

        try
        {
            var client = _httpClientFactory.CreateClient("PayOS");
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PaymentGatewayCreateResult
                {
                    Success = false,
                    ErrorCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                    ErrorMessage = "PayOS từ chối tạo yêu cầu thanh toán.",
                    SanitizedRequestPayload = json,
                    SanitizedResponsePayload = PaymentPayloadSanitizer.Redact(responseText)
                };
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
            if (code != "00")
            {
                var desc = root.TryGetProperty("desc", out var descEl) ? descEl.GetString() : "PayOS error";
                return new PaymentGatewayCreateResult
                {
                    Success = false,
                    ErrorCode = code,
                    ErrorMessage = desc,
                    SanitizedRequestPayload = json,
                    SanitizedResponsePayload = PaymentPayloadSanitizer.Redact(responseText)
                };
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return new PaymentGatewayCreateResult
                {
                    Success = false,
                    ErrorCode = "PARSE",
                    ErrorMessage = "PayOS không trả về dữ liệu thanh toán.",
                    SanitizedRequestPayload = json,
                    SanitizedResponsePayload = PaymentPayloadSanitizer.Redact(responseText)
                };
            }

            return new PaymentGatewayCreateResult
            {
                Success = true,
                GatewayTransactionId = ReadString(data, "paymentLinkId"),
                GatewayOrderCode = request.OrderCode.ToString(CultureInfo.InvariantCulture),
                QrCodePayload = ReadString(data, "qrCode"),
                CheckoutUrl = ReadString(data, "checkoutUrl"),
                AccountNumber = ReadString(data, "accountNumber"),
                AccountName = ReadString(data, "accountName"),
                SanitizedRequestPayload = json,
                SanitizedResponsePayload = PaymentPayloadSanitizer.Redact(responseText)
            };
        }
        catch (Exception ex)
        {
            PaymentLog.Event("PayOSCreateError", ex.Message);
            return new PaymentGatewayCreateResult
            {
                Success = false,
                ErrorCode = "NETWORK",
                ErrorMessage = "Không gọi được cổng thanh toán PayOS.",
                SanitizedRequestPayload = json
            };
        }
    }

    public async Task<PaymentGatewayVerifyResult> VerifyPaymentAsync(
        PaymentGatewayVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        var id = request.GatewayTransactionId;
        if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(request.GatewayOrderCode))
        {
            id = request.GatewayOrderCode;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return new PaymentGatewayVerifyResult { Success = false, ErrorMessage = "Thiếu mã giao dịch PayOS để đối soát." };
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new PaymentGatewayVerifyResult { Success = false, ErrorMessage = "Thiếu cấu hình PayOS." };
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Get, CombineUrl(_options.ApiBaseUrl, $"/v2/payment-requests/{id}"));
        AddAuthHeaders(httpRequest);

        try
        {
            var client = _httpClientFactory.CreateClient("PayOS");
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PaymentGatewayVerifyResult { Success = false, ErrorMessage = "Không truy vấn được PayOS." };
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data))
            {
                return new PaymentGatewayVerifyResult { Success = false, ErrorMessage = "PayOS không trả về dữ liệu." };
            }

            var status = ReadString(data, "status");
            var amount = ReadDecimal(data, "amountPaid") ?? ReadDecimal(data, "amount");
            var paid = string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase);
            var amountMatches = amount.HasValue &&
                                Math.Round(amount.Value, 0) == Math.Round(request.ExpectedAmount, 0);

            return new PaymentGatewayVerifyResult
            {
                Success = true,
                IsPaid = paid && amountMatches,
                Amount = amount,
                Currency = "VND",
                GatewayTransactionId = ReadString(data, "id") ?? id,
                ErrorMessage = paid && !amountMatches
                    ? $"Số tiền PayOS trả về không khớp. Expected={request.ExpectedAmount}, Actual={amount}."
                    : null
            };
        }
        catch (Exception)
        {
            return new PaymentGatewayVerifyResult { Success = false, ErrorMessage = "Lỗi mạng khi đối soát PayOS." };
        }
    }

    public Task<bool> VerifyWebhookSignatureAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.ChecksumKey) || string.IsNullOrWhiteSpace(payload))
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

            var bodySignature = signature;
            if (string.IsNullOrWhiteSpace(bodySignature) &&
                doc.RootElement.TryGetProperty("signature", out var sigEl))
            {
                bodySignature = sigEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(bodySignature))
            {
                return Task.FromResult(false);
            }

            var computed = CreateSignatureFromObject(dataEl);
            var valid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed.ToLowerInvariant()),
                Encoding.UTF8.GetBytes(bodySignature.ToLowerInvariant()));
            return Task.FromResult(valid);
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
                return new PaymentGatewayWebhookParseResult { Success = false, ErrorMessage = "Webhook thiếu trường data." };
            }

            var orderCode = ReadString(data, "orderCode");
            var description = ReadString(data, "description");
            var paymentLinkId = ReadString(data, "paymentLinkId");
            var reference = ReadString(data, "reference");
            var transactionDateTime = ReadString(data, "transactionDateTime");
            var amount = 0m;
            if (data.TryGetProperty("amount", out var amountEl))
            {
                if (amountEl.ValueKind == JsonValueKind.Number && amountEl.TryGetDecimal(out var dec))
                {
                    amount = dec;
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

            var providerOk = success || code == "00" || string.Equals(ReadString(data, "code"), "00", StringComparison.Ordinal);

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
                ProviderReportsSuccess = providerOk
            };
        }
        catch (Exception ex)
        {
            PaymentLog.Event("PayOSParseError", ex.Message);
            return new PaymentGatewayWebhookParseResult
            {
                Success = false,
                ErrorMessage = "Payload webhook không hợp lệ.",
                EventId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty)))
            };
        }
    }

    internal string CreatePaymentRequestSignature(int amount, string cancelUrl, string description, long orderCode, string returnUrl)
    {
        var data = $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
        return ComputeHmac(data, _options.ChecksumKey);
    }

    internal string CreateSignatureFromObject(JsonElement data)
    {
        var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in data.EnumerateObject())
        {
            pairs[prop.Name] = FormatWebhookValue(prop.Value);
        }

        var canonical = string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));
        return ComputeHmac(canonical, _options.ChecksumKey);
    }

    private static string FormatWebhookValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => JsonSerializer.Serialize(SortArray(value)),
            JsonValueKind.Object => JsonSerializer.Serialize(SortObject(value)),
            _ => value.GetRawText()
        };
    }

    private static JsonNode? SortObject(JsonElement element)
    {
        var obj = new JsonObject();
        foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }
        return obj;
    }

    private static JsonArray SortArray(JsonElement element)
    {
        var array = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            array.Add(item.ValueKind == JsonValueKind.Object ? SortObject(item) : JsonNode.Parse(item.GetRawText()));
        }
        return array;
    }

    private static string ComputeHmac(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("x-client-id", _options.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private static string TruncateDescription(string description, string transactionReference)
    {
        var text = string.IsNullOrWhiteSpace(description) ? transactionReference : description;
        text = new string(text.Where(char.IsLetterOrDigit).ToArray());
        if (text.Length <= 25)
        {
            return text;
        }

        return text[..25];
    }

    private static decimal? ReadDecimal(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(el.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.Null => null,
            _ => el.ToString()
        };
    }
}


public class PayOSPaymentGatewayOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = "https://localhost/payos/return";
    public string CancelUrl { get; set; } = "https://localhost/payos/cancel";
    public string ApiBaseUrl { get; set; } = "https://api-merchant.payos.vn";
}
