using System.Data;
using SmartPS.Data;
using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

public interface IAuditService
{
    /// <summary>
    /// Opens a transaction while holding the audit chain lock. Implementations must forward synchronously (non-async)
    /// so the lock marker flows into the calling method. Throws <see cref="AuditLockHeldException"/> when the lock is
    /// already held in the current flow and <see cref="InvalidOperationException"/> when the context already has a transaction.
    /// </summary>
    Task<AuditedTransaction> BeginAuditedTransactionAsync(
        SmartPsDbContext db,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>Adds one chained audit row to the context inside its audited transaction.</summary>
    Task<AuditLog> AppendAsync(SmartPsDbContext db, AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one audit row in its own transaction. Returns false when the audit store is unavailable.
    /// Throws only <see cref="OperationCanceledException"/> (caller token) or <see cref="AuditLockHeldException"/>.
    /// </summary>
    Task<bool> LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
