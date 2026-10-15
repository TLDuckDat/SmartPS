using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Throws <see cref="ExceptionFactory"/> on the first EF command whose SQL contains <c>match</c> AND that runs inside a
/// database transaction (the audited check-in transaction). Commands outside a transaction (classification) pass.
/// Install with <see cref="InterceptorInstall.With"/>.
/// </summary>
public sealed class InTransactionFaultInterceptor : DbCommandInterceptor
{
    private readonly string _match;
    private readonly Func<Exception> _exceptionFactory;
    private int _fired;

    public InTransactionFaultInterceptor(string match, Func<Exception> exceptionFactory)
    {
        _match = match;
        _exceptionFactory = exceptionFactory;
    }

    public int FiredCount => Volatile.Read(ref _fired);

    public Func<Exception> ExceptionFactory => _exceptionFactory;

    private void MaybeThrow(DbCommand command)
    {
        if (command.Transaction is null || !command.CommandText.Contains(_match, StringComparison.Ordinal))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
        {
            throw _exceptionFactory();
        }
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        MaybeThrow(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        MaybeThrow(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        MaybeThrow(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        MaybeThrow(command);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        MaybeThrow(command);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        MaybeThrow(command);
        return result;
    }
}
