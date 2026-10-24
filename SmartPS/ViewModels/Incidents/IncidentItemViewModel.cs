using SmartPS.Models.Incidents;
using SmartPS.Services.Audit;
using SmartPS.Services.Incidents;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Incidents;

public class IncidentItemViewModel : ViewModelBase
{
    public static string OpenStatusText => AppText.Get("Str_Inc_StatusOpen");
    public static string ResolvedStatusText => AppText.Get("Str_Inc_StatusResolved");

    private static readonly IReadOnlyDictionary<IncidentType, string> TypeNameKeys = new Dictionary<IncidentType, string>
    {
        [Models.Incidents.IncidentType.LostTicket] = "Str_Inc_TypeLostTicket",
        [Models.Incidents.IncidentType.UnreadablePlate] = "Str_Inc_TypeUnreadablePlate",
        [Models.Incidents.IncidentType.Overstay] = "Str_Inc_TypeOverstay",
        [Models.Incidents.IncidentType.VehicleDamage] = "Str_Inc_TypeVehicleDamage",
        [Models.Incidents.IncidentType.Security] = "Str_Inc_TypeSecurity",
        [Models.Incidents.IncidentType.Equipment] = "Str_Inc_TypeEquipment",
        [Models.Incidents.IncidentType.Other] = "Str_Inc_TypeOther"
    };

    public static IEnumerable<IncidentType> Types => TypeNameKeys.Keys;

    public static string GetTypeName(IncidentType type) =>
        TypeNameKeys.TryGetValue(type, out var key) ? AppText.Get(key) : type.ToString();

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(IncidentType));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ResolutionSummary));
    }

    public int IncidentId { get; set; }
    public string IncidentCode { get; set; } = string.Empty;
    public IncidentType Type { get; set; }
    public string IncidentType => GetTypeName(Type);
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
        : AppText.Get("Str_Inc_NotHandled");

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
