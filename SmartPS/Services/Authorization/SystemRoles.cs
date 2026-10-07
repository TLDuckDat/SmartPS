namespace SmartPS.Services.Authorization;

/// <summary>The only place where a role name is used for an access decision: the built-in Admin role.</summary>
public static class SystemRoles
{
    public const string Admin = "Admin";

    /// <summary>Marker stored in ACCESS_DENIED.requiredPermissions when a system admin is required.</summary>
    public const string AdminRoleRequirement = "Role:Admin";

    public static bool IsSystemAdmin(string? roleName)
        => string.Equals(roleName, Admin, StringComparison.OrdinalIgnoreCase);
}
