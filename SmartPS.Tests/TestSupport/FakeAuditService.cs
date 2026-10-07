using System.Collections.Concurrent;
using System.Data;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Records standalone audit writes (LogAsync). Transactional members are not supported in unit tests.
/// </summary>
public sealed class FakeAuditService : IAuditService
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();

    public bool LogResult { get; set; } = true;

    public Task<AuditedTransaction> BeginAuditedTransactionAsync(
        SmartPsDbContext db,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeAuditService does not support audited transactions.");

    public Task<AuditLog> AppendAsync(SmartPsDbContext db, AuditEntry entry, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeAuditService does not support AppendAsync.");

    public Task<bool> LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
        return Task.FromResult(LogResult);
    }
}
