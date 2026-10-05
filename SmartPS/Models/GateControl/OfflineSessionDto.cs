using SmartPS.Models.Parking;

namespace SmartPS.Models.GateControl;

public class OfflineSessionDto
{
    public int SessionId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public string? VehicleTypeName { get; set; }
    public int? SlotId { get; set; }
    public string? SlotCode { get; set; }
    public DateTime CheckInTime { get; set; }
    public string? CheckInImagePath { get; set; }
    public SessionStatus Status { get; set; }
    public bool IsMonthlyPass { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public CustomerType CustomerType { get; set; }
    public int? CreatedByUserId { get; set; }
    public decimal TotalFee { get; set; }
}
