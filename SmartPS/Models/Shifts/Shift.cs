using SmartPS.Models.Auth;

namespace SmartPS.Models.Shifts;

public class Shift
{
    public int ShiftId { get; set; }

    /// <summary>Mã ca dễ đọc CA-yyMMdd-01 (số thứ tự ca trong ngày, giờ Việt Nam).</summary>
    public string? ShiftCode { get; set; }

    /// <summary>Mã hiển thị: ShiftCode, hoặc CA-#Id với ca cũ chưa có mã.</summary>
    public string DisplayCode => string.IsNullOrWhiteSpace(ShiftCode) ? $"CA-#{ShiftId}" : ShiftCode;

    public int OpenedByUserId { get; set; }
    public User? OpenedByUser { get; set; }

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public decimal BeginningCash { get; set; }

    public DateTime? ClosedAt { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? Difference { get; set; }

    public ShiftStatus Status { get; set; } = ShiftStatus.Active;

    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ManagerNote { get; set; }

    public ICollection<FinancialTransaction> Transactions { get; set; } = new List<FinancialTransaction>();
}
