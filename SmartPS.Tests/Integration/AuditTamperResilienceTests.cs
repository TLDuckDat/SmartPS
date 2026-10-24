using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using SmartPS.DTOs.Auth;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Fix round 1 — hardening of the audit trail against a privileged attacker and hostile input:
/// FX4 (CH10) unparseable Outcome must not crash Verify / the audit page; FX5 (CH13) the append-only trigger is enabled
/// and blocks raw UPDATE/DELETE, and the empty guard table with a foreign key blocks TRUNCATE (which does not fire triggers);
/// FX6 (CH16) attemptedUsername is truncated; FX11 numbers that do not fit a decimal still hash-round-trip through the JSON column.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class AuditTamperResilienceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private const string TriggerName = SmartPS.Data.Configurations.Audit.AuditLogConfiguration.AuditLogTriggerName;

    private readonly SqlServerDatabaseFixture _db;

    public AuditTamperResilienceTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    /// <summary>DBA-style tamper: the append-only trigger is disabled for one statement and enabled again.</summary>
    private async Task TamperAsync(string sql)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            $"ALTER TABLE \"AuditLogs\" DISABLE TRIGGER \"{TriggerName}\"; {sql}; ALTER TABLE \"AuditLogs\" ENABLE TRIGGER \"{TriggerName}\";", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<IServiceProvider> AdminWithRowsAsync(int count)
    {
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        for (var i = 0; i < count; i++)
        {
            Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.ParkingCheckOut, AuditOutcome.Success, "ParkingSession", $"t{i}",
                new { Fee = 15000m, LicensePlate = "51A-123.45" })));
        }

        return sp;
    }

    [Fact]
    public async Task FX4_CH10_verify_reports_row_with_unparseable_outcome_and_still_logs_AUDIT_VERIFY()
    {
        _db.RequireAvailable();
        var sp = await AdminWithRowsAsync(3);
        var rows = await AuditDb.AllAsync(_db.Factory);
        var victim = rows[^2];
        var verifyRowsBefore = await _db.ScalarAsync<long>("SELECT count(*) FROM \"AuditLogs\" WHERE \"Action\" = 'AUDIT_VERIFY'");

        await TamperAsync($"UPDATE \"AuditLogs\" SET \"Outcome\" = 'Hacked' WHERE \"AuditLogId\" = {victim.AuditLogId}");
        AuditVerificationResult result;
        long verifyRowsAfter;
        string? lastVerifyOutcome;
        try
        {
            result = await sp.GetRequiredService<IAuditIntegrityVerifier>().VerifyAsync();
            verifyRowsAfter = await _db.ScalarAsync<long>("SELECT count(*) FROM \"AuditLogs\" WHERE \"Action\" = 'AUDIT_VERIFY'");
            lastVerifyOutcome = await _db.ScalarAsync<string>(
                "SELECT TOP (1) \"Outcome\" FROM \"AuditLogs\" WHERE \"Action\" = 'AUDIT_VERIFY' ORDER BY \"AuditLogId\" DESC");
        }
        finally
        {
            await TamperAsync($"UPDATE \"AuditLogs\" SET \"Outcome\" = '{victim.Outcome}' WHERE \"AuditLogId\" = {victim.AuditLogId}");
        }

        Assert.False(result.IsValid);
        Assert.Equal(victim.AuditLogId, result.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.HashMismatch, result.FailureReason);
        Assert.Equal(verifyRowsBefore + 1, verifyRowsAfter);
        Assert.Equal("Failed", lastVerifyOutcome);
    }

    [Fact]
    public async Task FX4_audit_page_does_not_crash_on_unparseable_outcome()
    {
        _db.RequireAvailable();
        var sp = await AdminWithRowsAsync(2);
        var victim = (await AuditDb.AllAsync(_db.Factory))[^1];

        await TamperAsync($"UPDATE \"AuditLogs\" SET \"Outcome\" = 'Hacked' WHERE \"AuditLogId\" = {victim.AuditLogId}");
        AuditPage page;
        try
        {
            page = await sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditQueryFilter { PageSize = 200 });
        }
        finally
        {
            await TamperAsync($"UPDATE \"AuditLogs\" SET \"Outcome\" = '{victim.Outcome}' WHERE \"AuditLogId\" = {victim.AuditLogId}");
        }

        Assert.Contains(page.Items, r => r.AuditLogId == victim.AuditLogId);
        Assert.True(page.TotalCount >= 2);
    }

    [Fact]
    public async Task FX5_audit_trigger_is_enabled_and_truncate_is_guarded()
    {
        _db.RequireAvailable();

        var enabled = await _db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.triggers WHERE name = @n AND parent_id = OBJECT_ID('dbo.AuditLogs') AND is_disabled = 0 AND is_instead_of_trigger = 1",
            ("n", TriggerName));
        Assert.Equal(1, enabled);

        var guardTable = await _db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.AuditLogsTruncateGuard') AND referenced_object_id = OBJECT_ID('dbo.AuditLogs')");
        Assert.Equal(1, guardTable);

        var applied = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%[_]InitialSqlServer'");
        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task FX5_CH13_raw_update_and_truncate_cannot_bypass_the_protection()
    {
        _db.RequireAvailable();
        _ = await AdminWithRowsAsync(2);
        var before = await AuditDb.AllAsync(_db.Factory);
        var victim = before[^1];

        var update = await Assert.ThrowsAsync<SqlException>(() => _db.ExecuteAsync(
            $"UPDATE \"AuditLogs\" SET \"Action\" = 'TAMPERED' WHERE \"AuditLogId\" = {victim.AuditLogId}"));
        // 4712: Cannot truncate table ... because it is being referenced by a FOREIGN KEY constraint
        var truncate = await Assert.ThrowsAsync<SqlException>(() => _db.ExecuteAsync("TRUNCATE TABLE \"AuditLogs\""));

        var after = await AuditDb.AllAsync(_db.Factory);
        if (after.Count == before.Count && after[^1].Action != victim.Action)
        {
            // Restore for the other tests in this class before failing.
            await TamperAsync($"UPDATE \"AuditLogs\" SET \"Action\" = '{victim.Action}' WHERE \"AuditLogId\" = {victim.AuditLogId}");
        }

        Assert.Contains("append-only", update.Message, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY", truncate.Message, StringComparison.Ordinal);
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(victim.Action, after[^1].Action);
    }

    [Fact]
    public async Task FX6_CH16_hostile_username_is_truncated_in_details_too()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var hostile = new string('A', 5000) + " ' ; DROP TABLE \"AuditLogs\"; -- %_\\";
        var expected = hostile.Trim().ToLowerInvariant()[..AuditRecordFactory.MaxUsernameLength];

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sp.Auth().LoginAsync(new LoginRequest { Username = hostile, Password = "x" }));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AuthLoginFailed));
        Assert.Equal(expected, row.Username);
        Assert.Equal(expected, AuditDb.String(AuditDb.Details(row), "attemptedUsername"));
        Assert.True(row.Details.Length < 1000, $"details length {row.Details.Length}");
        Assert.True(AuditChainVerifier.Verify(await AuditDb.AllAsync(_db.Factory)).IsValid);
    }

    [Fact]
    public async Task FX11_numbers_outside_decimal_range_survive_the_jsonb_round_trip()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var marker = Guid.NewGuid().ToString("N");

        Assert.True(await sp.GetRequiredService<IAuditService>().LogAsync(new AuditEntry(
            AuditActions.ShiftAdjustment, AuditOutcome.Success, "Test", marker,
            new { Big = 1e300, Negative = -1.5e200, Normal = 5000.00m })));

        await using var ctx = _db.CreateContext();
        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == marker);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);
        Assert.True(AuditChainVerifier.Verify(await AuditDb.AllAsync(_db.Factory)).IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData(" Success")]
    [InlineData("Success ")]
    [InlineData("success")]
    [InlineData("SUCCESS")]
    [InlineData("Success, Denied")]
    public async Task FY3_CH31_any_change_of_the_stored_outcome_text_is_detected_and_not_shown_as_valid(string tamperedText)
    {
        // FY3: Outcome is parsed strictly (exact "Success" / "Denied" / "Failed"); numeric, padded, re-cased or flag
        // forms are invalid → Verify reports HashMismatch at that row, and the audit page still lists the row
        // without presenting it as a valid outcome.
        _db.RequireAvailable();
        var tag = "fy3-" + Guid.NewGuid().ToString("N")[..8];
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.ParkingCheckOut, AuditOutcome.Success, "ParkingSession", $"{tag}-{i}")));
        }

        var victimId = await _db.ScalarAsync<long>("SELECT \"AuditLogId\" FROM \"AuditLogs\" WHERE \"EntityId\" = @e", ("e", $"{tag}-1"));

        await TamperAsync($"UPDATE \"AuditLogs\" SET \"Outcome\" = '{tamperedText.Replace("'", "''")}' WHERE \"AuditLogId\" = {victimId}");
        AuditVerificationResult verify;
        AuditPage page;
        try
        {
            verify = await sp.GetRequiredService<IAuditIntegrityVerifier>().VerifyAsync();
            page = await sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditQueryFilter { SearchText = tag });
        }
        finally
        {
            await TamperAsync($"UPDATE \"AuditLogs\" SET \"Outcome\" = 'Success' WHERE \"AuditLogId\" = {victimId}");
        }

        Assert.False(verify.IsValid, $"Outcome '{tamperedText}' was accepted as a valid stored value");
        Assert.Equal(victimId, verify.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.HashMismatch, verify.FailureReason);

        Assert.Equal(3, page.TotalCount);
        var shown = Assert.Single(page.Items, r => r.AuditLogId == victimId);
        Assert.False(Enum.IsDefined(shown.Outcome), $"tampered outcome '{tamperedText}' is displayed as {shown.Outcome}");
    }
}
