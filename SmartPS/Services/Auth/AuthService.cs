using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;

namespace SmartPS.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
        public User? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser is not null;

        public AuthService(IDbContextFactory<SmartPsDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
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
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
            }

            if (!user.IsActive)
            {
                throw new InvalidOperationException("Tài khoản của bạn đã bị khóa hoặc chưa được kích hoạt. Vui lòng liên hệ quản trị viên.");
            }

            var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            if (!passwordValid)
            {
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
            }

            CurrentUser = user;
            return user;
        }

        public async Task<bool> RegisterAsync(RegisterRequest request)
        {
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

            var roleExists = await db.Roles.AnyAsync(x => x.RoleId == request.RoleId);
            if (!roleExists)
            {
                throw new ArgumentException("Vai trò được chọn không tồn tại trong hệ thống.");
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
            db.Users.Add(user);

            await db.SaveChangesAsync();

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

            await using var db = await _contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == request.UserId);
            if (user is null)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng cần cập nhật.");
            }

            var roleExists = await db.Roles.AnyAsync(r => r.RoleId == request.RoleId);
            if (!roleExists)
            {
                throw new ArgumentException("Vai trò được chọn không tồn tại trong hệ thống.");
            }

            user.FullName = request.FullName.Trim();
            user.RoleId = request.RoleId;
            user.IsActive = request.IsActive;

            // Đổi mật khẩu nếu admin có nhập mật khẩu mới
            if (!string.IsNullOrWhiteSpace(request.NewPassword))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            }

            await db.SaveChangesAsync();

            // Nếu người dùng vừa cập nhật chính là tài khoản hiện tại đang đăng nhập, cập nhật lại CurrentUser
            if (CurrentUser != null && CurrentUser.UserId == user.UserId)
            {
                CurrentUser.FullName = user.FullName;
                CurrentUser.RoleId = user.RoleId;
                CurrentUser.IsActive = user.IsActive;
                var updatedRole = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == user.RoleId);
                if (updatedRole != null)
                {
                    CurrentUser.Role = updatedRole;
                }
            }

            return true;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            if (userId <= 0)
            {
                throw new ArgumentException("Mã tài khoản không hợp lệ.");
            }

            if (CurrentUser != null && CurrentUser.UserId == userId)
            {
                throw new InvalidOperationException("Bạn không thể tự xóa tài khoản đang đăng nhập.");
            }

            await using var db = await _contextFactory.CreateDbContextAsync();
            var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng cần xóa.");
            }

            if (user.Role?.RoleName == "Admin")
            {
                var adminCount = await db.Users.CountAsync(u => u.Role.RoleName == "Admin");
                if (adminCount <= 1)
                {
                    throw new InvalidOperationException("Không thể xóa tài khoản Quản trị viên (Admin) duy nhất còn lại trong hệ thống.");
                }
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync();

            return true;
        }

        public void Logout()
        {
            CurrentUser = null;
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var db = await _contextFactory.CreateDbContextAsync(cts.Token);
                await DbInitializer.InitializeAsync(db);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}