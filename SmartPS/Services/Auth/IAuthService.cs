using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;

namespace SmartPS.Services.Auth;

public interface IAuthService
{
    User? CurrentUser { get; }

    bool IsLoggedIn { get; }

    Task<User?> LoginAsync(LoginRequest request);

    Task<bool> RegisterAsync(RegisterRequest request);

    Task<List<Role>> GetRolesAsync();

    Task<List<User>> GetUsersAsync();

    void Logout();
}