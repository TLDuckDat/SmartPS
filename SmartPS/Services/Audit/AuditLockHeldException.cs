namespace SmartPS.Services.Audit;

/// <summary>
/// Thrown when an audit write or an authorization check is attempted while the audit chain lock is held by the
/// current flow. Without this guard the call would wait on the lock held by its own caller.
/// </summary>
public sealed class AuditLockHeldException : InvalidOperationException
{
    public AuditLockHeldException(string message) : base(message)
    {
    }
}
