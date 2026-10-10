namespace SmartPS.Models.Parking;

public class ParkingSettings
{
    public int SettingsId { get; set; }
    public int DefaultMaxVehiclesPerHousehold { get; set; } = 2;
}
