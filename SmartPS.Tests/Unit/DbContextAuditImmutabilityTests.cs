using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartPS.Data;
using SmartPS.Models.Auth;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AC-11 / R13 layer 2: SaveChanges rejects Modified/Deleted AuditLog entries before any SQL is sent.
/// Uses an offline context (port 1): reaching the database would surface as a connection error instead.
/// </summary>
public class DbContextAuditImmutabilityTests
{
    private const string OfflineConnection = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";

    private sealed class CountingInterceptor : DbCommandInterceptor, IDbConnectionInterceptor
    {
        public int Commands;
        public int ConnectionOpens;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Commands++;
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult(result);
        }

        public InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            ConnectionOpens++;
            return result;
        }

        public ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            ConnectionOpens++;
            return ValueTask.FromResult(result);
        }
    }

    private static (SmartPsDbContext Db, CountingInterceptor Interceptor) CreateOffline()
    {
        var interceptor = new CountingInterceptor();
        var options = new DbContextOptionsBuilder<SmartPsDbContext>()
            .UseNpgsql(OfflineConnection)
            .AddInterceptors(interceptor)
            .Options;
        return (new SmartPsDbContext(options), interceptor);
    }

    private static AuditLog ExistingRow() => new()
    {
        AuditLogId = 1,
        OccurredAtUtc = DateTime.UtcNow,
        Username = "admin",
        RoleName = "Admin",
        Action = AuditActions.AuthLoginSuccess,
        Outcome = AuditOutcome.Success,
        Details = "{}",
        MachineName = "PC",
        PrevHash = AuditHashing.GenesisHash,
        Hash = new string('a', 64)
    };

    [Fact]
    public void AuditLogs_DbSet_is_mapped_to_AuditLogs_table()
    {
        var (db, _) = CreateOffline();
        using (db)
        {
            var entity = db.Model.FindEntityType(typeof(AuditLog));
            Assert.NotNull(entity);
            Assert.Equal("AuditLogs", entity!.GetTableName());
            Assert.Equal("jsonb", entity.FindProperty(nameof(AuditLog.Details))!.GetColumnType());
            Assert.True(entity.GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(AuditLog.PrevHash)),
                "unique index on PrevHash (E1 fork protection) is missing");
            Assert.Empty(entity.GetForeignKeys()); // no FK from UserId to Users (plan §0.5)
        }
    }

    [Fact]
    public void SaveChanges_with_modified_audit_row_throws_and_sends_no_sql()
    {
        // AC-11: Given app code edits an AuditLog through DbContext, When SaveChanges, Then exception and no SQL.
        var (db, interceptor) = CreateOffline();
        using (db)
        {
            var row = ExistingRow();
            db.AuditLogs.Attach(row);
            row.Username = "tampered";

            Assert.Throws<AuditLogImmutableException>(() => db.SaveChanges());
            Assert.Equal(0, interceptor.Commands);
            Assert.Equal(0, interceptor.ConnectionOpens);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_with_modified_audit_row_throws_and_sends_no_sql()
    {
        var (db, interceptor) = CreateOffline();
        await using (db)
        {
            var row = ExistingRow();
            db.AuditLogs.Attach(row);
            db.Entry(row).State = EntityState.Modified;

            await Assert.ThrowsAsync<AuditLogImmutableException>(() => db.SaveChangesAsync());
            Assert.Equal(0, interceptor.Commands);
            Assert.Equal(0, interceptor.ConnectionOpens);
        }
    }

    [Fact]
    public void SaveChanges_with_deleted_audit_row_throws_and_sends_no_sql()
    {
        var (db, interceptor) = CreateOffline();
        using (db)
        {
            var row = ExistingRow();
            db.AuditLogs.Attach(row);
            db.AuditLogs.Remove(row);

            Assert.Throws<AuditLogImmutableException>(() => db.SaveChanges(acceptAllChangesOnSuccess: false));
            Assert.Equal(0, interceptor.Commands);
            Assert.Equal(0, interceptor.ConnectionOpens);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_with_deleted_audit_row_throws_and_sends_no_sql()
    {
        var (db, interceptor) = CreateOffline();
        await using (db)
        {
            var row = ExistingRow();
            db.AuditLogs.Attach(row);
            db.AuditLogs.Remove(row);

            await Assert.ThrowsAsync<AuditLogImmutableException>(() => db.SaveChangesAsync(acceptAllChangesOnSuccess: true));
            Assert.Equal(0, interceptor.Commands);
            Assert.Equal(0, interceptor.ConnectionOpens);
        }
    }

    [Fact]
    public void Mixed_batch_with_one_modified_audit_row_is_rejected_entirely()
    {
        var (db, interceptor) = CreateOffline();
        using (db)
        {
            db.Users.Add(new User { Username = "x", PasswordHash = "h", FullName = "X", RoleId = 1 });
            var row = ExistingRow();
            db.AuditLogs.Attach(row);
            row.Details = "{\"edited\":true}";

            var ex = Assert.Throws<AuditLogImmutableException>(() => db.SaveChanges());
            Assert.IsAssignableFrom<InvalidOperationException>(ex);
            Assert.Equal(0, interceptor.Commands);
            Assert.Equal(0, interceptor.ConnectionOpens);
        }
    }

    [Fact]
    public async Task Added_audit_row_is_not_blocked_by_the_guard()
    {
        // Append-only: inserts are allowed (here they fail only because the offline server is unreachable).
        var (db, interceptor) = CreateOffline();
        await using (db)
        {
            var row = ExistingRow();
            row.AuditLogId = 0;
            db.AuditLogs.Add(row);

            var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());

            Assert.NotNull(ex);
            Assert.IsNotType<AuditLogImmutableException>(ex);
            Assert.True(interceptor.ConnectionOpens > 0, "the insert should have tried to reach the database");
        }
    }
}
