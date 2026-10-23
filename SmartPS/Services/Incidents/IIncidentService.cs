using SmartPS.Models.Incidents;
using SmartPS.Services.Common;

namespace SmartPS.Services.Incidents;

public sealed record IncidentInfo(
    int IncidentId,
    string IncidentCode,
    IncidentType Type,
    string? LicensePlate,
    string Description,
    IncidentStatus Status,
    DateTime ReportedAt,
    string ReportedBy,
    DateTime? ResolvedAt,
    string? ResolvedBy,
    string? ResolutionNotes);

public interface IIncidentService
{
    /// <summary>Danh sách biên bản, mới nhất trước. Trả rỗng khi không kết nối được CSDL.</summary>
    Task<IReadOnlyList<IncidentInfo>> GetIncidentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lập biên bản sự cố mới (nhân viên soát vé hoặc người có quyền Incident.Manage).</summary>
    Task<OperationResult> ReportAsync(IncidentType type, string? licensePlate, string description, CancellationToken cancellationToken = default);

    /// <summary>Đóng biên bản với ghi chú xử lý (quyền Incident.Manage).</summary>
    Task<OperationResult> ResolveAsync(int incidentId, string resolutionNotes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lập biên bản "Xe gửi quá hạn" cho các xe đang gửi lâu hơn ngưỡng trong cài đặt,
    /// bỏ qua biển số đã có biên bản quá hạn chưa xử lý. Message chứa số biên bản đã lập.
    /// </summary>
    Task<OperationResult> ScanOverstayAsync(CancellationToken cancellationToken = default);
}
