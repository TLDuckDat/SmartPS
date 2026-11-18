using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Customers;

internal static class CustomersAuditExtensions
{
    /// <summary>Ghi một dòng audit thành công với Details { Old, New, Context } trong audited transaction hiện tại.</summary>
    public static Task<AuditLog> AppendChangeAsync(
        this IAuditService audit,
        SmartPsDbContext db,
        string action,
        string entityType,
        object entityId,
        object? oldValue,
        object? newValue,
        CancellationToken ct,
        object? context = null)
        => audit.AppendAsync(
            db,
            new AuditEntry(action, AuditOutcome.Success, entityType, entityId.ToString(), new { Old = oldValue, New = newValue, Context = context }),
            ct);
}
