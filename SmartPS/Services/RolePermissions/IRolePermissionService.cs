namespace SmartPS.Services.RolePermissions;

public interface IRolePermissionService
{
    /// <summary>Reads the role x permission matrix. Requires Role.View.</summary>
    Task<RolePermissionMatrix> GetMatrixAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the desired grants for the given roles (roles not in the dictionary are untouched) and returns the roles
    /// that actually changed. Requires Role.Manage. The built-in Admin role cannot be edited.
    /// </summary>
    Task<IReadOnlyList<RolePermissionChange>> SaveAsync(
        IReadOnlyDictionary<int, IReadOnlyCollection<string>> desiredGrantsByRoleId,
        CancellationToken cancellationToken = default);
}
