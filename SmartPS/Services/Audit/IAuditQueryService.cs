namespace SmartPS.Services.Audit;

public interface IAuditQueryService
{
    /// <summary>Returns one page of audit records, newest first. Requires Audit.View.</summary>
    Task<AuditPage> QueryAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default);
}
