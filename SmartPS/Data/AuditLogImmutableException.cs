namespace SmartPS.Data;

/// <summary>Thrown when code tries to modify or delete an audit record through the DbContext.</summary>
public sealed class AuditLogImmutableException : InvalidOperationException
{
    public AuditLogImmutableException(string message) : base(message)
    {
    }
}
