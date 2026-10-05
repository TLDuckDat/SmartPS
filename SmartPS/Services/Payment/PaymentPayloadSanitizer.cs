using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartPS.Services.Payment;

public static class PaymentPayloadSanitizer
{
    private static readonly Regex SecretPattern = new(
        "(api[_-]?key|checksum[_-]?key|client[_-]?id|secret|password|authorization|x-api-key|x-client-id)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(payload);
            if (node != null)
            {
                RedactNode(node);
                return node.ToJsonString();
            }
        }
        catch
        {
            // fall through to regex
        }

        return SecretPattern.Replace(payload, "$1=***");
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (SecretPattern.IsMatch(key))
                {
                    obj[key] = "***";
                    continue;
                }

                if (obj[key] is JsonNode child)
                {
                    RedactNode(child);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null)
                {
                    RedactNode(item);
                }
            }
        }
    }
}
