namespace SmartPS.Models.Parking;

public class BlacklistEntry
{
    public int BlacklistEntryId { get; set; }
    public string LicensePlate { get; set; } = string.Empty; // Đã chuẩn hoá (chữ + số)
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public SmartPS.Models.Auth.User? CreatedByUser { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedAt { get; set; }
    public int? RemovedByUserId { get; set; }
    public SmartPS.Models.Auth.User? RemovedByUser { get; set; }
    public string? RemoveReason { get; set; }
}
