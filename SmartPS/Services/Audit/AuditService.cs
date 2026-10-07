using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Audit;

/// <summary>
/// Writes the hash-chained audit trail. Appends are serialized by a PostgreSQL session advisory lock that is taken
/// before the transaction starts, so the chain head read inside the transaction is always the latest committed row.
/// While the lock is held only database statements may run (no HTTP, no file I/O, no UI).
/// </summary>
public sealed class AuditService : IAuditService
{
    public const long AuditChainLockKey = 0x534D_5053_4155_4454L;

    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    private static readonly AsyncLocal<LockMarker?> s_marker = new();
    private static readonly ConditionalWeakTable<SmartPsDbContext, AuditedTransaction> s_transactions = new();

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditService(
        IDbContextFactory<SmartPsDbContext> contextFactory,
        ICurrentUserContext currentUser,
        TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static bool IsLockHeldInCurrentFlow => s_marker.Value?.IsHeld == true;

    // Deliberately NOT async: the AsyncLocal marker must be assigned in the caller's execution context.
    public Task<AuditedTransaction> BeginAuditedTransactionAsync(
        SmartPsDbContext db,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (IsLockHeldInCurrentFlow)
        {
            return Task.FromException<AuditedTransaction>(new AuditLockHeldException(
                "An audited transaction is already open in this flow; nested audited transactions would deadlock."));
        }

        if (db.Database.CurrentTransaction is not null)
        {
            return Task.FromException<AuditedTransaction>(new InvalidOperationException(
                "The context already has an active transaction."));
        }

        var marker = new LockMarker { IsHeld = true };
        s_marker.Value = marker;
        return BeginCoreAsync(db, isolationLevel, marker, cancellationToken).AsTask();
    }

    public async Task<AuditLog> AppendAsync(SmartPsDbContext db, AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entry);

        if (!s_transactions.TryGetValue(db, out var transaction) || !transaction.IsActive)
        {
            throw new InvalidOperationException("AppendAsync requires an open audited transaction on this context.");
        }

        if (db.ChangeTracker.Entries<AuditLog>().Any(e => e.State == EntityState.Added))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var prevHash = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.AuditLogId)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(cancellationToken) ?? AuditHashing.GenesisHash;

        var actor = entry.Actor
            ?? (_currentUser.User is { } user ? AuditActor.FromUser(user) : AuditActor.Anonymous);

        var row = AuditRecordFactory.Create(
            entry,
            actor,
            _timeProvider.GetUtcNow().UtcDateTime,
            Environment.MachineName,
            prevHash);

        db.AuditLogs.Add(row);
        return row;
    }

    public async Task<bool> LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsLockHeldInCurrentFlow)
        {
            throw new AuditLockHeldException(
                "LogAsync was called while the audit chain lock is held by the current flow.");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await BeginAuditedTransactionAsync(db, IsolationLevel.ReadCommitted, cancellationToken))
            {
                await AppendAsync(db, entry, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuditLockHeldException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Audit] Could not write audit record {entry.Action}: {ex.GetType().Name}");
            return false;
        }
    }

    private static async ValueTask<AuditedTransaction> BeginCoreAsync(
        SmartPsDbContext db,
        IsolationLevel isolationLevel,
        LockMarker marker,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var lockPossiblyHeld = false;
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(LockTimeout);
            lockPossiblyHeld = true;
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
                command.Parameters.AddWithValue("key", AuditChainLockKey);
                await command.ExecuteNonQueryAsync(timeoutSource.Token);
            }
            catch (Exception) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for the audit chain lock.");
            }

            var transaction = await db.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            var audited = new AuditedTransaction(transaction, () => ReleaseAsync(db, connection, marker, lockPossiblyHeld: true));
            s_transactions.AddOrUpdate(db, audited);
            return audited;
        }
        catch
        {
            await ReleaseAsync(db, connection, marker, lockPossiblyHeld);
            throw;
        }
    }

    private static async Task ReleaseAsync(SmartPsDbContext db, NpgsqlConnection connection, LockMarker marker, bool lockPossiblyHeld)
    {
        s_transactions.Remove(db);
        try
        {
            if (lockPossiblyHeld && connection.State == ConnectionState.Open)
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock_all()", connection);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        catch
        {
            // The connection may still own the lock: never hand it back to the pool.
            try
            {
                NpgsqlConnection.ClearPool(connection);
            }
            catch
            {
                // Best effort.
            }
        }

        try
        {
            await db.Database.CloseConnectionAsync();
        }
        catch
        {
            // Best effort.
        }

        marker.IsHeld = false;
    }

    private sealed class LockMarker
    {
        public volatile bool IsHeld;
    }
}
