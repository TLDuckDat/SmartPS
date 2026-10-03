namespace SmartPS.Models.Auth;

public class Permission
{
    public int PermissionId { get; set; }

    public string PermissionName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();
}