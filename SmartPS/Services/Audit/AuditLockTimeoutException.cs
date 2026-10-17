namespace SmartPS.Services.Audit;

/// <summary>
/// The audit chain lock could not be acquired in time. This is a contention problem, not a lost database connection,
/// so callers must not fall back to offline mode because of it.
/// </summary>
public sealed class AuditLockTimeoutException : InvalidOperationException
{
    public AuditLockTimeoutException(string message) : base(message)
    {
    }
}
