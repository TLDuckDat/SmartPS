namespace SmartPS.Services.Authorization;

public sealed class PermissionDeniedException : UnauthorizedAccessException
{
    public PermissionDeniedException(IReadOnlyList<string> requiredPermissions, string? username, string reason = "MissingPermission")
        : base("Bạn không có quyền thực hiện thao tác này.")
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
