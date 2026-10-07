using SmartPS.Models.Auth;

namespace SmartPS.Services.Authorization;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private static readonly Snapshot Empty = new(null, new HashSet<string>(StringComparer.Ordinal), false);

    private volatile Snapshot _snapshot = Empty;

    public User? User => _snapshot.User;

    public IReadOnlySet<string> Permissions => _snapshot.Permissions;

    public bool IsSystemAdmin => _snapshot.IsSystemAdmin;

    public event EventHandler? Changed;

    public void SetUser(User? user)
    {
        if (user is null)
        {
            _snapshot = Empty;
        }
        else
        {
            var permissions = new HashSet<string>(StringComparer.Ordinal);
            var rolePermissions = user.Role?.RolePermissions;
            if (rolePermissions is not null)
            {
                foreach (var rolePermission in rolePermissions)
                {
                    var name = rolePermission.Permission?.PermissionName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        permissions.Add(name);
                    }
                }
            }

            _snapshot = new Snapshot(user, permissions, SystemRoles.IsSystemAdmin(user.Role?.RoleName));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Snapshot(User? User, IReadOnlySet<string> Permissions, bool IsSystemAdmin);
}
