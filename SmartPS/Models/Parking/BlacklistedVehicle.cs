using System;
using System.ComponentModel.DataAnnotations;

namespace SmartPS.Models.Parking;

public class BlacklistedVehicle
{
    [Key]
    public int BlacklistId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
