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
}
