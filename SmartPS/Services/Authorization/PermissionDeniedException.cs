using SmartPS.Services.Localization;

namespace SmartPS.Services.Authorization;

public sealed class PermissionDeniedException : UnauthorizedAccessException
{
    public PermissionDeniedException(IReadOnlyList<string> requiredPermissions, string? username, string reason = "MissingPermission")
        : base(AppText.Get("Msg_Inc_NoPermission"))
    {
        RequiredPermissions = requiredPermissions;
        Username = username;
        Reason = reason;
    }

    public IReadOnlyList<string> RequiredPermissions { get; }

    public string? Username { get; }

    /// <summary>"MissingPermission", "ActorMismatch", "NotAuthenticated" or "AdminRoleRequired".</summary>
    public string Reason { get; }
}
