namespace SmartPS.Models.Parking;

public class CustomerVehicle
{
    public int CustomerVehicleId { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string LicensePlate { get; set; } = string.Empty; // Đã chuẩn hoá (chữ + số)
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }
}
