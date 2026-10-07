using SmartPS.Models.Auth;

namespace SmartPS.Services.Authorization;

/// <summary>Holds the logged-in user and an immutable snapshot of the permissions loaded with that user.</summary>
public interface ICurrentUserContext
{
    User? User { get; }

    IReadOnlySet<string> Permissions { get; }

    bool IsSystemAdmin { get; }

    void SetUser(User? user);

    event EventHandler? Changed;
}
