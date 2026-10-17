using System.Text.Json;

namespace SmartPS.Tests.Unit;

/// <summary>AuditRecordFactory / AuditActor / AuditEntry contracts (plan §2.4, §1.4).</summary>
public class AuditRecordFactoryTests
{
    private static readonly DateTime When = new DateTime(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    [Fact]
    public void Create_copies_entry_and_actor_and_chains_hash()
    {
        var entry = new AuditEntry(AuditActions.ParkingCheckOut, AuditOutcome.Success, "ParkingSession", "77",
            new { LicensePlate = "51A-999.99", Fee = 5000m, PaymentMethod = "Cash" });
        var actor = new AuditActor(12, "op1", "Operator");
        var prev = new string('a', 64);

        var row = AuditRecordFactory.Create(entry, actor, When, "GATE-PC", prev);

        Assert.Equal(0, row.AuditLogId);
        Assert.Equal(AuditHashing.NormalizeTimestamp(When), row.OccurredAtUtc);
        Assert.Equal(12, row.UserId);
        Assert.Equal("op1", row.Username);
        Assert.Equal("Operator", row.RoleName);
        Assert.Equal("PARKING_CHECKOUT", row.Action);
        Assert.Equal("ParkingSession", row.EntityType);
        Assert.Equal("77", row.EntityId);
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("{\"fee\":5000,\"licensePlate\":\"51A-999.99\",\"paymentMethod\":\"Cash\"}", row.Details);
        Assert.Equal("GATE-PC", row.MachineName);
        Assert.Equal(prev, row.PrevHash);
        Assert.Equal(AuditHashing.ComputeHash(prev, row), row.Hash);
    }

    [Fact]
    public void Create_with_null_details_and_anonymous_actor()
    {
        var row = AuditRecordFactory.Create(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success),
            AuditActor.Anonymous, When, "PC", AuditHashing.GenesisHash);

        Assert.Equal("{}", row.Details);
        Assert.Null(row.UserId);
        Assert.Equal(string.Empty, row.Username);
        Assert.Equal(string.Empty, row.RoleName);
        Assert.Equal(AuditHashing.GenesisHash, row.PrevHash);
    }

    [Fact]
    public void Create_truncates_long_fields_before_hashing()
    {
        var entry = new AuditEntry(new string('A', 100), AuditOutcome.Failed, new string('T', 100), new string('9', 300));
        var actor = new AuditActor(1, new string('u', 150), new string('r', 80));

        var row = AuditRecordFactory.Create(entry, actor, When, new string('m', 200), AuditHashing.GenesisHash);

        Assert.Equal(AuditRecordFactory.MaxUsernameLength, row.Username.Length);
        Assert.Equal(AuditRecordFactory.MaxRoleNameLength, row.RoleName.Length);
        Assert.Equal(AuditRecordFactory.MaxActionLength, row.Action.Length);
        Assert.Equal(AuditRecordFactory.MaxEntityTypeLength, row.EntityType!.Length);
        Assert.Equal(AuditRecordFactory.MaxEntityIdLength, row.EntityId!.Length);
        Assert.Equal(AuditRecordFactory.MaxMachineNameLength, row.MachineName.Length);
        Assert.Equal(AuditHashing.ComputeHash(AuditHashing.GenesisHash, row), row.Hash);
    }

    [Fact]
    public void Max_lengths_match_column_mapping()
    {
        Assert.Equal(100, AuditRecordFactory.MaxUsernameLength);
        Assert.Equal(50, AuditRecordFactory.MaxRoleNameLength);
        Assert.Equal(64, AuditRecordFactory.MaxActionLength);
        Assert.Equal(64, AuditRecordFactory.MaxEntityTypeLength);
        Assert.Equal(128, AuditRecordFactory.MaxEntityIdLength);
        Assert.Equal(128, AuditRecordFactory.MaxMachineNameLength);
    }

    [Fact]
    public void Create_strips_secrets_from_details()
    {
        // R18
        var entry = new AuditEntry(AuditActions.UserUpdate, AuditOutcome.Success, "User", "3",
            new { PasswordChanged = true, PasswordHash = "$2a$11$xyz", NewPassword = "Secret#9" });

        var row = AuditRecordFactory.Create(entry, new AuditActor(1, "admin", "Admin"), When, "PC", AuditHashing.GenesisHash);

        Assert.Equal("{\"passwordChanged\":true}", row.Details);
        Assert.DoesNotContain("$2a$", row.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret#9", row.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditActor_Anonymous_has_no_identity()
    {
        Assert.Null(AuditActor.Anonymous.UserId);
        Assert.Equal(string.Empty, AuditActor.Anonymous.Username);
        Assert.Equal(string.Empty, AuditActor.Anonymous.RoleName);
    }

    [Fact]
    public void AuditActor_FromUser_snapshots_id_username_and_role_name()
    {
        var user = TestUsers.Manager();

        var actor = AuditActor.FromUser(user);

        Assert.Equal(user.UserId, actor.UserId);
        Assert.Equal(user.Username, actor.Username);
        Assert.Equal("Manager", actor.RoleName);
    }

    [Fact]
    public void AuditEntry_AccessDenied_builds_denied_entry_with_required_permissions_and_reason()
    {
        var actor = new AuditActor(9, "op", "Operator");

        var entry = AuditEntry.AccessDenied(new[] { Permissions.UserCreate }, "MissingPermission", "User", null, actor);

        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal("User", entry.EntityType);
        Assert.Null(entry.EntityId);
        Assert.Equal(actor, entry.Actor);
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        Assert.Equal(new[] { "User.Create" },
            doc.RootElement.GetProperty("requiredPermissions").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal("MissingPermission", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void AuditEntry_AccessDenied_defaults_reason_to_MissingPermission()
    {
        var entry = AuditEntry.AccessDenied(new[] { Permissions.AuditView });

        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        Assert.Equal("MissingPermission", doc.RootElement.GetProperty("reason").GetString());
        Assert.Null(entry.Actor);
    }
}
