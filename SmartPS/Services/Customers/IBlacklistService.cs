using SmartPS.Services.Common;

namespace SmartPS.Services.Customers;

public sealed record BlacklistQuery(bool IncludeInactive = false, string? SearchText = null);

public sealed record BlacklistEntryDto(
    int BlacklistEntryId,
    string LicensePlate,
    string Reason,
    DateTime CreatedAt,
    int? CreatedByUserId,
    string? CreatedByUsername,
    bool IsActive,
    DateTime? RemovedAt,
    int? RemovedByUserId,
    string? RemovedByUsername,
    string? RemoveReason);

/// <summary>Danh sách đen biển số. Xem cần Customer.View (ném PermissionDeniedException); thêm/gỡ cần Blacklist.Manage.</summary>
public interface IBlacklistService
{
    Task<IReadOnlyList<BlacklistEntryDto>> GetEntriesAsync(BlacklistQuery query, CancellationToken cancellationToken = default);

    Task<OperationResult<int>> AddAsync(string licensePlate, string reason, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveAsync(int blacklistEntryId, string removeReason, CancellationToken cancellationToken = default);
}
