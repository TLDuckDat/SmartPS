namespace SmartPS.Services.Authorization;

public interface IPermissionService
{
    /// <summary>True when a user is logged in.</summary>
    bool IsAuthenticated { get; }

    bool HasPermission(string permission);

    bool HasAnyPermission(params string[] permissions);

    /// <summary>False when nobody is logged in.</summary>
    bool HasAllPermissions(params string[] permissions);

    bool IsAdmin();
}
