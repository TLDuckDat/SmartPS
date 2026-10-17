namespace SmartPS.Services.Auth;

/// <summary>Raised when a user cannot be deleted because other records (shifts, sessions, payments) reference them.</summary>
public sealed class UserHasHistoryException : InvalidOperationException
{
    public UserHasHistoryException(int userId, string message, Exception? inner = null) : base(message, inner)
    {
        UserId = userId;
    }

    public int UserId { get; }
}
