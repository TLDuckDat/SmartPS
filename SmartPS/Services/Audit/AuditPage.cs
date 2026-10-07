using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

public sealed record AuditPage(IReadOnlyList<AuditLog> Items, int TotalCount, int PageIndex, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
