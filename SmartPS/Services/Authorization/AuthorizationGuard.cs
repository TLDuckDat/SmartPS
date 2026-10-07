using SmartPS.Constants;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Authorization;

public sealed class AuthorizationGuard : IAuthorizationGuard
{
    private readonly IPermissionService _permissions;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditService _auditService;

    public AuthorizationGuard(IPermissionService permissions, ICurrentUserContext currentUser, IAuditService auditService)
    {
        _permissions = permissions;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public int? CurrentUserId => _currentUser.User?.UserId;

    public Task DemandAsync(string permission, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default)
    {
        return DemandAnyAsync(new[] { permission }, entityType, entityId, cancellationToken);
    }

    public async Task DemandAnyAsync(IReadOnlyCollection<string> permissions, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default)
    {
        EnsureAuditLockNotHeld();

        if (permissions.Any(_permissions.HasPermission))
        {
            return;
        }

        var reason = _currentUser.User is null ? "NotAuthenticated" : "MissingPermission";
        await DenyCoreAsync(permissions, reason, entityType, entityId, null, cancellationToken);
    }

    public async Task DemandActorAsync(int actorUserId, string permission, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default)
    {
        EnsureAuditLockNotHeld();

        var user = _currentUser.User;
        if (user is null || user.UserId != actorUserId)
        {
            await DenyCoreAsync(new[] { permission }, "ActorMismatch", entityType, entityId, actorUserId, cancellationToken);
            return;
        }

        if (_permissions.HasPermission(permission))
        {
            return;
        }

        await DenyCoreAsync(new[] { permission }, "MissingPermission", entityType, entityId, null, cancellationToken);
    }

    public async Task DenyAsync(IReadOnlyCollection<string> requiredPermissions, string reason = "MissingPermission", string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default)
    {
        EnsureAuditLockNotHeld();
        await DenyCoreAsync(requiredPermissions, reason, entityType, entityId, null, cancellationToken);
    }

    private async Task DenyCoreAsync(
        IReadOnlyCollection<string> required,
        string reason,
        string? entityType,
        string? entityId,
        int? actorUserIdRequested,
        CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, object?>
        {
            ["requiredPermissions"] = required.ToArray(),
            ["reason"] = reason
        };
        if (actorUserIdRequested.HasValue)
        {
            details["actorUserIdRequested"] = actorUserIdRequested.Value;
        }

        await _auditService.LogAsync(
            new AuditEntry(AuditActions.AccessDenied, AuditOutcome.Denied, entityType, entityId, details),
            cancellationToken);

        throw new PermissionDeniedException(required.ToArray(), _currentUser.User?.Username, reason);
    }

    private static void EnsureAuditLockNotHeld()
    {
        if (AuditService.IsLockHeldInCurrentFlow)
        {
            throw new AuditLockHeldException(
                "Authorization checks must run before the audited transaction is opened.");
        }
    }
}
