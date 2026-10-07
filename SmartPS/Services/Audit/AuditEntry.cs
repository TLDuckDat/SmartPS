using SmartPS.Constants;
using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

/// <summary>Request to write one audit record. Details is serialized to canonical JSON when the record is built.</summary>
public sealed record AuditEntry(
    string Action,
    AuditOutcome Outcome,
    string? EntityType = null,
    string? EntityId = null,
    object? Details = null,
    AuditActor? Actor = null)
{
    public static AuditEntry AccessDenied(
        IReadOnlyCollection<string> requiredPermissions,
        string reason = "MissingPermission",
        string? entityType = null,
        string? entityId = null,
        AuditActor? actor = null)
    {
        return new AuditEntry(
            AuditActions.AccessDenied,
            AuditOutcome.Denied,
            entityType,
            entityId,
            new { RequiredPermissions = requiredPermissions.ToArray(), Reason = reason },
            actor);
    }
}
