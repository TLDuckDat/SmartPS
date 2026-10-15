using SmartPS.Data;

namespace SmartPS.Services.GateControl;

public sealed record PlateTicket(
    int TicketId,
    string TicketCode,
    int CustomerId,
    int VehicleTypeId,
    DateTime StartDateUtc,
    DateTime EndDateUtc);

public sealed record PlateOwnership(
    string NormalizedPlate,
    int? VehicleId,
    int? VehicleTypeId,
    int? OwnerCustomerId,
    string? OwnerName,
    bool OwnerIsActive,
    int? HouseholdId,
    bool HouseholdIsActive,
    string? ApartmentCode,
    IReadOnlyList<PlateTicket> ValidTickets)
{
    public bool HasOwner => OwnerCustomerId is not null;

    /// <summary>Cư dân: chủ xe đang hoạt động và thuộc một hộ đang hoạt động. Vé tháng và Customer.Type không được xét.</summary>
    public bool IsResident => OwnerCustomerId is not null && OwnerIsActive && HouseholdId is not null && HouseholdIsActive;

    public static PlateOwnership None(string normalizedPlate)
        => new(normalizedPlate, null, null, null, null, false, null, false, null, Array.Empty<PlateTicket>());
}

/// <summary>Lớp chỉ-đọc: tra chủ xe, hộ và vé tháng theo biển số trên các bảng của nhóm quản lý khách hàng.</summary>
public interface IPlateOwnershipReader
{
    Task<PlateOwnership> ReadAsync(SmartPsDbContext db, string normalizedPlate, DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
