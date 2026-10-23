namespace SmartPS.Models.Parking;

public class ParkingSettings
{
    public int SettingsId { get; set; }
    public int DefaultMaxVehiclesPerHousehold { get; set; } = 2;

    /// <summary>Xe đang gửi lâu hơn số giờ này được coi là quá hạn (quét sự cố "Xe gửi quá hạn").</summary>
    public int OverstayThresholdHours { get; set; } = 48;
}
