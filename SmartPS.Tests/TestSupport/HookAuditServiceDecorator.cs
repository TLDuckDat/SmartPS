using System.Data;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Runs a synchronous hook right before the real audited transaction is opened, i.e. after the service's
/// pre-transaction checks (TOCTOU probes, FX3/FX9). Begin is forwarded synchronously (non-async, addendum N4).
/// Also counts calls so offline tests can assert that no audited transaction was attempted (FX12).
/// </summary>
public sealed class HookAuditServiceDecorator : IAuditService
{
    private readonly IAuditService _inner;
    private readonly Action? _beforeBegin;
    private int _beginCalls;

    public HookAuditServiceDecorator(IAuditService inner, Action? beforeBegin = null)
    {
        _inner = inner;
        _beforeBegin = beforeBegin;
    }

    public int BeginCalls => Volatile.Read(ref _beginCalls);

    public Task<AuditedTransaction> BeginAuditedTransactionAsync(
        SmartPsDbContext db,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _beginCalls);
        _beforeBegin?.Invoke();
        return _inner.BeginAuditedTransactionAsync(db, isolationLevel, cancellationToken);
    }

    public Task<AuditLog> AppendAsync(SmartPsDbContext db, AuditEntry entry, CancellationToken cancellationToken = default)
        => _inner.AppendAsync(db, entry, cancellationToken);

    public Task<bool> LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        => _inner.LogAsync(entry, cancellationToken);
}
