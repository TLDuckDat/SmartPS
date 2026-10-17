using SmartPS.Models.Parking;

namespace SmartPS.Models.GateControl;

public class GateCheckInResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ParkingSession? Session { get; set; }
    public bool IsMonthlyTicket { get; set; }
    public string? CustomerName { get; set; }
    public string? AssignedSlotCode { get; set; }
    public bool IsPermissionDenied { get; set; }

    // Phân luồng cư dân / khách vãng lai / danh sách đen
    public VehicleCategory Category { get; set; }
    public CheckInRejectReason RejectReason { get; set; }
    public bool IsBlacklisted { get; set; }
    public string? BlacklistReason { get; set; }
    public bool IsResident { get; set; }
    public string? ApartmentCode { get; set; }
    public DateTime? TicketValidUntilUtc { get; set; }
    public bool CustomerLockedWarning { get; set; }
    public bool TicketVehicleTypeMismatchWarning { get; set; }
    public string? AssignedZoneCode { get; set; }
    public ZoneAudience? AssignedZoneAudience { get; set; }
}
