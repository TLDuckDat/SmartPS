using SmartPS.Models.Parking;

namespace SmartPS.Models.GateControl;

public class GateCheckOutResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ParkingSession? CompletedSession { get; set; }
}
