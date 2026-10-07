using Microsoft.EntityFrameworkCore.Storage;

namespace SmartPS.Services.Audit;

/// <summary>
/// Database transaction that holds the audit chain lock. Use the block form
/// "await using (var tx = await ...) { ... }" so the transaction is rolled back and the lock released
/// before any code that runs after the block.
/// </summary>
public sealed class AuditedTransaction : IAsyncDisposable
{
    private readonly Func<Task> _release;
    private bool _rolledBack;
    private bool _released;

    internal AuditedTransaction(IDbContextTransaction transaction, Func<Task> release)
    {
        Transaction = transaction;
        _release = release;
    }

    public IDbContextTransaction Transaction { get; }

    public bool IsCommitted { get; private set; }

    internal bool IsActive => !IsCommitted && !_rolledBack && !_released;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.CommitAsync(cancellationToken);
        IsCommitted = true;
        await ReleaseOnceAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted || _rolledBack)
        {
            return;
        }

        _rolledBack = true;
        try
        {
            await Transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await ReleaseOnceAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsCommitted && !_rolledBack)
        {
            _rolledBack = true;
            try
            {
                await Transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // The connection may already be broken; the lock is released with the session below.
            }
        }

        try
        {
            await Transaction.DisposeAsync();
        }
        catch
        {
            // Nothing left to clean up.
        }

        await ReleaseOnceAsync();
    }

    private async Task ReleaseOnceAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        await _release();
    }
}
