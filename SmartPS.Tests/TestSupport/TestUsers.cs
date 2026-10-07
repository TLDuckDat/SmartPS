using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Auth;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Builds in-memory user graphs for unit tests and inserts real users for integration tests.
/// </summary>
public static class TestUsers
{
    public const string DefaultPassword = "Test@123";

    /// <summary>Seed admin account (seed_data.sql).</summary>
    public const string SeedAdminUsername = "admin";
    public const string SeedAdminPassword = "Admin@123";

    /// <summary>Total number of permissions after the resident/visitor flow (23 RBAC + 3 customer/blacklist).</summary>
    public const int TotalPermissionCount = 26;

    /// <summary>The 3 permissions added by the resident/visitor flow (R8).</summary>
    public static readonly IReadOnlyList<string> CustomerPermissions = new[]
    {
        Permissions.CustomerView, Permissions.CustomerManage, Permissions.BlacklistManage
    };

    /// <summary>Operator grants written by the AddRbacAndAuditTrail migration (the original 7).</summary>
    public static readonly IReadOnlyList<string> OperatorRbacMigrationPermissions = new[]
    {
        Permissions.ParkingView, Permissions.ParkingCheckIn, Permissions.ParkingCheckOut, Permissions.ReportView,
        Permissions.ShiftView, Permissions.ShiftOpen, Permissions.ShiftClose
    };

    /// <summary>Manager grants written by the AddRbacAndAuditTrail migration (the original 17, spec §6.1 of the RBAC task).</summary>
    public static readonly IReadOnlyList<string> ManagerRbacMigrationPermissions = new[]
    {
        Permissions.ReportView, Permissions.ReportExport, Permissions.PricingManage,
        Permissions.ParkingView, Permissions.ParkingCheckIn, Permissions.ParkingCheckOut, Permissions.ParkingConfigure,
        Permissions.ShiftView, Permissions.ShiftOpen, Permissions.ShiftClose, Permissions.ShiftReview, Permissions.ShiftAdjust,
        Permissions.PaymentRefund, Permissions.UserView, Permissions.RoleView, Permissions.AuditView, Permissions.IncidentManage
    };

    /// <summary>Operator grants from seed_data.sql (RBAC grants + Customer.View).</summary>
    public static readonly IReadOnlyList<string> OperatorSeedPermissions =
        OperatorRbacMigrationPermissions.Append(Permissions.CustomerView).ToArray();

    /// <summary>Manager defaults (RBAC spec §6.1 + the 3 customer/blacklist permissions).</summary>
    public static readonly IReadOnlyList<string> ManagerDefaultPermissions =
        ManagerRbacMigrationPermissions.Concat(CustomerPermissions).ToArray();

    /// <summary>The 18 permissions that existed before this task (fb7303a).</summary>
    public static readonly IReadOnlyList<string> LegacyPermissions = new[]
    {
        "User.View", "User.Create", "User.Edit", "User.Delete", "Role.View", "Role.Manage",
        "Parking.View", "Parking.CheckIn", "Parking.CheckOut", "Parking.Configure",
        "Pricing.Manage", "Report.View", "Report.Export",
        "Shift.View", "Shift.Open", "Shift.Close", "Shift.Review", "Shift.Adjust"
    };

    /// <summary>The 5 permissions added by this task (R2).</summary>
    public static readonly IReadOnlyList<string> NewPermissions = new[]
    {
        "Audit.View", "Audit.Verify", "Payment.Refund", "Settings.Manage", "Incident.Manage"
    };

    private static int s_nextId = 1000;

    /// <summary>In-memory user whose role carries exactly <paramref name="permissions"/>.</summary>
    public static User Build(string roleName, params string[] permissions)
    {
        var userId = Interlocked.Increment(ref s_nextId);
        var role = new Role
        {
            RoleId = (StringComparer.Ordinal.GetHashCode(roleName) & 0x7FFFFFFF) % 1000 + 1,
            RoleName = roleName,
            Description = roleName
        };

        var permissionId = 1;
        foreach (var name in permissions)
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.RoleId,
                Role = role,
                PermissionId = permissionId,
                Permission = new Permission { PermissionId = permissionId, PermissionName = name, Description = name }
            });
            permissionId++;
        }

        return new User
        {
            UserId = userId,
            Username = $"{roleName.ToLowerInvariant()}_{userId}",
            FullName = $"Test {roleName} {userId}",
            IsActive = true,
            RoleId = role.RoleId,
            Role = role
        };
    }

    public static User Operator() => Build("Operator", OperatorSeedPermissions.ToArray());

    public static User Manager() => Build("Manager", ManagerDefaultPermissions.ToArray());

    /// <summary>System admin; deliberately carries no RolePermissions to prove the admin bypass.</summary>
    public static User AdminWithoutGrants() => Build("Admin");

    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 40)].ToLowerInvariant();

    /// <summary>Inserts a user directly (bypassing AuthService) with a BCrypt hash.</summary>
    public static async Task<User> CreateAsync(
        IDbContextFactory<SmartPsDbContext> factory,
        string roleName,
        string password = DefaultPassword,
        bool isActive = true,
        string? username = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var role = await db.Roles.SingleAsync(r => r.RoleName == roleName);
        var user = new User
        {
            Username = username ?? UniqueName($"t_{roleName}"),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4),
            FullName = $"Test {roleName}",
            IsActive = isActive,
            RoleId = role.RoleId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        user.Role = role;
        return user;
    }

    /// <summary>Creates a non-system role with the given grants (used for AC-6b and R7 tests).</summary>
    public static async Task<Role> CreateRoleAsync(IDbContextFactory<SmartPsDbContext> factory, string roleName, params string[] permissions)
    {
        await using var db = await factory.CreateDbContextAsync();
        var role = new Role { RoleName = roleName, Description = "test role" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var permissionIds = await db.Permissions
            .Where(p => permissions.Contains(p.PermissionName))
            .Select(p => p.PermissionId)
            .ToListAsync();
        if (permissionIds.Count != permissions.Length)
        {
            throw new InvalidOperationException("Unknown permission requested for test role.");
        }

        foreach (var id in permissionIds)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = id });
        }

        await db.SaveChangesAsync();
        return role;
    }

    public static async Task<int> RoleIdAsync(IDbContextFactory<SmartPsDbContext> factory, string roleName)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Roles.Where(r => r.RoleName == roleName).Select(r => r.RoleId).SingleAsync();
    }

    public static async Task<User> LoadAsync(IDbContextFactory<SmartPsDbContext> factory, int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().Include(u => u.Role).SingleAsync(u => u.UserId == userId);
    }

    public static async Task<User> LoadByUsernameAsync(IDbContextFactory<SmartPsDbContext> factory, string username)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().Include(u => u.Role).SingleAsync(u => u.Username == username);
    }

    public static async Task<HashSet<string>> GrantsAsync(IDbContextFactory<SmartPsDbContext> factory, string roleName)
    {
        await using var db = await factory.CreateDbContextAsync();
        var names = await db.RolePermissions
            .Where(rp => rp.Role.RoleName == roleName)
            .Select(rp => rp.Permission.PermissionName)
            .ToListAsync();
        return names.ToHashSet(StringComparer.Ordinal);
    }
}
