using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SmartPS.Tests.Integration;

/// <summary>
/// R12/R13 against real PostgreSQL: DB triggers (AC-10), jsonb round trip (T-JSONB), tamper detection (AC-12 / E7),
/// AUDIT_VERIFY logging (R17, A12), actor resolution and schema.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AuditTrailDbTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public AuditTrailDbTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static PostgresException? FindPostgres(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is PostgresException pg)
            {
                return pg;
            }

            ex = ex.InnerException;
        }

        return null;
    }

    private async Task SeedRowsAsync(IServiceProvider sp, int count, string tag)
    {
        var audit = sp.GetRequiredService<IAuditService>();
        for (var i = 0; i < count; i++)
        {
            Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLoginSuccess, AuditOutcome.Success, "Test", $"{tag}-{i}")));
        }
    }

    private async Task<List<(long Id, string Action, string Hash)>> SnapshotAsync()
    {
        var rows = await AuditDb.AllAsync(_db.Factory);
        return rows.Select(r => (r.AuditLogId, r.Action, r.Hash)).ToList();
    }

    [Theory]
    [InlineData("UPDATE \"AuditLogs\" SET \"Action\" = 'X'")]
    [InlineData("UPDATE \"AuditLogs\" SET \"Action\" = 'X' WHERE false")]
    [InlineData("DELETE FROM \"AuditLogs\"")]
    [InlineData("TRUNCATE \"AuditLogs\"")]
    public async Task AC10_raw_update_delete_truncate_are_rejected_and_data_unchanged(string sql)
    {
        // AC-10: Given AuditLogs has data, When UPDATE / DELETE / TRUNCATE via SQL, Then error and data unchanged.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        await SeedRowsAsync(sp, 3, "ac10");
        var before = await SnapshotAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync(sql));

        Assert.Contains("append-only", ex.MessageText, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task EF_bulk_update_and_delete_are_blocked_by_the_trigger()
    {
        // ExecuteUpdate/ExecuteDelete bypass the SaveChanges guard; the DB trigger still blocks them (plan §7).
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        await SeedRowsAsync(sp, 1, "bulk");
        var before = await SnapshotAsync();

        await using (var ctx = _db.CreateContext())
        {
            var del = await Record.ExceptionAsync(() => ctx.AuditLogs.ExecuteDeleteAsync());
            Assert.Contains("append-only", FindPostgres(del)?.MessageText ?? string.Empty, StringComparison.Ordinal);

            var upd = await Record.ExceptionAsync(() => ctx.AuditLogs.ExecuteUpdateAsync(s => s.SetProperty(a => a.Username, "x")));
            Assert.Contains("append-only", FindPostgres(upd)?.MessageText ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Inserted_rows_chain_to_the_previous_head()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var headBefore = (await AuditDb.AllAsync(_db.Factory)).LastOrDefault()?.Hash ?? AuditHashing.GenesisHash;

        await SeedRowsAsync(sp, 3, "chain");

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        Assert.True(rows.Count >= 3);
        Assert.Equal(headBefore, rows[0].PrevHash);
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.Equal(rows[i - 1].Hash, rows[i].PrevHash);
        }

        Assert.All(rows, r => Assert.Equal(AuditHashing.ComputeHash(r.PrevHash, r), r.Hash));
    }

    [Fact]
    public async Task TJSONB_round_trip_keeps_the_hash_stable()
    {
        // T-JSONB: jsonb rewrites text (key order, spacing); the stored hash must still verify after reading back.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var marker = Guid.NewGuid().ToString("N");
        var details = new
        {
            Zeta = "last",
            Alpha = "first",
            Name = "Nguyễn Văn Ánh – bãi xe Đà Nẵng",
            Nested = new { Z = 1, A = new[] { 3, 1, 2 }, Inner = new { Y = (string?)null, X = true } },
            Amount = 5000.00m,
            Rate = 0.10m,
            Flag = false,
            Marker = marker
        };

        Assert.True(await sp.GetRequiredService<IAuditService>().LogAsync(
            new AuditEntry(AuditActions.ParkingCheckOut, AuditOutcome.Success, "ParkingSession", marker, details)));

        await using var ctx = _db.CreateContext();
        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == marker);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);
        Assert.Equal(AuditDetails.ToCanonicalJson(details), AuditDetails.Canonicalize(row.Details));
        Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind);
        Assert.Equal(0, row.OccurredAtUtc.Ticks % 10);
        var json = AuditDb.Details(row);
        Assert.Equal("Nguyễn Văn Ánh – bãi xe Đà Nẵng", json.GetProperty("name").GetString());
        Assert.Equal(5000.00m, json.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Details_column_is_jsonb_and_indexes_exist()
    {
        _db.RequireAvailable();

        var type = await _db.ScalarAsync<string>(
            "SELECT data_type FROM information_schema.columns WHERE table_name = 'AuditLogs' AND column_name = 'Details'");
        Assert.Equal("jsonb", type);

        foreach (var index in new[]
                 {
                     "IX_AuditLogs_OccurredAtUtc", "IX_AuditLogs_Action_OccurredAtUtc", "IX_AuditLogs_UserId",
                     "IX_AuditLogs_Username", "IX_AuditLogs_EntityType_EntityId", "IX_AuditLogs_PrevHash"
                 })
        {
            var count = await _db.ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE tablename = 'AuditLogs' AND indexname = @n", ("n", index));
            Assert.True(count == 1, $"index {index} missing");
        }

        var unique = await _db.ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_AuditLogs_PrevHash'");
        Assert.Contains("UNIQUE", unique, StringComparison.OrdinalIgnoreCase);

        var fkCount = await _db.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.table_constraints WHERE table_name = 'AuditLogs' AND constraint_type = 'FOREIGN KEY'");
        Assert.Equal(0, fkCount);
    }

    [Fact]
    public async Task Duplicate_PrevHash_is_rejected_by_unique_index()
    {
        // E1 fork protection at the database level.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        await SeedRowsAsync(sp, 1, "fork");
        var head = (await AuditDb.AllAsync(_db.Factory)).Last();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync(
            "INSERT INTO \"AuditLogs\" (\"OccurredAtUtc\",\"Username\",\"RoleName\",\"Action\",\"Outcome\",\"Details\",\"MachineName\",\"PrevHash\",\"Hash\") " +
            "VALUES (now(), 'x', 'x', 'FORK', 'Success', '{}'::jsonb, 'x', @p, @h)",
            ("p", head.PrevHash), ("h", new string('b', 64))));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    [Fact]
    public async Task Actor_defaults_to_logged_in_user_and_explicit_actor_wins()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        var admin = await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var a = Guid.NewGuid().ToString("N");
        var b = Guid.NewGuid().ToString("N");

        Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "Test", a)));
        Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLoginFailed, AuditOutcome.Failed, "Test", b,
            new { attemptedUsername = "ghost" }, new AuditActor(null, "ghost", string.Empty))));

        await using var ctx = _db.CreateContext();
        var rowA = await ctx.AuditLogs.AsNoTracking().SingleAsync(x => x.EntityId == a);
        Assert.Equal(admin.UserId, rowA.UserId);
        Assert.Equal("admin", rowA.Username);
        Assert.Equal("Admin", rowA.RoleName);
        Assert.Equal(Environment.MachineName.Length > 128 ? Environment.MachineName[..128] : Environment.MachineName, rowA.MachineName);

        var rowB = await ctx.AuditLogs.AsNoTracking().SingleAsync(x => x.EntityId == b);
        Assert.Null(rowB.UserId);
        Assert.Equal("ghost", rowB.Username);
        Assert.Equal(string.Empty, rowB.RoleName);
        Assert.Equal(AuditOutcome.Failed, rowB.Outcome);
    }

    [Fact]
    public async Task Without_user_and_actor_the_row_is_anonymous()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        var id = Guid.NewGuid().ToString("N");

        Assert.True(await sp.GetRequiredService<IAuditService>().LogAsync(new AuditEntry(AuditActions.AccessDenied, AuditOutcome.Denied, "Navigation", id)));

        await using var ctx = _db.CreateContext();
        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync(x => x.EntityId == id);
        Assert.Null(row.UserId);
        Assert.Equal(string.Empty, row.Username);
        Assert.Equal(string.Empty, row.RoleName);
        Assert.Equal(AuditOutcome.Denied, row.Outcome);
    }

    [Fact]
    public async Task AC12_verify_ok_then_detects_tampered_row_by_id_and_logs_AUDIT_VERIFY()
    {
        // AC-12: Given N>=3 rows, Verify OK; Given a row edited with the trigger disabled, Verify reports that Id (HashMismatch).
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        await SeedRowsAsync(sp, 3, "ac12");
        var verifier = sp.GetRequiredService<IAuditIntegrityVerifier>();

        var idBeforeOk = await AuditDb.MaxIdAsync(_db.Factory);
        var ok = await verifier.VerifyAsync();
        Assert.True(ok.IsValid);
        Assert.True(ok.CheckedCount >= 3);
        var okRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBeforeOk, AuditActions.AuditVerify));
        Assert.Equal(AuditOutcome.Success, okRow.Outcome); // A12

        var rows = await AuditDb.AllAsync(_db.Factory);
        var target = rows[rows.Count / 2];
        var originalUsername = target.Username;
        // Keep the trigger's enable mode (ENABLE ALWAYS after HardenAuditTriggers, FX5) when restoring it.
        var originalMode = await _db.ScalarAsync<string>("SELECT tgenabled::text FROM pg_trigger WHERE tgname = 'TR_AuditLogs_NoUpdate'");

        try
        {
            await _db.ExecuteAsync("ALTER TABLE \"AuditLogs\" DISABLE TRIGGER \"TR_AuditLogs_NoUpdate\"");
            await _db.ExecuteAsync("UPDATE \"AuditLogs\" SET \"Username\" = 'tampered' WHERE \"AuditLogId\" = @id", ("id", target.AuditLogId));

            var idBeforeBroken = await AuditDb.MaxIdAsync(_db.Factory);
            var broken = await verifier.VerifyAsync();

            Assert.False(broken.IsValid);
            Assert.Equal(target.AuditLogId, broken.FirstInvalidAuditLogId);
            Assert.Equal(AuditChainFailureReason.HashMismatch, broken.FailureReason);
            var brokenRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBeforeBroken, AuditActions.AuditVerify));
            Assert.Equal(AuditOutcome.Failed, brokenRow.Outcome); // A12
        }
        finally
        {
            await _db.ExecuteAsync("UPDATE \"AuditLogs\" SET \"Username\" = @u WHERE \"AuditLogId\" = @id", ("u", originalUsername), ("id", target.AuditLogId));
            await _db.ExecuteAsync(originalMode == "A"
                ? "ALTER TABLE \"AuditLogs\" ENABLE ALWAYS TRIGGER \"TR_AuditLogs_NoUpdate\""
                : "ALTER TABLE \"AuditLogs\" ENABLE TRIGGER \"TR_AuditLogs_NoUpdate\"");
        }

        Assert.True((await verifier.VerifyAsync()).IsValid);
        await Assert.ThrowsAsync<PostgresException>(() => _db.ExecuteAsync("UPDATE \"AuditLogs\" SET \"Action\" = 'X' WHERE false"));
    }

    [Fact]
    public async Task Verify_requires_Audit_Verify()
    {
        // R5: Audit.Verify; Manager defaults (spec §6.1) include Audit.View only.
        _db.RequireAvailable();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.GetRequiredService<IAuditIntegrityVerifier>().VerifyAsync());

        Assert.Contains(Permissions.AuditVerify, ex.RequiredPermissions);
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows, r => r.Action == AuditActions.AccessDenied);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(manager.UserId, denied.UserId);
        Assert.DoesNotContain(rows, r => r.Action == AuditActions.AuditVerify);
    }
}
