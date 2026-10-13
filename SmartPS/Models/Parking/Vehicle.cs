namespace SmartPS.Models.Parking;

public class Vehicle
{
    public int VehicleId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public string? Color { get; set; }
    public string? Brand { get; set; }
    public int OwnerCustomerId { get; set; }
    public Customer? OwnerCustomer { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
