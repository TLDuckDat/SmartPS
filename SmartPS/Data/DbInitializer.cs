using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Models.Auth;

namespace SmartPS.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(SmartPsDbContext context)
    {
        // 1. Tự động áp dụng Migration hoặc tạo Database nếu chưa tồn tại
        try
        {
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                // Nếu chưa có migration nào thì đảm bảo DB được tạo
                await context.Database.EnsureCreatedAsync();
            }
        }
        catch
        {
            // Fallback nếu môi trường chưa cấu hình migration lịch sử
            await context.Database.EnsureCreatedAsync();
        }

        // 2. Khởi tạo danh sách Quyền hạn (Permissions)
        var allPermissionNames = Permissions.GetAll();
        var existingPermissionNames = await context.Permissions
            .Select(p => p.PermissionName)
            .ToListAsync();

        var missingPermissions = allPermissionNames
            .Where(name => !existingPermissionNames.Contains(name))
            .Select(name => new Permission
            {
                PermissionName = name,
                Description = GetPermissionDescription(name)
            })
            .ToList();

        if (missingPermissions.Count > 0)
        {
            context.Permissions.AddRange(missingPermissions);
            await context.SaveChangesAsync();
        }

        // 3. Khởi tạo Vai trò (Roles)
        var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
        if (adminRole is null)
        {
            adminRole = new Role
            {
                RoleName = "Admin",
                Description = "Quản trị viên toàn quyền hệ thống bãi đỗ xe"
            };
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
        }

        var managerRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Manager");
        if (managerRole is null)
        {
            managerRole = new Role
            {
                RoleName = "Manager",
                Description = "Quản lý điều hành vận hành bãi đỗ xe"
            };
            context.Roles.Add(managerRole);
            await context.SaveChangesAsync();
        }

        var operatorRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Operator");
        if (operatorRole is null)
        {
            operatorRole = new Role
            {
                RoleName = "Operator",
                Description = "Nhân viên trực ca bốt soát vé bãi đỗ xe"
            };
            context.Roles.Add(operatorRole);
            await context.SaveChangesAsync();
        }

        // 4. Gán quyền cho Vai trò (RolePermissions)
        var allDbPermissions = await context.Permissions.ToListAsync();

        // Gán toàn bộ quyền cho Admin
        var existingAdminPermIds = await context.RolePermissions
            .Where(rp => rp.RoleId == adminRole.RoleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var missingAdminRolePerms = allDbPermissions
            .Where(p => !existingAdminPermIds.Contains(p.PermissionId))
            .Select(p => new RolePermission
            {
                RoleId = adminRole.RoleId,
                PermissionId = p.PermissionId
            })
            .ToList();

        if (missingAdminRolePerms.Count > 0)
        {
            context.RolePermissions.AddRange(missingAdminRolePerms);
            await context.SaveChangesAsync();
        }

        // Gán quyền tác nghiệp cho Operator (Xem bãi xe, check in, check out, xem báo cáo ca)
        var operatorAllowedNames = new HashSet<string>
        {
            Permissions.ParkingView,
            Permissions.ParkingCheckIn,
            Permissions.ParkingCheckOut,
            Permissions.ReportView
        };

        var existingOperatorPermIds = await context.RolePermissions
            .Where(rp => rp.RoleId == operatorRole.RoleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var missingOperatorRolePerms = allDbPermissions
            .Where(p => operatorAllowedNames.Contains(p.PermissionName) && !existingOperatorPermIds.Contains(p.PermissionId))
            .Select(p => new RolePermission
            {
                RoleId = operatorRole.RoleId,
                PermissionId = p.PermissionId
            })
            .ToList();

        if (missingOperatorRolePerms.Count > 0)
        {
            context.RolePermissions.AddRange(missingOperatorRolePerms);
            await context.SaveChangesAsync();
        }

        // 5. Khởi tạo Tài khoản Quản trị viên (Admin) mặc định nếu chưa có
        var hasAdminUser = await context.Users.AnyAsync(u => u.Username.ToLower() == "admin");
        if (!hasAdminUser)
        {
            var adminUser = new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                FullName = "Quản trị viên Hệ thống",
                RoleId = adminRole.RoleId,
                IsActive = true
            };

            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }
    }

    private static string GetPermissionDescription(string permissionName)
    {
        return permissionName switch
        {
            Permissions.UserView => "Xem danh sách người dùng",
            Permissions.UserCreate => "Tạo người dùng mới",
            Permissions.UserEdit => "Chỉnh sửa thông tin người dùng",
            Permissions.UserDelete => "Xóa người dùng",
            Permissions.RoleView => "Xem danh sách vai trò",
            Permissions.RoleManage => "Quản lý phân quyền vai trò",
            Permissions.ParkingView => "Xem trạng thái bãi đỗ xe",
            Permissions.ParkingCheckIn => "Soát vé xe vào",
            Permissions.ParkingCheckOut => "Soát vé xe ra & tính phí",
            Permissions.ParkingConfigure => "Cấu hình khu vực & vị trí đỗ",
            Permissions.PricingManage => "Cấu hình bảng giá gửi xe",
            Permissions.ReportView => "Xem báo cáo doanh thu & lượt xe",
            Permissions.ReportExport => "Xuất báo cáo dữ liệu",
            _ => $"Quyền {permissionName}"
        };
    }
}
