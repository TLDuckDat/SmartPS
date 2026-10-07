namespace SmartPS.Services.RolePermissions;

public sealed record RoleInfo(int RoleId, string RoleName, bool IsSystemAdmin);

public sealed record PermissionInfo(int PermissionId, string Name, string Module, string Description);

public sealed record RolePermissionMatrix(
    IReadOnlyList<RoleInfo> Roles,
    IReadOnlyList<PermissionInfo> Permissions,
    IReadOnlyDictionary<int, IReadOnlySet<string>> GrantsByRoleId);

public sealed record RolePermissionChange(
    int RoleId,
    string RoleName,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed);
