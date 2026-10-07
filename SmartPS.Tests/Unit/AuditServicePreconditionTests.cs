using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AuditService behaviour that does not need a live server: E2 (offline → LogAsync returns false),
/// Append preconditions, M2 marker reset after a failed Begin (addendum N3), N4 (Begin is non-async).
/// </summary>
public class AuditServicePreconditionTests
{
    private const string OfflineConnection = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Pooling=false";

    private sealed class OfflineFactory : IDbContextFactory<SmartPsDbContext>
    {
        public SmartPsDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<SmartPsDbContext>().UseNpgsql(OfflineConnection).Options);
    }

    private static AuditService CreateService(CurrentUserContext? context = null)
        => new(new OfflineFactory(), context ?? new CurrentUserContext());

    [Fact]
    public void Lock_constants_match_plan()
    {
        Assert.Equal(0x534D_5053_4155_4454L, AuditService.AuditChainLockKey);
        Assert.Equal(TimeSpan.FromSeconds(15), AuditService.LockTimeout);
    }

    [Fact]
    public void Lock_is_not_held_by_default()
    {
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
    }

    [Fact]
    public void BeginAuditedTransactionAsync_is_not_an_async_state_machine()
    {
        // Addendum N4 / plan §1.3: the AsyncLocal marker must be set synchronously so it flows to the caller.
        var method = typeof(AuditService).GetMethod(nameof(IAuditService.BeginAuditedTransactionAsync), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Null(method!.GetCustomAttribute<AsyncStateMachineAttribute>());
    }

    [Fact]
    public async Task LogAsync_returns_false_when_database_is_unreachable()
    {
        // E2: audit DB offline must not break the caller.
        var context = new CurrentUserContext();
        context.SetUser(TestUsers.Operator());
        var service = CreateService(context);

        var written = await service.LogAsync(new AuditEntry(AuditActions.ParkingCheckIn, AuditOutcome.Success, "ParkingSession", "1"));

        Assert.False(written);
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
    }

    [Fact]
    public async Task LogAsync_with_cancelled_token_throws_OperationCanceledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LogAsync(new AuditEntry(AuditActions.AuthLogout, AuditOutcome.Success), cts.Token));
    }

    [Fact]
    public async Task AppendAsync_without_audited_transaction_is_rejected()
    {
        var service = CreateService();
        await using var db = new OfflineFactory().CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AppendAsync(db, new AuditEntry(AuditActions.UserCreate, AuditOutcome.Success, "User", "1")));
        Assert.Empty(db.ChangeTracker.Entries<AuditLog>());
    }

    [Fact]
    public async Task Failed_begin_resets_the_lock_marker()
    {
        // Addendum N3: BeginCore failure path resets the marker, so later audit calls are not mistaken for misuse.
        var service = CreateService();
        await using var db = new OfflineFactory().CreateDbContext();
        var sw = Stopwatch.StartNew();

        // Begin is called directly in this method (not in a lambda or helper) so the AsyncLocal marker lives in this flow.
        Exception? first = null;
        try
        {
            await using (await service.BeginAuditedTransactionAsync(db))
            {
            }
        }
        catch (Exception ex) when (ex is not AuditLockHeldException)
        {
            first = ex;
        }

        Assert.NotNull(first);
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
        Assert.True(sw.Elapsed < AuditService.LockTimeout, "connection failure must surface before the lock timeout");

        // A second attempt in the same flow fails for the same (connection) reason, not with AuditLockHeldException.
        Exception? second = null;
        try
        {
            await using (await service.BeginAuditedTransactionAsync(db))
            {
            }
        }
        catch (Exception ex) when (ex is not AuditLockHeldException)
        {
            second = ex;
        }

        Assert.NotNull(second);
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
    }

    [Fact]
    public void AuditLockHeldException_is_an_InvalidOperationException()
    {
        var ex = new AuditLockHeldException("held");

        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Equal("held", ex.Message);
    }
}
