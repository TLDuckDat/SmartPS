namespace SmartPS.Models.Parking;

public class CustomerTier
{
    public int TierId { get; set; }
    public CustomerType CustomerType { get; set; } // Regular, Loyal, VIP
    public string TierName { get; set; } = string.Empty; // "Khách Vãng Lai", "Khách Thân Quen", "Khách VIP / Cư Dân"
    public double DiscountPercentage { get; set; } // 0, 10, 20
    public string BadgeColor { get; set; } = "#64748B"; // Hex color
    public string BadgeIcon { get; set; } = "👤"; // 👤, 🌟, 👑
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public string DisplayName => $"{BadgeIcon} {TierName} (-{DiscountPercentage:0}%)";
}
