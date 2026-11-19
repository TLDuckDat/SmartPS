using System.Globalization;
using System.Text;
using System.Text.Json;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Audit;

/// <summary>
/// Chuyển dữ liệu thô của nhật ký (mã hành động, kết quả, loại đối tượng, chi tiết JSON)
/// thành chữ dễ hiểu cho người dùng cuối. Mã gốc vẫn được giữ nguyên trong cơ sở dữ liệu.
/// </summary>
public static class AuditDisplayText
{
    /// <summary>Trường nội bộ không có ý nghĩa với người dùng: ẩn ở bản dành cho khách hàng.</summary>
    private static readonly HashSet<string> TechnicalFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "checkInImage", "checkOutImage", "dbAudit", "normalizedPlate", "timestamp", "source", "byOwner",
        "versionId", "roleId", "vehicleTypeId", "ownerCustomerId", "customerId", "vehicleId",
        "firstInvalidAuditLogId", "error", "requiredPermissions", "occurredAtUtc", "entityId", "entityType"
    };

    /// <summary>Trường chứa số tiền: hiển thị dạng "15.000 đ".</summary>
    private static readonly HashSet<string> MoneyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "actualCash", "amount", "beginningCash", "block4hPrice", "dailyPrice", "difference", "expectedCash", "fee",
        "monthly1Price", "monthly3Price", "monthly6Price", "monthlyPrice", "overQuotaDailyPrice", "overnightSurcharge", "totalFee"
    };

    public static string Action(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return string.Empty;
        return Lookup($"Str_AuditAction_{action}") ?? action;
    }

    public static string Outcome(object? outcome)
    {
        var text = outcome?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Lookup($"Str_Audit_Outcome_{text}") ?? text;
    }

    public static string Entity(string? entityType, string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType)) return entityId ?? string.Empty;
        var name = Lookup($"Str_AuditEntity_{entityType}") ?? entityType;
        return string.IsNullOrWhiteSpace(entityId) ? name : $"{name} {entityId}";
    }

    /// <summary>Chi tiết JSON thành các dòng "Tên mục: giá trị". Chế độ nhà phát triển giữ nguyên JSON.</summary>
    public static string Details(string? json, bool developerMode)
    {
        if (string.IsNullOrWhiteSpace(json)) return Lookup("Str_Audit_NoDetails") ?? string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (developerMode)
            {
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }

            var sb = new StringBuilder();
            AppendElement(sb, doc.RootElement, null, 0);
            var text = sb.ToString().TrimEnd();
            return text.Length == 0 ? Lookup("Str_Audit_NoDetails") ?? string.Empty : text;
        }
        catch (JsonException)
        {
            return developerMode ? json : Lookup("Str_Audit_NoDetails") ?? string.Empty;
        }
    }

    private static void AppendElement(StringBuilder sb, JsonElement element, string? name, int depth)
    {
        var indent = new string(' ', depth * 4);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (name != null)
                {
                    sb.Append(indent).Append(FieldLabel(name)).AppendLine(":");
                    depth++;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TechnicalFields.Contains(property.Name)) continue;
                    AppendElement(sb, property.Value, property.Name, depth);
                }
                break;

            case JsonValueKind.Array:
                var values = element.EnumerateArray().ToList();
                if (values.All(v => v.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array))
                {
                    var joined = values.Count == 0
                        ? Lookup("Str_AuditValue_None") ?? "-"
                        : string.Join(", ", values.Select(v => Value(name, v)));
                    sb.Append(indent).Append(name == null ? string.Empty : FieldLabel(name) + ": ").AppendLine(joined);
                }
                else
                {
                    if (name != null) sb.Append(indent).Append(FieldLabel(name)).AppendLine(":");
                    var index = 1;
                    foreach (var item in values)
                    {
                        sb.Append(indent).Append("  ").Append(index++).AppendLine(".");
                        AppendElement(sb, item, null, depth + 1);
                    }
                }
                break;

            default:
                if (name == null) break;
                sb.Append(indent).Append(FieldLabel(name)).Append(": ").AppendLine(Value(name, element));
                break;
        }
    }

    private static string FieldLabel(string name)
    {
        var key = char.ToUpperInvariant(name[0]) + name[1..];
        return Lookup($"Str_AuditField_{key}") ?? name;
    }

    private static string Value(string? name, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return Lookup("Str_Btn_Yes") ?? "true";
            case JsonValueKind.False:
                return Lookup("Str_Btn_No") ?? "false";
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return Lookup("Str_AuditValue_None") ?? "-";
            case JsonValueKind.Number:
                if (name != null && MoneyFields.Contains(name) && value.TryGetDecimal(out var money))
                    return string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0} đ", money);
                return value.GetRawText();
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc)
                    && text.Contains('T'))
                {
                    return AuditTime.ToVietnamTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                }

                return Lookup($"Str_AuditValue_{text}") ?? text;
            default:
                return value.GetRawText();
        }
    }

    /// <summary>Chuỗi theo ngôn ngữ đang chọn; null nếu từ điển không có khóa này.</summary>
    private static string? Lookup(string key)
    {
        var text = AppText.Get(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? null : text;
    }
}
