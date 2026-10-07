using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Auth;
using SmartPS.Services.Audit;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.RolePermissions;

public sealed class RolePermissionService : IRolePermissionService
{
    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;
    private readonly IAuthService _authService;
    private readonly IPermissionService _permissions;

    public RolePermissionService(
        IDbContextFactory<SmartPsDbContext> contextFactory,
        IAuthorizationGuard guard,
        IAuditService audit,
        IAuthService authService,
        IPermissionService permissions)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<RolePermissionMatrix> GetMatrixAsync(CancellationToken cancellationToken = default)
    {
        await _guard.DemandAsync(Permissions.RoleView, "Role", null, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.RoleId).ToListAsync(cancellationToken);
        var permissions = (await db.Permissions.AsNoTracking().ToListAsync(cancellationToken))
            .Select(p =>
            {
                var module = TryGetModule(p.PermissionName);
                return new PermissionInfo(p.PermissionId, p.PermissionName, module, p.Description);
            })
            .OrderBy(p => ModuleIndex(p.Module))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var grants = await db.RolePermissions
            .AsNoTracking()
            .Select(rp => new { rp.RoleId, rp.Permission.PermissionName })
            .ToListAsync(cancellationToken);
        var grantsByRole = grants
            .GroupBy(g => g.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PermissionName).ToHashSet(StringComparer.Ordinal));

        var roleInfos = new List<RoleInfo>();
        var result = new Dictionary<int, IReadOnlySet<string>>();
        foreach (var role in roles)
        {
            var isAdmin = SystemRoles.IsSystemAdmin(role.RoleName);
            roleInfos.Add(new RoleInfo(role.RoleId, role.RoleName, isAdmin));

            // Admin là vai trò hệ thống: luôn có toàn bộ quyền
            result[role.RoleId] = isAdmin
                ? permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                : grantsByRole.TryGetValue(role.RoleId, out var set) ? set : new HashSet<string>(StringComparer.Ordinal);
        }

        return new RolePermissionMatrix(roleInfos, permissions, result);
    }

    public async Task<IReadOnlyList<RolePermissionChange>> SaveAsync(
        IReadOnlyDictionary<int, IReadOnlyCollection<string>> desiredGrantsByRoleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desiredGrantsByRoleId);

        await _guard.DemandAsync(Permissions.RoleManage, "Role", null, cancellationToken);

        // Chỉ Admin hệ thống mới được lưu ma trận phân quyền (Role.Manage của vai trò khác là chưa đủ)
        if (!_permissions.IsAdmin())
        {
            await _guard.DenyAsync(new[] { SystemRoles.AdminRoleRequirement }, "AdminRoleRequired", "Role", null, cancellationToken);
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Kiểm tra đầu vào (chưa giữ khoá nhật ký)
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleId, cancellationToken);
        var permissionIds = await db.Permissions.AsNoTracking()
            .ToDictionaryAsync(p => p.PermissionName, p => p.PermissionId, StringComparer.Ordinal, cancellationToken);

        foreach (var (roleId, desired) in desiredGrantsByRoleId)
        {
            if (!roles.TryGetValue(roleId, out var role))
            {
                throw new ArgumentException($"Vai trò không tồn tại: {roleId}.", nameof(desiredGrantsByRoleId));
            }

            if (SystemRoles.IsSystemAdmin(role.RoleName))
            {
                throw new InvalidOperationException("Vai trò Admin là vai trò hệ thống và không thể chỉnh sửa quyền.");
            }

            var unknown = desired.FirstOrDefault(p => !permissionIds.ContainsKey(p));
            if (unknown is not null)
            {
                throw new ArgumentException($"Quyền không tồn tại: {unknown}.", nameof(desiredGrantsByRoleId));
            }
        }

        var changes = new List<RolePermissionChange>();
        await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
        {
            var targetRoleIds = desiredGrantsByRoleId.Keys.ToList();
            var current = await db.RolePermissions
                .Include(rp => rp.Permission)
                .Where(rp => targetRoleIds.Contains(rp.RoleId))
                .ToListAsync(cancellationToken);

            foreach (var roleId in targetRoleIds.OrderBy(id => id))
            {
                var role = roles[roleId];
                var desired = desiredGrantsByRoleId[roleId].ToHashSet(StringComparer.Ordinal);
                var existing = current.Where(rp => rp.RoleId == roleId).ToList();
                var existingNames = existing.Select(rp => rp.Permission.PermissionName).ToHashSet(StringComparer.Ordinal);

                var added = desired.Except(existingNames).OrderBy(n => n, StringComparer.Ordinal).ToList();
                var removed = existingNames.Except(desired).OrderBy(n => n, StringComparer.Ordinal).ToList();
                if (added.Count == 0 && removed.Count == 0)
                {
                    continue;
                }

                db.RolePermissions.RemoveRange(existing.Where(rp => removed.Contains(rp.Permission.PermissionName)));
                foreach (var name in added)
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionIds[name] });
                }

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.RolePermissionsUpdate,
                    AuditOutcome.Success,
                    "Role",
                    roleId.ToString(),
                    new { RoleName = role.RoleName, Added = added, Removed = removed }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                changes.Add(new RolePermissionChange(roleId, role.RoleName, added, removed));
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // Quyền của người dùng hiện tại có thể vừa thay đổi: nạp lại từ cơ sở dữ liệu
        if (changes.Count > 0)
        {
            await _authService.ReloadCurrentUserAsync(cancellationToken);
        }

        return changes;
    }

    private static string TryGetModule(string permissionName)
    {
        try
        {
            return Permissions.GetModule(permissionName);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static int ModuleIndex(string module)
    {
        var index = Permissions.ModuleOrder.ToList().IndexOf(module);
        return index < 0 ? int.MaxValue : index;
    }
}
