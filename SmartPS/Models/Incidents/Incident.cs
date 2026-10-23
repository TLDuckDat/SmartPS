using SmartPS.Models.Auth;

namespace SmartPS.Models.Incidents;

/// <summary>Biên bản sự cố bãi xe (mất vé, biển số không đọc được, xe quá hạn...).</summary>
public class Incident
{
    public int IncidentId { get; set; }

    /// <summary>Mã biên bản dạng SC-&lt;năm&gt;-&lt;số thứ tự&gt;, ví dụ SC-2026-001.</summary>
    public string IncidentCode { get; set; } = string.Empty;

    public IncidentType Type { get; set; } = IncidentType.Other;
    public string? LicensePlate { get; set; }
    public string Description { get; set; } = string.Empty;

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    public int ReportedByUserId { get; set; }
    public User? ReportedByUser { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public int? ResolvedByUserId { get; set; }
    public User? ResolvedByUser { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}
