using SmartPS.Constants;
using SmartPS.Services.Auth;

namespace SmartPS.Services.Authorization;

public class PermissionService : IPermissionService
{
    private readonly IAuthService _authService;

    public PermissionService(IAuthService authService)
    {
        _authService = authService;
    }

    public bool HasPermission(string permission)
    {
        var user = _authService.CurrentUser;

        if (user is null) return false;

        // return user.Role.RolePermissions.Any(x => x.Permission.PermissionName == permission);
        // bổ sung toán tử ? để tránh trường hợp user.Role hoặc user.Role.RolePermissions là null, tránh lỗi NullReferenceException
        return user?.Role?.RolePermissions?.Any(x => x.Permission?.PermissionName == permission) ?? false;
    }

    public bool HasAnyPermission(params string[] permissions)
    {
        return permissions.Any(HasPermission);
    }

    public bool HasAllPermissions(params string[] permissions)
    {
        return permissions.All(HasPermission);
    }

    public bool IsAdmin()
    {
        return _authService.CurrentUser?.Role.RoleName.Equals("Admin", 
               StringComparison.OrdinalIgnoreCase) == true;
    }
}