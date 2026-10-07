using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Audit;
using SmartPS.Models.Auth;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuthorizationGuard _guard;
        private readonly IAuditService _audit;

        public User? CurrentUser => _currentUser.User;
        public bool IsLoggedIn => CurrentUser is not null;

        public AuthService(
            IDbContextFactory<SmartPsDbContext> contextFactory,
            ICurrentUserContext currentUser,
            IAuthorizationGuard guard,
            IAuditService audit)
        {
            _contextFactory = contextFactory;
            _currentUser = currentUser;
            _guard = guard;
            _audit = audit;
        }

        public async Task<User?> LoginAsync(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                throw new ArgumentException("Tên đăng nhập không được để trống.", nameof(request.Username));
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new ArgumentException("Mật khẩu không được để trống.", nameof(request.Password));
            }

            var userName = request.Username.Trim().ToLowerInvariant();

            await using var db = await _contextFactory.CreateDbContextAsync();

            var user = await db.Users
                             .AsNoTracking()
                             .Include(x => x.Role)
                                .ThenInclude(x => x.RolePermissions)
                                    .ThenInclude(x => x.Permission)
                             .FirstOrDefaultAsync(x => x.Username.ToLower() == userName);

            if (user is null)
            {
                await LogLoginFailedAsync(null, userName, "UnknownUser");
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
            }

            if (!user.IsActive)
            {
                await LogLoginFailedAsync(user, userName, "Inactive");
                throw new InvalidOperationException("Tài khoản của bạn đã bị khóa hoặc chưa được kích hoạt. Vui lòng liên hệ quản trị viên.");
            }

            var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            if (!passwordValid)
            {
                await LogLoginFailedAsync(user, userName, "InvalidPassword");
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
            }

            _currentUser.SetUser(user);
            await _audit.LogAsync(new AuditEntry(
                AuditActions.AuthLoginSuccess,
                AuditOutcome.Success,
                "User",
                user.UserId.ToString(),
                null,
                AuditActor.FromUser(user)));

            return user;
        }

        public async Task<bool> RegisterAsync(RegisterRequest request)
        {
            await _guard.DemandAsync(Permissions.UserCreate, "User");

            if (string.IsNullOrWhiteSpace(request.Username))
            {
                throw new ArgumentException("Tên đăng nhập không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                throw new ArgumentException("Họ và tên không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            {
                throw new ArgumentException("Mật khẩu phải có ít nhất 6 ký tự.");
            }

            if (request.RoleId <= 0)
            {
                throw new ArgumentException("Vui lòng chọn vai trò hợp lệ.");
            }

            var userName = request.Username.Trim().ToLowerInvariant();
            var fullName = request.FullName.Trim();

            await using var db = await _contextFactory.CreateDbContextAsync();

            var exists = await db.Users.AnyAsync(x => x.Username.ToLower() == userName);
            if (exists)
            {
                throw new InvalidOperationException($"Tên đăng nhập '{userName}' đã tồn tại trong hệ thống. Vui lòng chọn tên khác.");
            }

            var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.RoleId == request.RoleId);
            if (role is null)
            {
                throw new ArgumentException("Vai trò được chọn không tồn tại trong hệ thống.");
            }

            // Chỉ Admin mới được tạo tài khoản mang vai trò Admin
            if (SystemRoles.IsSystemAdmin(role.RoleName))
            {
                await DemandSystemAdminAsync(null);
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            var user = new User
            {
                Username = userName,
                PasswordHash = passwordHash,
                FullName = fullName,
                RoleId = request.RoleId,
                IsActive = true
            };

            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db))
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.UserCreate,
                    AuditOutcome.Success,
                    "User",
                    user.UserId.ToString(),
                    new
                    {
                        Username = user.Username,
                        FullName = user.FullName,
                        RoleId = role.RoleId,
                        RoleName = role.RoleName,
                        IsActive = user.IsActive
                    }));
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            return true;
        }

        public async Task<List<Role>> GetRolesAsync()
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            return await db.Roles.AsNoTracking().OrderBy(r => r.RoleId).ToListAsync();
        }

        public async Task<List<User>> GetUsersAsync()
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            return await db.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .OrderByDescending(u => u.UserId)
                .ToListAsync();
        }

        public async Task<bool> UpdateUserAsync(UpdateUserRequest request)
        {
            await _guard.DemandAsync(Permissions.UserEdit, "User", request.UserId > 0 ? request.UserId.ToString() : null);

            if (request.UserId <= 0)
            {
                throw new ArgumentException("Mã tài khoản không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                throw new ArgumentException("Họ và tên không được để trống.");
            }

            if (request.RoleId <= 0)
            {
                throw new ArgumentException("Vui lòng chọn vai trò hợp lệ.");
            }

            if (!string.IsNullOrEmpty(request.NewPassword) && request.NewPassword.Length < 6)
            {
                throw new ArgumentException("Mật khẩu mới phải có ít nhất 6 ký tự.");
            }

            var entityId = request.UserId.ToString();
            var isSelf = CurrentUser is not null && CurrentUser.UserId == request.UserId;

            await using var db = await _contextFactory.CreateDbContextAsync();

            // Tiền kiểm tra (chưa giữ khoá nhật ký): tải mục tiêu và vai trò mới, không theo dõi thay đổi
            var existing = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == request.UserId);
            if (existing is null)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng cần cập nhật.");
            }

            var newRole = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == request.RoleId);
            if (newRole is null)
            {
                throw new ArgumentException("Vai trò được chọn không tồn tại trong hệ thống.");
            }

            var roleChanged = existing.RoleId != request.RoleId;
            if (roleChanged)
            {
                // Gán hoặc gỡ vai trò Admin chỉ do Admin thực hiện
                if (SystemRoles.IsSystemAdmin(existing.Role?.RoleName) || SystemRoles.IsSystemAdmin(newRole.RoleName))
                {
                    await DemandSystemAdminAsync(entityId);
                }
            }

            if (isSelf)
            {
                if (!request.IsActive)
                {
                    await FailAdminProtectionAsync(
                        AuditActions.UserUpdate, entityId, AdminProtectionReason.SelfDeactivate,
                        "Bạn không thể tự khóa tài khoản đang đăng nhập.");
                }

                if (roleChanged)
                {
                    await FailAdminProtectionAsync(
                        AuditActions.UserUpdate, entityId, AdminProtectionReason.SelfRoleChange,
                        "Bạn không thể tự thay đổi vai trò của tài khoản đang đăng nhập.");
                }
            }

            var newFullName = request.FullName.Trim();
            var passwordChanged = !string.IsNullOrWhiteSpace(request.NewPassword);
            var newPasswordHash = passwordChanged ? BCrypt.Net.BCrypt.HashPassword(request.NewPassword) : null;
            var lastAdminViolation = false;
            var notFound = false;

            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db))
            {
                var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == request.UserId);
                if (user is null)
                {
                    notFound = true;
                }
                else
                {
                    var wasActiveAdmin = user.IsActive && SystemRoles.IsSystemAdmin(user.Role?.RoleName);
                    var staysActiveAdmin = request.IsActive && SystemRoles.IsSystemAdmin(newRole.RoleName);

                    if (wasActiveAdmin && !staysActiveAdmin && !await HasOtherActiveAdminAsync(db, user.UserId))
                    {
                        lastAdminViolation = true;
                    }
                    else
                    {
                        var before = new { FullName = user.FullName, RoleId = user.RoleId, RoleName = user.Role?.RoleName, IsActive = user.IsActive };

                        user.FullName = newFullName;
                        user.RoleId = request.RoleId;
                        user.IsActive = request.IsActive;

                        // Đổi mật khẩu nếu admin có nhập mật khẩu mới
                        if (newPasswordHash is not null)
                        {
                            user.PasswordHash = newPasswordHash;
                        }

                        await db.SaveChangesAsync();

                        await _audit.AppendAsync(db, new AuditEntry(
                            AuditActions.UserUpdate,
                            AuditOutcome.Success,
                            "User",
                            entityId,
                            new
                            {
                                Before = before,
                                After = new { FullName = newFullName, RoleId = newRole.RoleId, RoleName = newRole.RoleName, IsActive = request.IsActive },
                                PasswordChanged = passwordChanged
                            }));
                        await db.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                }
            }

            if (notFound)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng cần cập nhật.");
            }

            if (lastAdminViolation)
            {
                await FailAdminProtectionAsync(
                    AuditActions.UserUpdate, entityId, AdminProtectionReason.LastActiveAdmin,
                    "Không thể hạ quyền hoặc khóa tài khoản Quản trị viên (Admin) đang hoạt động duy nhất còn lại trong hệ thống.");
            }

            // Nạp lại người dùng hiện tại cùng vai trò và quyền để không mất quyền sau khi tự sửa tài khoản
            if (isSelf)
            {
                await ReloadCurrentUserAsync();
            }

            return true;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            await _guard.DemandAsync(Permissions.UserDelete, "User", userId > 0 ? userId.ToString() : null);

            if (userId <= 0)
            {
                throw new ArgumentException("Mã tài khoản không hợp lệ.");
            }

            var entityId = userId.ToString();

            if (CurrentUser != null && CurrentUser.UserId == userId)
            {
                await FailAdminProtectionAsync(
                    AuditActions.UserDelete, entityId, AdminProtectionReason.SelfDelete,
                    "Bạn không thể tự xóa tài khoản đang đăng nhập.");
            }

            await using var db = await _contextFactory.CreateDbContextAsync();

            var existing = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (existing is null)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng cần xóa.");
            }

            // Xóa tài khoản mang vai trò Admin chỉ do Admin thực hiện
            if (SystemRoles.IsSystemAdmin(existing.Role?.RoleName))
            {
                await DemandSystemAdminAsync(entityId);
            }

            var lastAdminViolation = false;
            var notFound = false;
            var hasHistory = false;

            try
            {
                await using (var transaction = await _audit.BeginAuditedTransactionAsync(db))
                {
                    var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
                    if (user is null)
                    {
                        notFound = true;
                    }
                    else if (user.IsActive && SystemRoles.IsSystemAdmin(user.Role?.RoleName)
                             && !await HasOtherActiveAdminAsync(db, user.UserId))
                    {
                        lastAdminViolation = true;
                    }
                    else
                    {
                        var deleted = new { Username = user.Username, FullName = user.FullName, RoleName = user.Role?.RoleName };

                        db.Users.Remove(user);
                        await db.SaveChangesAsync();

                        await _audit.AppendAsync(db, new AuditEntry(AuditActions.UserDelete, AuditOutcome.Success, "User", entityId, deleted));
                        await db.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                hasHistory = true;
            }

            if (notFound)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng cần xóa.");
            }

            if (lastAdminViolation)
            {
                await FailAdminProtectionAsync(
                    AuditActions.UserDelete, entityId, AdminProtectionReason.LastActiveAdmin,
                    "Không thể xóa tài khoản Quản trị viên (Admin) đang hoạt động duy nhất còn lại trong hệ thống.");
            }

            if (hasHistory)
            {
                await _audit.LogAsync(new AuditEntry(
                    AuditActions.UserDelete, AuditOutcome.Failed, "User", entityId,
                    new { Reason = "HasHistory", Username = existing.Username }));
                throw new UserHasHistoryException(
                    userId,
                    "Không thể xóa tài khoản đã có lịch sử hoạt động (ca trực, giao dịch...). Hãy khóa tài khoản thay vì xóa.");
            }

            return true;
        }

        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            var user = _currentUser.User;
            if (user is not null)
            {
                await _audit.LogAsync(new AuditEntry(
                    AuditActions.AuthLogout,
                    AuditOutcome.Success,
                    "User",
                    user.UserId.ToString(),
                    null,
                    AuditActor.FromUser(user)), cancellationToken);
            }

            _currentUser.SetUser(null);
        }

        public async Task ReloadCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var current = _currentUser.User;
            if (current is null)
            {
                return;
            }

            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var reloaded = await db.Users
                .AsNoTracking()
                .Include(u => u.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.UserId == current.UserId, cancellationToken);

            if (reloaded is not null)
            {
                _currentUser.SetUser(reloaded);
            }
        }

        public async Task<bool> CanConnectToDatabaseAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(4));

                await using var db = await _contextFactory.CreateDbContextAsync(cts.Token);
                return await db.Database.CanConnectAsync(cts.Token);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> EnsureDatabaseInitializedAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await using var db = await _contextFactory.CreateDbContextAsync(cts.Token);
                await DbInitializer.InitializeAsync(db, cts.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Task LogLoginFailedAsync(User? user, string attemptedUsername, string reason)
        {
            var actor = new AuditActor(user?.UserId, attemptedUsername, user?.Role?.RoleName ?? string.Empty);
            return _audit.LogAsync(new AuditEntry(
                AuditActions.AuthLoginFailed,
                AuditOutcome.Failed,
                null,
                null,
                new { AttemptedUsername = attemptedUsername, Reason = reason },
                actor));
        }

        /// <summary>Gán, gỡ hoặc xóa vai trò Admin chỉ do Admin hệ thống thực hiện (ACCESS_DENIED nếu không phải).</summary>
        private async Task DemandSystemAdminAsync(string? entityId)
        {
            if (_currentUser.IsSystemAdmin)
            {
                return;
            }

            await _guard.DenyAsync(new[] { SystemRoles.AdminRoleRequirement }, "AdminRoleRequired", "User", entityId);
        }

        private async Task FailAdminProtectionAsync(string action, string entityId, AdminProtectionReason reason, string message)
        {
            await _audit.LogAsync(new AuditEntry(action, AuditOutcome.Failed, "User", entityId, new { Reason = reason.ToString() }));
            throw new AdminProtectionException(reason, message);
        }

        private static Task<bool> HasOtherActiveAdminAsync(SmartPsDbContext db, int excludedUserId)
        {
            return db.Users.AnyAsync(u => u.UserId != excludedUserId && u.IsActive && u.Role.RoleName == SystemRoles.Admin);
        }
    }
}
