namespace SmartPS.Models.Audit;

/// <summary>Append-only audit record linked to its predecessor through a SHA-256 hash chain.</summary>
public class AuditLog
{
    public long AuditLogId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string Details { get; set; } = "{}";
    public string MachineName { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}
