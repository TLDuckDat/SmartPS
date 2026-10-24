using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Audit;
using SmartPS.Models.Auth;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Localization;

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
                throw new ArgumentException(AppText.Get("Msg_Auth_UsernameRequired"), nameof(request.Username));
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_PasswordRequired"), nameof(request.Password));
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
                throw new UnauthorizedAccessException(AppText.Get("Msg_Auth_InvalidCredentials"));
            }

            if (!user.IsActive)
            {
                await LogLoginFailedAsync(user, userName, "Inactive");
                throw new InvalidOperationException(AppText.Get("Msg_Auth_AccountLocked"));
            }

            var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            if (!passwordValid)
            {
                await LogLoginFailedAsync(user, userName, "InvalidPassword");
                throw new UnauthorizedAccessException(AppText.Get("Msg_Auth_InvalidCredentials"));
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
                throw new ArgumentException(AppText.Get("Msg_Auth_UsernameRequired"));
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_FullNameRequired"));
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_PasswordTooShort"));
            }

            if (request.RoleId <= 0)
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_RoleRequired"));
            }

            var userName = request.Username.Trim().ToLowerInvariant();
            var fullName = request.FullName.Trim();

            await using var db = await _contextFactory.CreateDbContextAsync();

            var exists = await db.Users.AnyAsync(x => x.Username.ToLower() == userName);
            if (exists)
            {
                throw new InvalidOperationException(AppText.Get("Msg_Auth_UsernameTaken", userName));
            }

            var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.RoleId == request.RoleId);
            if (role is null)
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_RoleNotFound"));
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
                throw new ArgumentException(AppText.Get("Msg_Auth_InvalidUserId"));
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_FullNameRequired"));
            }

            if (request.RoleId <= 0)
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_RoleRequired"));
            }

            if (!string.IsNullOrEmpty(request.NewPassword) && request.NewPassword.Length < 6)
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_NewPasswordTooShort"));
            }

            var entityId = request.UserId.ToString();
            var isSelf = CurrentUser is not null && CurrentUser.UserId == request.UserId;

            await using var db = await _contextFactory.CreateDbContextAsync();

            // Tiền kiểm tra (chưa giữ khoá nhật ký): tải mục tiêu và vai trò mới, không theo dõi thay đổi
            var existing = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == request.UserId);
            if (existing is null)
            {
                throw new KeyNotFoundException(AppText.Get("Msg_Auth_UserToUpdateNotFound"));
            }

            var newRole = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == request.RoleId);
            if (newRole is null)
            {
                throw new ArgumentException(AppText.Get("Msg_Auth_RoleNotFound"));
            }

            var roleChanged = existing.RoleId != request.RoleId;

            // Mọi thay đổi trên tài khoản Admin (hoặc biến tài khoản thành Admin) chỉ do Admin thực hiện
            if (SystemRoles.IsSystemAdmin(existing.Role?.RoleName) || SystemRoles.IsSystemAdmin(newRole.RoleName))
            {
                await DemandSystemAdminAsync(entityId);
            }

            if (isSelf)
            {
                if (!request.IsActive)
                {
                    await FailAdminProtectionAsync(
                        AuditActions.UserUpdate, entityId, AdminProtectionReason.SelfDeactivate,
                        AppText.Get("Msg_Auth_CannotLockSelf"));
                }

                if (roleChanged)
                {
                    await FailAdminProtectionAsync(
                        AuditActions.UserUpdate, entityId, AdminProtectionReason.SelfRoleChange,
                        AppText.Get("Msg_Auth_CannotChangeOwnRole"));
                }
            }

            var newFullName = request.FullName.Trim();
            var passwordChanged = !string.IsNullOrWhiteSpace(request.NewPassword);
            var newPasswordHash = passwordChanged ? BCrypt.Net.BCrypt.HashPassword(request.NewPassword) : null;
            var lastAdminViolation = false;
            var notFound = false;
            var adminRequired = false;

            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db))
            {
                var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == request.UserId);
                if (user is null)
                {
                    notFound = true;
                }
                else if (!_currentUser.IsSystemAdmin
                         && (SystemRoles.IsSystemAdmin(user.Role?.RoleName) || SystemRoles.IsSystemAdmin(newRole.RoleName)))
                {
                    // Kiểm tra lại trên thực thể đang theo dõi (vai trò có thể đổi sau tiền kiểm tra)
                    adminRequired = true;
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
                throw new KeyNotFoundException(AppText.Get("Msg_Auth_UserToUpdateNotFound"));
            }

            if (adminRequired)
            {
                await DemandSystemAdminAsync(entityId);
            }

            if (lastAdminViolation)
            {
                await FailAdminProtectionAsync(
                    AuditActions.UserUpdate, entityId, AdminProtectionReason.LastActiveAdmin,
                    AppText.Get("Msg_Auth_LastAdminDemote"));
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
                throw new ArgumentException(AppText.Get("Msg_Auth_InvalidUserId"));
            }

            var entityId = userId.ToString();

            if (CurrentUser != null && CurrentUser.UserId == userId)
            {
                await FailAdminProtectionAsync(
                    AuditActions.UserDelete, entityId, AdminProtectionReason.SelfDelete,
                    AppText.Get("Msg_Auth_CannotDeleteSelf"));
            }

            await using var db = await _contextFactory.CreateDbContextAsync();

            var existing = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (existing is null)
            {
                throw new KeyNotFoundException(AppText.Get("Msg_Auth_UserToDeleteNotFound"));
            }

            // Xóa tài khoản mang vai trò Admin chỉ do Admin thực hiện
            if (SystemRoles.IsSystemAdmin(existing.Role?.RoleName))
            {
                await DemandSystemAdminAsync(entityId);
            }

            var lastAdminViolation = false;
            var notFound = false;
            var hasHistory = false;
            var adminRequired = false;

            try
            {
                await using (var transaction = await _audit.BeginAuditedTransactionAsync(db))
                {
                    var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
                    if (user is null)
                    {
                        notFound = true;
                    }
                    else if (!_currentUser.IsSystemAdmin && SystemRoles.IsSystemAdmin(user.Role?.RoleName))
                    {
                        adminRequired = true;
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
            // 547: vi phạm khoá ngoại (tài khoản đã có lịch sử nghiệp vụ)
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
            {
                hasHistory = true;
            }

            if (notFound)
            {
                throw new KeyNotFoundException(AppText.Get("Msg_Auth_UserToDeleteNotFound"));
            }

            if (adminRequired)
            {
                await DemandSystemAdminAsync(entityId);
            }

            if (lastAdminViolation)
            {
                await FailAdminProtectionAsync(
                    AuditActions.UserDelete, entityId, AdminProtectionReason.LastActiveAdmin,
                    AppText.Get("Msg_Auth_LastAdminDelete"));
            }

            if (hasHistory)
            {
                await _audit.LogAsync(new AuditEntry(
                    AuditActions.UserDelete, AuditOutcome.Failed, "User", entityId,
                    new { Reason = "HasHistory", Username = existing.Username }));
                throw new UserHasHistoryException(
                    userId,
                    AppText.Get("Msg_Auth_HasHistory"));
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
            if (attemptedUsername.Length > AuditRecordFactory.MaxUsernameLength)
            {
                attemptedUsername = attemptedUsername[..AuditRecordFactory.MaxUsernameLength];
            }
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
            return db.Users.AnyAsync(u => u.UserId != excludedUserId && u.IsActive
                && EF.Functions.Collate(u.Role.RoleName, SmartPsDbContext.CaseInsensitiveCollation) == SystemRoles.Admin);
        }
    }
}
