using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SmartPS.Tests.Unit;

/// <summary>R13 layer 3 (hash chain) — canonical payload and SHA-256 per plan §1.4.</summary>
public class AuditHashingTests
{
    private static readonly DateTime SampleTime =
        new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc).AddTicks(1_234_560); // .123456

    private static AuditLog Sample() => new()
    {
        AuditLogId = 42,
        OccurredAtUtc = SampleTime,
        UserId = 5,
        Username = "admin",
        RoleName = "Admin",
        Action = "AUTH_LOGIN_SUCCESS",
        EntityType = null,
        EntityId = null,
        Outcome = AuditOutcome.Success,
        Details = "{}",
        MachineName = "PC-01",
        PrevHash = new string('0', 64),
        Hash = string.Empty
    };

    private static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [Fact]
    public void GenesisHash_is_64_zero_characters()
    {
        Assert.Equal(new string('0', 64), AuditHashing.GenesisHash);
        Assert.Equal("v1", AuditHashing.FormatVersion);
    }

    [Fact]
    public void NormalizeTimestamp_truncates_to_microseconds_and_keeps_utc()
    {
        var input = new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc).AddTicks(1_234_567);

        var normalized = AuditHashing.NormalizeTimestamp(input);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(input.Ticks - 7, normalized.Ticks);
        Assert.Equal(0, normalized.Ticks % 10);
    }

    [Fact]
    public void NormalizeTimestamp_converts_local_time_to_utc()
    {
        var local = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Local).AddTicks(15);

        var normalized = AuditHashing.NormalizeTimestamp(local);

        var expectedUtc = local.ToUniversalTime();
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(expectedUtc.Ticks - expectedUtc.Ticks % 10, normalized.Ticks);
    }

    [Fact]
    public void FormatTimestamp_uses_fixed_invariant_format_with_six_fraction_digits()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // A non-Gregorian default calendar must not leak into the canonical form.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var value = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(70);

            Assert.Equal("2026-01-02T03:04:05.000007Z", AuditHashing.FormatTimestamp(value));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BuildCanonicalPayload_has_exact_v1_layout_with_nulls()
    {
        var payload = AuditHashing.BuildCanonicalPayload(Sample());

        Assert.Equal(
            "[\"v1\",\"2026-10-07T01:02:03.123456Z\",5,\"admin\",\"Admin\",\"AUTH_LOGIN_SUCCESS\",null,null,\"Success\",\"{}\",\"PC-01\"]",
            payload);
    }

    [Fact]
    public void BuildCanonicalPayload_embeds_canonical_details_as_string_and_null_user()
    {
        var entry = Sample();
        entry.UserId = null;
        entry.Username = "ghost";
        entry.RoleName = string.Empty;
        entry.Action = "ACCESS_DENIED";
        entry.EntityType = "Navigation";
        entry.EntityId = "UserManagement";
        entry.Outcome = AuditOutcome.Denied;
        entry.Details = "{ \"b\": 2, \"a\": \"x\" }";

        var payload = AuditHashing.BuildCanonicalPayload(entry);

        Assert.Equal(
            "[\"v1\",\"2026-10-07T01:02:03.123456Z\",null,\"ghost\",\"\",\"ACCESS_DENIED\",\"Navigation\",\"UserManagement\",\"Denied\",\"{\\\"a\\\":\\\"x\\\",\\\"b\\\":2}\",\"PC-01\"]",
            payload);
    }

    [Fact]
    public void ComputeHash_is_lowercase_sha256_of_prev_plus_payload()
    {
        var entry = Sample();
        var prev = AuditHashing.GenesisHash;

        var hash = AuditHashing.ComputeHash(prev, entry);

        Assert.Equal(Sha256Hex(prev + AuditHashing.BuildCanonicalPayload(entry)), hash);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeHash_depends_on_previous_hash()
    {
        var entry = Sample();

        var a = AuditHashing.ComputeHash(AuditHashing.GenesisHash, entry);
        var b = AuditHashing.ComputeHash(new string('1', 64), entry);

        Assert.NotEqual(a, b);
    }

    public static TheoryData<string> HashedFieldMutations => new()
    {
        "OccurredAtUtc", "UserId", "Username", "RoleName", "Action", "EntityType", "EntityId", "Outcome", "Details", "MachineName"
    };

    [Theory]
    [MemberData(nameof(HashedFieldMutations))]
    public void Changing_any_hashed_field_changes_the_hash(string field)
    {
        var original = Sample();
        var mutated = Sample();
        switch (field)
        {
            case "OccurredAtUtc": mutated.OccurredAtUtc = mutated.OccurredAtUtc.AddTicks(10); break;
            case "UserId": mutated.UserId = 6; break;
            case "Username": mutated.Username = "admin2"; break;
            case "RoleName": mutated.RoleName = "Manager"; break;
            case "Action": mutated.Action = "AUTH_LOGOUT"; break;
            case "EntityType": mutated.EntityType = "User"; break;
            case "EntityId": mutated.EntityId = "1"; break;
            case "Outcome": mutated.Outcome = AuditOutcome.Failed; break;
            case "Details": mutated.Details = "{\"x\":1}"; break;
            case "MachineName": mutated.MachineName = "PC-02"; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }

        Assert.NotEqual(
            AuditHashing.ComputeHash(AuditHashing.GenesisHash, original),
            AuditHashing.ComputeHash(AuditHashing.GenesisHash, mutated));
    }

    [Fact]
    public void AuditLogId_PrevHash_and_Hash_columns_are_not_part_of_the_payload()
    {
        var a = Sample();
        var b = Sample();
        b.AuditLogId = 9999;
        b.Hash = "anything";
        b.PrevHash = new string('f', 64);

        Assert.Equal(AuditHashing.BuildCanonicalPayload(a), AuditHashing.BuildCanonicalPayload(b));
    }

    [Fact]
    public void Details_key_order_and_whitespace_do_not_change_the_hash()
    {
        // jsonb rewrites JSON text (key order, whitespace); the hash must survive the round trip.
        var a = Sample();
        a.Details = "{\"amount\":5000.00,\"plate\":\"51A-123.45\",\"nested\":{\"z\":true,\"a\":null}}";
        var b = Sample();
        b.Details = "{ \"nested\" : { \"a\" : null, \"z\" : true }, \"plate\" : \"51A-123.45\", \"amount\" : 5000.00 }";

        Assert.Equal(
            AuditHashing.ComputeHash(AuditHashing.GenesisHash, a),
            AuditHashing.ComputeHash(AuditHashing.GenesisHash, b));
    }
}
