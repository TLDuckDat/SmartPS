using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartPS.Data;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Audited transaction mechanics: T-M2a (fail fast while the lock is held), AC-13 / E1 (concurrent writers keep one chain),
/// commit/rollback atomicity of AppendAsync, addendum N3 (lock timeout → LogAsync returns false).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AuditLockTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly TimeSpan FailFast = TimeSpan.FromSeconds(1);

    private readonly PostgresDatabaseFixture _db;

    public AuditLockTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static void AssertChain(IReadOnlyList<AuditLog> rows)
    {
        Assert.NotEmpty(rows);
        Assert.Equal(AuditHashing.GenesisHash, rows[0].PrevHash);
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i - 1].Hash == rows[i].PrevHash, $"row {rows[i].AuditLogId} does not link to {rows[i - 1].AuditLogId}");
        }

        Assert.Equal(rows.Count, rows.Select(r => r.PrevHash).Distinct(StringComparer.Ordinal).Count());
        Assert.True(AuditChainVerifier.Verify(rows).IsValid);
    }

    [Fact]
    public async Task TM2a_LogAsync_and_guard_inside_audited_block_throw_fast_then_work_after()
    {
        // T-M2a: a self-deadlock becomes an immediate AuditLockHeldException instead of a 15 s hang.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var guard = sp.GetRequiredService<IAuthorizationGuard>();

        await using (var db = _db.CreateContext())
        {
            await using (var tx = await audit.BeginAuditedTransactionAsync(db))
            {
                Assert.True(AuditService.IsLockHeldInCurrentFlow);
                Assert.False(tx.IsCommitted);

                var sw = Stopwatch.StartNew();
                await Assert.ThrowsAsync<AuditLockHeldException>(() => audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success)));
                Assert.True(sw.Elapsed < FailFast, $"LogAsync took {sw.Elapsed}");

                sw.Restart();
                await Assert.ThrowsAsync<AuditLockHeldException>(() => guard.DemandAsync(Permissions.UserView));
                Assert.True(sw.Elapsed < FailFast, $"DemandAsync took {sw.Elapsed}");

                await using var nestedDb = _db.CreateContext();
                sw.Restart();
                await Assert.ThrowsAsync<AuditLockHeldException>(() => audit.BeginAuditedTransactionAsync(nestedDb));
                Assert.True(sw.Elapsed < FailFast, $"nested Begin took {sw.Elapsed}");
            }
        }

        Assert.False(AuditService.IsLockHeldInCurrentFlow);
        var after = Stopwatch.StartNew();
        Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "Test", "after-block")));
        Assert.True(after.Elapsed < TimeSpan.FromSeconds(5), $"LogAsync after the block took {after.Elapsed} (lock not released?)");
    }

    [Fact]
    public async Task Committed_block_persists_business_change_and_audit_row_together()
    {
        // R15 success path at the mechanism level.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var headBefore = (await AuditDb.AllAsync(_db.Factory)).LastOrDefault()?.Hash ?? AuditHashing.GenesisHash;

        await using (var db = _db.CreateContext())
        {
            await using (var tx = await audit.BeginAuditedTransactionAsync(db))
            {
                db.Roles.Add(new SmartPS.Models.Auth.Role { RoleName = "r_" + marker, Description = "tmp" });
                await db.SaveChangesAsync();
                var appended = await audit.AppendAsync(db, new AuditEntry(AuditActions.RolePermissionsUpdate, AuditOutcome.Success, "Role", marker));
                Assert.Equal(headBefore, appended.PrevHash);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                Assert.True(tx.IsCommitted);
            }
        }

        Assert.False(AuditService.IsLockHeldInCurrentFlow);
        await using var check = _db.CreateContext();
        Assert.True(await check.Roles.AnyAsync(r => r.RoleName == "r_" + marker));
        var row = await check.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == marker);
        Assert.Equal(headBefore, row.PrevHash);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);
    }

    [Fact]
    public async Task Uncommitted_block_rolls_back_business_change_and_audit_row_and_releases_lock()
    {
        // R15: the operation rolls back → no audit Success row.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var marker = Guid.NewGuid().ToString("N")[..12];

        await using (var db = _db.CreateContext())
        {
            await using (var tx = await audit.BeginAuditedTransactionAsync(db))
            {
                db.Roles.Add(new SmartPS.Models.Auth.Role { RoleName = "r_" + marker, Description = "tmp" });
                await audit.AppendAsync(db, new AuditEntry(AuditActions.RolePermissionsUpdate, AuditOutcome.Success, "Role", marker));
                await db.SaveChangesAsync();
                // leave the block without CommitAsync
            }
        }

        Assert.False(AuditService.IsLockHeldInCurrentFlow);
        await using (var check = _db.CreateContext())
        {
            Assert.False(await check.Roles.AnyAsync(r => r.RoleName == "r_" + marker));
            Assert.False(await check.AuditLogs.AnyAsync(a => a.EntityId == marker));
        }

        var sw = Stopwatch.StartNew();
        Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "Test", "after-rollback-" + marker)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"lock still held after rollback ({sw.Elapsed})");
        AssertChain(await AuditDb.AllAsync(_db.Factory));
    }

    [Fact]
    public async Task Begin_on_context_with_existing_transaction_is_rejected_and_marker_cleared()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        var audit = sp.GetRequiredService<IAuditService>();

        await using var db = _db.CreateContext();
        await using var plain = await db.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using (await audit.BeginAuditedTransactionAsync(db))
            {
            }
        });

        Assert.False(AuditService.IsLockHeldInCurrentFlow);
        Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "Test", "after-existing-tx")));
    }

    [Fact]
    public async Task AC13_twenty_parallel_writes_keep_a_single_valid_chain()
    {
        // AC-13 / E1: Given 20 concurrent audit writes, Then every PrevHash equals the previous row's Hash by Id and Verify is OK.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            audit.LogAsync(new AuditEntry(AuditActions.AuthLoginSuccess, AuditOutcome.Success, "Parallel", $"{tag}-{i}")))));

        Assert.All(results, r => Assert.True(r));
        var rows = await AuditDb.AllAsync(_db.Factory);
        Assert.Equal(20, rows.Count(r => r.EntityId != null && r.EntityId.StartsWith(tag, StringComparison.Ordinal)));
        AssertChain(rows);
        Assert.True((await sp.GetRequiredService<IAuditIntegrityVerifier>().VerifyAsync()).IsValid);
    }

    [Fact]
    public async Task E1_parallel_audited_business_transactions_do_not_fork_the_chain()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var tag = Guid.NewGuid().ToString("N")[..8];

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            await using var db = _db.CreateContext();
            await using (var tx = await audit.BeginAuditedTransactionAsync(db))
            {
                await audit.AppendAsync(db, new AuditEntry(AuditActions.ShiftAdjustment, AuditOutcome.Success, "Parallel", $"{tag}-{i}"));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
        })));

        var rows = await AuditDb.AllAsync(_db.Factory);
        Assert.Equal(10, rows.Count(r => r.EntityId != null && r.EntityId.StartsWith(tag, StringComparison.Ordinal)));
        AssertChain(rows);
    }

    [Fact]
    public async Task N3_lock_timeout_makes_LogAsync_return_false_without_writing()
    {
        // Addendum N3: when another session holds the chain lock longer than LockTimeout, LogAsync returns false
        // (caller token not cancelled) and leaves no marker behind.
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var audit = sp.GetRequiredService<IAuditService>();
        var marker = Guid.NewGuid().ToString("N");

        await using var holder = new NpgsqlConnection(_db.ConnectionString);
        await holder.OpenAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", holder))
        {
            take.Parameters.AddWithValue("k", AuditService.AuditChainLockKey);
            await take.ExecuteNonQueryAsync();
        }

        bool written;
        var sw = Stopwatch.StartNew();
        try
        {
            written = await audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "Test", marker));
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", holder);
            release.Parameters.AddWithValue("k", AuditService.AuditChainLockKey);
            await release.ExecuteNonQueryAsync();
        }

        Assert.False(written);
        Assert.InRange(sw.Elapsed, AuditService.LockTimeout - TimeSpan.FromSeconds(1), AuditService.LockTimeout + TimeSpan.FromSeconds(15));
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
        await using (var check = _db.CreateContext())
        {
            Assert.False(await check.AuditLogs.AnyAsync(a => a.EntityId == marker));
        }

        Assert.True(await audit.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success, "Test", marker + "-retry")));
        AssertChain(await AuditDb.AllAsync(_db.Factory));
    }
}
