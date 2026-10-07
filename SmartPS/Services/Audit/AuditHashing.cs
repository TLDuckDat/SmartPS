using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

/// <summary>Canonical payload and SHA-256 hash for the audit chain (format v1).</summary>
public static class AuditHashing
{
    public const string FormatVersion = "v1";

    public static readonly string GenesisHash = new('0', 64);

    public static DateTime NormalizeTimestamp(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    public static string FormatTimestamp(DateTime utc)
    {
        return utc.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffffff'Z'", CultureInfo.InvariantCulture);
    }

    public static string BuildCanonicalPayload(AuditLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, AuditDetails.CanonicalWriterOptions))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(FormatVersion);
            writer.WriteStringValue(FormatTimestamp(NormalizeTimestamp(entry.OccurredAtUtc)));
            if (entry.UserId.HasValue)
            {
                writer.WriteNumberValue(entry.UserId.Value);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteStringValue(entry.Username);
            writer.WriteStringValue(entry.RoleName);
            writer.WriteStringValue(entry.Action);
            WriteNullableString(writer, entry.EntityType);
            WriteNullableString(writer, entry.EntityId);
            writer.WriteStringValue(entry.Outcome.ToString());
            writer.WriteStringValue(AuditDetails.Canonicalize(entry.Details));
            writer.WriteStringValue(entry.MachineName);
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string ComputeHash(string prevHash, AuditLog entry)
    {
        var bytes = Encoding.UTF8.GetBytes(prevHash + BuildCanonicalPayload(entry));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
