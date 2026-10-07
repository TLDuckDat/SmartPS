using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;

namespace SmartPS.Tests.TestSupport;

/// <summary>Exposes the user held by <see cref="ICurrentUserContext"/>; every other member is unused in unit tests.</summary>
public sealed class FakeAuthService : IAuthService
{
    private readonly ICurrentUserContext _currentUser;

    public FakeAuthService(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public User? CurrentUser => _currentUser.User;

    public bool IsLoggedIn => _currentUser.User is not null;

    public Task<User?> LoginAsync(LoginRequest request) => throw new NotImplementedException();
    public Task<bool> RegisterAsync(RegisterRequest request) => throw new NotImplementedException();
    public Task<List<Role>> GetRolesAsync() => throw new NotImplementedException();
    public Task<List<User>> GetUsersAsync() => throw new NotImplementedException();
    public Task<bool> UpdateUserAsync(UpdateUserRequest request) => throw new NotImplementedException();
    public Task<bool> DeleteUserAsync(int userId) => throw new NotImplementedException();

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.SetUser(null);
        return Task.CompletedTask;
    }

    public Task ReloadCurrentUserAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> CanConnectToDatabaseAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> EnsureDatabaseInitializedAsync() => Task.FromResult(false);
}
