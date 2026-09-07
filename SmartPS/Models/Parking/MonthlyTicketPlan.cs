namespace SmartPS.Models.Parking;

public class MonthlyTicketPlan
{
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty; // e.g. "Gói Xe Máy 1 Tháng", "Gói Ô Tô 6 Tháng (Giảm 5%)"
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public int DurationMonths { get; set; } // 1, 3, 6, 12
    public decimal PricePerMonth { get; set; }
    public double DiscountPercentage { get; set; } // 0, 5, 10
    public decimal TotalPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    public string DisplayText => $"{PlanName} - {TotalPrice:N0} VNĐ ({DurationMonths} tháng)";
}
