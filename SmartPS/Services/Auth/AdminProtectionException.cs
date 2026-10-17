namespace SmartPS.Services.Auth;

public enum AdminProtectionReason
{
    LastActiveAdmin,
    SelfDeactivate,
    SelfRoleChange,
    SelfDelete
}

/// <summary>Raised when a user change would lock out the last active Admin or when a user targets their own account.</summary>
public sealed class AdminProtectionException : InvalidOperationException
{
    public AdminProtectionException(AdminProtectionReason reason, string message) : base(message)
    {
        Reason = reason;
    }

    public AdminProtectionReason Reason { get; }
}
