using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SmartPS.Data;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// EF command interceptor that fires once, on the <c>occurrence</c>-th command whose SQL text contains <c>match</c>:
/// it first awaits <c>before</c> (if any), then either throws a connection-style <see cref="NpgsqlException"/>
/// (<c>throwInstead</c>) or lets the command run. Only EF-issued commands are seen (raw ADO commands are not intercepted).
/// </summary>
public sealed class CommandHookInterceptor : DbCommandInterceptor
{
    private readonly string _match;
    private readonly int _occurrence;
    private readonly Func<Task>? _before;
    private readonly bool _throwInstead;
    private readonly Func<Exception>? _exceptionFactory;
    private int _seen;
    private int _fired;

    /// <param name="exceptionFactory">With <paramref name="throwInstead"/>: the exception to throw instead of the default
    /// connection-style <see cref="NpgsqlException"/> (e.g. a non-connection failure for fail-closed tests).</param>
    public CommandHookInterceptor(string match, int occurrence = 1, Func<Task>? before = null, bool throwInstead = false, Func<Exception>? exceptionFactory = null)
    {
        _match = match;
        _occurrence = occurrence;
        _before = before;
        _throwInstead = throwInstead;
        _exceptionFactory = exceptionFactory;
    }

    /// <summary>Number of times the hook fired (0 or 1).</summary>
    public int FiredCount => Volatile.Read(ref _fired);

    /// <summary>SQL text of the command the hook fired on.</summary>
    public string? FiredOnCommandText { get; private set; }

    private async Task MaybeFireAsync(DbCommand command)
    {
        if (!command.CommandText.Contains(_match, StringComparison.Ordinal))
        {
            return;
        }

        if (Interlocked.Increment(ref _seen) != _occurrence)
        {
            return;
        }

        Interlocked.Increment(ref _fired);
        FiredOnCommandText = command.CommandText;
        if (_before is not null)
        {
            await _before();
        }

        if (_throwInstead && _exceptionFactory is not null)
        {
            throw _exceptionFactory();
        }

        if (_throwInstead)
        {
            throw new NpgsqlException(
                "Simulated connection failure (test hook)",
                new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        }
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await MaybeFireAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await MaybeFireAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await MaybeFireAsync(command);
        return result;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        MaybeFireAsync(command).GetAwaiter().GetResult();
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        MaybeFireAsync(command).GetAwaiter().GetResult();
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        MaybeFireAsync(command).GetAwaiter().GetResult();
        return result;
    }

    /// <summary>
    /// Override for <see cref="IntegrationServices.Create"/>: replaces the container's context factory with one whose
    /// contexts carry <paramref name="hook"/>. Test helpers (ResidentVisitorData, AuditDb, fixture.Factory) stay unhooked.
    /// </summary>
    public static Action<IServiceCollection> Install(PostgresDatabaseFixture fixture, CommandHookInterceptor hook) => services =>
    {
        services.RemoveAll<IDbContextFactory<SmartPsDbContext>>();
        services.AddSingleton<IDbContextFactory<SmartPsDbContext>>(new HookedFactory(fixture.ConnectionString, hook));
    };

    private sealed class HookedFactory : IDbContextFactory<SmartPsDbContext>
    {
        private readonly DbContextOptions<SmartPsDbContext> _options;

        public HookedFactory(string connectionString, CommandHookInterceptor hook)
        {
            _options = new DbContextOptionsBuilder<SmartPsDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(hook)
                .Options;
        }

        public SmartPsDbContext CreateDbContext() => new(_options);
    }
}
