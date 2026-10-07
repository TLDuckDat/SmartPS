using System.Data;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Wraps the real audit service and fails <see cref="AppendAsync"/> for one action (T-R15a).
/// BeginAuditedTransactionAsync forwards synchronously (non-async) as required by plan addendum N4,
/// so the lock-held marker still flows into the calling service method.
/// </summary>
public sealed class ThrowingAuditServiceDecorator : IAuditService
{
    private readonly IAuditService _inner;

    public ThrowingAuditServiceDecorator(IAuditService inner, string failingAction)
    {
        _inner = inner;
        FailingAction = failingAction;
    }

    public string FailingAction { get; }

    public int FailuresThrown { get; private set; }

    public Task<AuditedTransaction> BeginAuditedTransactionAsync(
        SmartPsDbContext db,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
        => _inner.BeginAuditedTransactionAsync(db, isolationLevel, cancellationToken);

    public Task<AuditLog> AppendAsync(SmartPsDbContext db, AuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.Equals(entry.Action, FailingAction, StringComparison.Ordinal))
        {
            FailuresThrown++;
            throw new InvalidOperationException($"Simulated audit append failure for {entry.Action}.");
        }

        return _inner.AppendAsync(db, entry, cancellationToken);
    }

    public Task<bool> LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        => _inner.LogAsync(entry, cancellationToken);
}
