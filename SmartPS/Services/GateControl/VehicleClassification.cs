using SmartPS.Models.GateControl;

namespace SmartPS.Services.GateControl;

public enum ClassificationWarning
{
    None = 0,
    CustomerLocked = 1,
    TicketVehicleTypeMismatch = 2
}

/// <summary>Mục danh sách đen đang hiệu lực (đọc từ bảng của nhóm quản lý khách hàng).</summary>
public sealed record BlacklistMatch(string NormalizedPlate, string Reason);

public sealed record VehicleClassification(
    VehicleCategory Category,
    string NormalizedPlate,
    BlacklistMatch? Blacklist,
    PlateOwnership? Owner,
    PlateTicket? Ticket,
    ClassificationWarning Warning)
{
    public bool IsBlacklisted => Category == VehicleCategory.Blacklisted;

    public bool IsResident => Category == VehicleCategory.Resident;

    public bool IsMonthlyPass => Ticket is not null;

    public static VehicleClassification Visitor(string normalizedPlate)
        => new(VehicleCategory.Visitor, normalizedPlate, null, null, null, ClassificationWarning.None);
}

/// <summary>
/// Phân loại xe vào bãi: danh sách đen, cư dân (hộ đang hoạt động), vé tháng, vãng lai.
/// Cư dân chỉ được xác định bởi hộ của chủ xe, không bao giờ từ vé tháng hay <c>Customer.Type</c>.
/// </summary>
public static class VehicleClassifier
{
    public static VehicleClassification Classify(
        string normalizedPlate,
        BlacklistMatch? blacklist,
        PlateOwnership? ownership,
        int requestedVehicleTypeId,
        DateTime nowUtc)
    {
        var ownerLocked = ownership is { HasOwner: true, OwnerIsActive: false };

        var valid = ownerLocked || ownership is null
            ? new List<PlateTicket>()
            : ownership.ValidTickets.Where(t => t.StartDateUtc <= nowUtc && nowUtc <= t.EndDateUtc).ToList();

        var honoured = valid.FirstOrDefault(t => requestedVehicleTypeId <= 0 || t.VehicleTypeId == requestedVehicleTypeId);

        var warning = ClassificationWarning.None;
        if (ownerLocked)
        {
            warning = ClassificationWarning.CustomerLocked;
        }
        else if (valid.Count > 0 && honoured is null)
        {
            warning = ClassificationWarning.TicketVehicleTypeMismatch;
        }

        if (blacklist is not null)
        {
            return new VehicleClassification(VehicleCategory.Blacklisted, normalizedPlate, blacklist, ownership,
                honoured ?? valid.FirstOrDefault(), warning);
        }

        if (ownership is { IsResident: true })
        {
            return new VehicleClassification(VehicleCategory.Resident, normalizedPlate, null, ownership, honoured, warning);
        }

        if (honoured is not null)
        {
            return new VehicleClassification(VehicleCategory.MonthlyPass, normalizedPlate, null, ownership, honoured, warning);
        }

        return new VehicleClassification(VehicleCategory.Visitor, normalizedPlate, null, ownership, null, warning);
    }
}
