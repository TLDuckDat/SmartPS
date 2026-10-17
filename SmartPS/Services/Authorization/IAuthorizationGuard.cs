namespace SmartPS.Services.Authorization;

/// <summary>
/// Service-level permission checks. A denial writes an ACCESS_DENIED audit record and throws
/// <see cref="PermissionDeniedException"/>. Every method throws AuditLockHeldException when called while the
/// audit chain lock is held by the current flow, so checks must run before the audited transaction opens.
/// </summary>
public interface IAuthorizationGuard
{
    /// <summary>Id of the logged-in user, or null when nobody is logged in.</summary>
    int? CurrentUserId { get; }

    Task DemandAsync(string permission, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);

    Task DemandAnyAsync(IReadOnlyCollection<string> permissions, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);

    /// <summary>Denies (ActorMismatch) when nobody is logged in or the actor id differs from the logged-in user, then checks the permission.</summary>
    Task DemandActorAsync(int actorUserId, string permission, string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);

    /// <summary>Always logs ACCESS_DENIED and throws <see cref="PermissionDeniedException"/> with the given reason.</summary>
    Task DenyAsync(IReadOnlyCollection<string> requiredPermissions, string reason = "MissingPermission", string? entityType = null, string? entityId = null, CancellationToken cancellationToken = default);
}
