using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Audit;

public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;

    public AuditQueryService(IDbContextFactory<SmartPsDbContext> contextFactory, IAuthorizationGuard guard)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public async Task<AuditPage> QueryAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await _guard.DemandAsync(Permissions.AuditView, "AuditLog", null, cancellationToken);

        var pageSize = Math.Clamp(filter.PageSize, 1, AuditQueryFilter.MaxPageSize);
        var pageIndex = Math.Max(0, filter.PageIndex);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<AuditLog> query;
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var pattern = "%" + EscapeLike(filter.SearchText.Trim()) + "%";
            query = db.AuditLogs
                .FromSqlInterpolated($"SELECT * FROM \"AuditLogs\" WHERE \"EntityId\" ILIKE {pattern} OR \"Details\"::text ILIKE {pattern}")
                .AsNoTracking();
        }
        else
        {
            query = db.AuditLogs.AsNoTracking();
        }

        if (filter.FromDateVn.HasValue)
        {
            var from = AuditTime.VietnamDateStartUtc(filter.FromDateVn.Value);
            query = query.Where(a => a.OccurredAtUtc >= from);
        }

        if (filter.ToDateVn.HasValue)
        {
            var to = AuditTime.VietnamDateStartUtc(filter.ToDateVn.Value.AddDays(1));
            query = query.Where(a => a.OccurredAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            var userTerm = filter.Username.Trim().ToLowerInvariant();
            query = query.Where(a => a.Username.ToLower().Contains(userTerm));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action);
        }

        if (filter.Outcome.HasValue)
        {
            var outcome = filter.Outcome.Value;
            query = query.Where(a => a.Outcome == outcome);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.AuditLogId)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AuditPage(items, total, pageIndex, pageSize);
    }

    /// <summary>Escapes the LIKE wildcards (and the escape character itself) so user input is matched literally.</summary>
    private static string EscapeLike(string term)
    {
        var builder = new StringBuilder(term.Length + 4);
        foreach (var ch in term)
        {
            if (ch is '\\' or '%' or '_')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
