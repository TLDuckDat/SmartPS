using SmartPS.Constants;
using SmartPS.Services.Auth;

namespace SmartPS.Services.Authorization;

public interface IPermissionService
{
    bool HasPermission(string permission);

    bool HasAnyPermission(params string[] permissions);

    bool HasAllPermissions(params string[] permissions);

    bool IsAdmin();
}