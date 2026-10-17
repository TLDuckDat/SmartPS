using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartPS.Services.Audit;

/// <summary>Canonical JSON form of audit details (stable across jsonb round trips) and secret stripping.</summary>
public static class AuditDetails
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static readonly JsonWriterOptions CanonicalWriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlySet<string> ForbiddenKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwordHash", "newPassword", "confirmPassword", "apiKey", "checksumKey",
        "clientId", "signature", "webhookSignature", "secret", "token", "rawPayload"
    };

    public static string ToCanonicalJson(object? details)
    {
        if (details is null)
        {
            return "{}";
        }

        var node = JsonSerializer.SerializeToNode(details, details.GetType(), SerializerOptions);
        if (node is null)
        {
            return "{}";
        }

        StripForbidden(node);
        return Canonicalize(node.ToJsonString(SerializerOptions));
    }

    public static string Canonicalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CanonicalWriterOptions))
        {
            WriteCanonical(writer, document.RootElement);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void StripForbidden(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (ForbiddenKeys.Contains(key))
                    {
                        obj.Remove(key);
                    }
                    else
                    {
                        StripForbidden(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    StripForbidden(item);
                }

                break;
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var number))
                {
                    writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    // Không biểu diễn được bằng decimal: ghi dạng chuỗi để băm ổn định sau khi qua jsonb
                    writer.WriteStringValue(element.GetRawText());
                }

                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
