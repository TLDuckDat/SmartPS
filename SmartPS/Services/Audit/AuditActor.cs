using SmartPS.Models.Auth;

namespace SmartPS.Services.Audit;

/// <summary>Snapshot of who performed an audited action.</summary>
public sealed record AuditActor(int? UserId, string Username, string RoleName)
{
    public static AuditActor Anonymous { get; } = new(null, string.Empty, string.Empty);

    public static AuditActor FromUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new AuditActor(user.UserId, user.Username ?? string.Empty, user.Role?.RoleName ?? string.Empty);
    }
}
