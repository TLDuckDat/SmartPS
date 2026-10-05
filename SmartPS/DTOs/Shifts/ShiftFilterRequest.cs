using SmartPS.Models.Shifts;

namespace SmartPS.DTOs.Shifts;

public class ShiftFilterRequest
{
    public int? UserId { get; set; }
    public DateTime? OpenedFromUtc { get; set; }
    public DateTime? OpenedToUtc { get; set; }
    public ShiftStatus? Status { get; set; }
    public int Limit { get; set; } = 200;
}
