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

        public void Logout()
        {
            CurrentUser = null;
        }
    }
}