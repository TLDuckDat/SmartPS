namespace SmartPS.Models.Parking;

public class Household
{
    public int HouseholdId { get; set; }
    public string ApartmentCode { get; set; } = string.Empty;
    public int? MaxVehicles { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
}
