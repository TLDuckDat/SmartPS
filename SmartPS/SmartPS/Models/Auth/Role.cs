namespace SmartPS.Models.Auth;

public class Role
{
    public int RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; }
        = new List<User>();

    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();
}