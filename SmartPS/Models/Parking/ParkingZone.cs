namespace SmartPS.Models.Parking;

public class ParkingZone
{
    public int ZoneId { get; set; }
    public string ZoneCode { get; set; } = string.Empty; // A, B, C...
    public string ZoneName { get; set; } = string.Empty; // Khu A - Xe Máy, Khu B - Ô Tô
    public int? VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public int TotalCapacity { get; set; }
    public string? Description { get; set; }

    public ICollection<ParkingSlot> Slots { get; set; } = new List<ParkingSlot>();
}
