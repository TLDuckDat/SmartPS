using SmartPS.Models.Incidents;
using SmartPS.Services.Audit;
using SmartPS.Services.Incidents;

namespace SmartPS.ViewModels.Incidents;

public class IncidentItemViewModel : ViewModelBase
{
    public const string OpenStatusText = "Đang xử lý";
    public const string ResolvedStatusText = "Đã giải quyết";

    public static IReadOnlyDictionary<IncidentType, string> TypeNames { get; } = new Dictionary<IncidentType, string>
    {
        [Models.Incidents.IncidentType.LostTicket] = "Mất thẻ xe / Vé lượt",
        [Models.Incidents.IncidentType.UnreadablePlate] = "Biển số bị che khuất / Không đọc được",
        [Models.Incidents.IncidentType.Overstay] = "Xe gửi quá hạn",
        [Models.Incidents.IncidentType.VehicleDamage] = "Va chạm / Hư hỏng phương tiện",
        [Models.Incidents.IncidentType.Security] = "Nghi vấn an ninh",
        [Models.Incidents.IncidentType.Equipment] = "Sự cố thiết bị (camera, barrier)",
        [Models.Incidents.IncidentType.Other] = "Khác"
    };

    public int IncidentId { get; set; }
    public string IncidentCode { get; set; } = string.Empty;
    public IncidentType Type { get; set; }
    public string IncidentType => TypeNames.TryGetValue(Type, out var name) ? name : Type.ToString();
    public string LicensePlate { get; set; } = string.Empty;
    public DateTime ReportedTime { get; set; }
    public string ReportedBy { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public string Status => IsResolved ? ResolvedStatusText : OpenStatusText;
    public DateTime? ResolvedTime { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }

    public string ResolutionSummary => IsResolved
        ? $"{ResolvedBy} · {ResolvedTime:dd/MM/yyyy HH:mm}"
        : "Chưa xử lý";

    public static IncidentItemViewModel FromInfo(IncidentInfo info)
    {
        return new IncidentItemViewModel
        {
            IncidentId = info.IncidentId,
            IncidentCode = info.IncidentCode,
            Type = info.Type,
            LicensePlate = string.IsNullOrWhiteSpace(info.LicensePlate) ? "—" : info.LicensePlate,
            ReportedTime = AuditTime.ToVietnamTime(info.ReportedAt),
            ReportedBy = info.ReportedBy,
            Notes = info.Description,
            IsResolved = info.Status == IncidentStatus.Resolved,
            ResolvedTime = info.ResolvedAt.HasValue ? AuditTime.ToVietnamTime(info.ResolvedAt.Value) : null,
            ResolvedBy = info.ResolvedBy,
            ResolutionNotes = info.ResolutionNotes
        };
    }
}
