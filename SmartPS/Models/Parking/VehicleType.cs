namespace SmartPS.Models.Parking;

public class VehicleType
{
    public int VehicleTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<ParkingSlot> ParkingSlots { get; set; } = new List<ParkingSlot>();
    public ICollection<ParkingSession> ParkingSessions { get; set; } = new List<ParkingSession>();
    public ICollection<PricingRule> PricingRules { get; set; } = new List<PricingRule>();
}
