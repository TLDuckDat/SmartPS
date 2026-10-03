namespace SmartPS.Models.Parking;

public class ParkingSlot
{
    public int SlotId { get; set; }
    public string SlotCode { get; set; } = string.Empty; // Ví dụ: A-01, A-02, B-01...
    public string ZoneName { get; set; } = string.Empty; // Ví dụ: "Khu A - Xe Máy", "Khu B - Ô Tô"
    public int? ZoneId { get; set; }
    public ParkingZone? Zone { get; set; }
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public SlotStatus Status { get; set; } = SlotStatus.Available;

    // Tọa độ và kích thước ô đỗ trên Sơ đồ bãi xe 2D (Canvas)
    public double CoordX { get; set; }
    public double CoordY { get; set; }
    public double Width { get; set; } = 80;
    public double Height { get; set; } = 120;

    public string? CurrentLicensePlate { get; set; }

    public ICollection<ParkingSession> ParkingSessions { get; set; } = new List<ParkingSession>();
}
