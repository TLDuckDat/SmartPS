using System.Text.Json;

namespace SmartPS.Tests.Unit;

/// <summary>Canonical JSON (plan §1.4) and secret stripping (R18).</summary>
public class AuditDetailsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Canonicalize_blank_input_returns_empty_object(string? input)
    {
        Assert.Equal("{}", AuditDetails.Canonicalize(input));
    }

    [Fact]
    public void Canonicalize_sorts_object_keys_ordinally_and_recursively()
    {
        var result = AuditDetails.Canonicalize("{\"b\":1,\"a\":{\"d\":2,\"c\":3},\"B\":0}");

        // Ordinal: uppercase 'B' (0x42) sorts before 'a' (0x61).
        Assert.Equal("{\"B\":0,\"a\":{\"c\":3,\"d\":2},\"b\":1}", result);
    }

    [Fact]
    public void Canonicalize_keeps_array_order_and_sorts_objects_inside_arrays()
    {
        var result = AuditDetails.Canonicalize("{\"items\":[3,1,{\"y\":1,\"x\":2},\"a\"]}");

        Assert.Equal("{\"items\":[3,1,{\"x\":2,\"y\":1},\"a\"]}", result);
    }

    [Fact]
    public void Canonicalize_removes_whitespace_and_keeps_decimal_scale()
    {
        var result = AuditDetails.Canonicalize("{ \"fee\" : 5000.00 , \"rate\" : 0.10, \"count\": 3 }");

        Assert.Equal("{\"count\":3,\"fee\":5000.00,\"rate\":0.10}", result);
    }

    [Fact]
    public void Canonicalize_preserves_booleans_and_nulls()
    {
        Assert.Equal("{\"a\":true,\"b\":false,\"c\":null}", AuditDetails.Canonicalize("{\"c\":null,\"b\":false,\"a\":true}"));
    }

    [Fact]
    public void Canonicalize_does_not_escape_vietnamese_text()
    {
        var result = AuditDetails.Canonicalize("{\"name\":\"Nguyễn Văn Á\"}");

        Assert.Equal("{\"name\":\"Nguyễn Văn Á\"}", result);
    }

    [Fact]
    public void Canonicalize_is_idempotent()
    {
        var once = AuditDetails.Canonicalize("{\"z\":{\"b\":[1,{\"d\":1,\"c\":2}],\"a\":\"Đ\"},\"m\":1.50}");

        Assert.Equal(once, AuditDetails.Canonicalize(once));
    }

    [Fact]
    public void ToCanonicalJson_null_returns_empty_object()
    {
        Assert.Equal("{}", AuditDetails.ToCanonicalJson(null));
    }

    [Fact]
    public void ToCanonicalJson_uses_camelCase_and_sorted_keys()
    {
        var result = AuditDetails.ToCanonicalJson(new { LicensePlate = "51A-123.45", Fee = 5000m, PaymentMethod = "Cash" });

        Assert.Equal("{\"fee\":5000,\"licensePlate\":\"51A-123.45\",\"paymentMethod\":\"Cash\"}", result);
    }

    [Fact]
    public void ToCanonicalJson_strips_forbidden_keys_recursively_but_keeps_passwordChanged()
    {
        // R18: no password, hash, PayOS keys or webhook signature in audit details.
        var result = AuditDetails.ToCanonicalJson(new
        {
            Password = "Secret#1",
            PasswordHash = "$2a$11$abcdefghijklmnopqrstuv",
            NewPassword = "Secret#2",
            ConfirmPassword = "Secret#2",
            PasswordChanged = true,
            Nested = new { ApiKey = "k", ChecksumKey = "c", ClientId = "id", Ok = 1 },
            Items = new[] { new { Signature = "s", WebhookSignature = "w", Secret = "x", Token = "t", RawPayload = "{}", V = 1 } }
        });

        Assert.Equal("{\"items\":[{\"v\":1}],\"nested\":{\"ok\":1},\"passwordChanged\":true}", result);
    }

    [Fact]
    public void ToCanonicalJson_strips_forbidden_dictionary_keys_case_insensitively()
    {
        var details = new Dictionary<string, object?>
        {
            ["PASSWORD"] = "x",
            ["Token"] = "t",
            ["keep"] = 1,
            ["inner"] = new Dictionary<string, object?> { ["SIGNATURE"] = "sig", ["plate"] = "30A" }
        };

        var result = AuditDetails.ToCanonicalJson(details);

        using var doc = JsonDocument.Parse(result);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, n => n.Equals("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("token", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("keep", names);
        var inner = doc.RootElement.EnumerateObject().Single(p => p.Name.Equals("inner", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.DoesNotContain(inner.EnumerateObject(), p => p.Name.Equals("signature", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inner.EnumerateObject(), p => p.Value.GetString() == "30A");
        Assert.DoesNotContain("\"sig\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCanonicalJson_output_is_already_canonical()
    {
        var result = AuditDetails.ToCanonicalJson(new { B = new[] { 2, 1 }, A = new { D = 0.10m, C = "Đường" } });

        Assert.Equal(result, AuditDetails.Canonicalize(result));
        Assert.Equal("{\"a\":{\"c\":\"Đường\",\"d\":0.10},\"b\":[2,1]}", result);
    }

    [Fact]
    public void ForbiddenKeys_contains_every_secret_key_from_plan_and_not_passwordChanged()
    {
        var expected = new[]
        {
            "password", "passwordHash", "newPassword", "confirmPassword", "apiKey", "checksumKey",
            "clientId", "signature", "webhookSignature", "secret", "token", "rawPayload"
        };

        foreach (var key in expected)
        {
            Assert.Contains(AuditDetails.ForbiddenKeys, k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(AuditDetails.ForbiddenKeys, k => k.Equals("passwordChanged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FX11_number_outside_decimal_range_is_written_as_its_raw_text_string()
    {
        // jsonb rewrites 1E+300 as 1 followed by 300 zeros; keeping it as a JSON string makes the hash round-trip.
        Assert.Equal("{\"v\":\"1E+300\"}", AuditDetails.ToCanonicalJson(new { V = 1e300 }));
        Assert.Equal("{\"v\":\"1e300\"}", AuditDetails.Canonicalize("{\"v\":1e300}"));
    }

    [Fact]
    public void FX11_canonical_form_with_out_of_range_numbers_is_idempotent()
    {
        var once = AuditDetails.ToCanonicalJson(new { Big = 1e300, Negative = -1.5e200, Normal = 5000.00m, Small = 0.10m });

        Assert.Equal(once, AuditDetails.Canonicalize(once));
        Assert.Contains("\"normal\":5000.00", once, StringComparison.Ordinal);
        Assert.Contains("\"small\":0.10", once, StringComparison.Ordinal);
    }
}
