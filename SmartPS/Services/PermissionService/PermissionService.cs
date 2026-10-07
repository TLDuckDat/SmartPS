namespace SmartPS.Services.Authorization;

public class PermissionService : IPermissionService
{
    private readonly ICurrentUserContext _currentUser;

    public PermissionService(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public bool IsAuthenticated => _currentUser.User is not null;

    public bool HasPermission(string permission)
    {
        // Không đăng nhập = không có quyền gì
        if (_currentUser.User is null)
        {
            return false;
        }

        // Admin là vai trò hệ thống: luôn có toàn bộ quyền
        if (_currentUser.IsSystemAdmin)
        {
            return true;
        }

        return _currentUser.Permissions.Contains(permission);
    }

    public bool HasAnyPermission(params string[] permissions)
    {
        return permissions.Any(HasPermission);
    }

    public bool HasAllPermissions(params string[] permissions)
    {
        return IsAuthenticated && permissions.All(HasPermission);
    }

    public bool IsAdmin()
    {
        return _currentUser.User is not null && _currentUser.IsSystemAdmin;
    }
}
