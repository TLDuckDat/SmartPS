using SmartPS.Models.Auth;

namespace SmartPS.Models.Parking;

public class ParkingSession
{
    public int SessionId { get; set; }
    public string TicketCode { get; set; } = string.Empty; // Mã thẻ hoặc chuỗi sinh QR
    public string LicensePlate { get; set; } = string.Empty; // Biển số xe

    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    public int? SlotId { get; set; }
    public ParkingSlot? Slot { get; set; }

    public DateTime CheckInTime { get; set; } = DateTime.UtcNow;
    public DateTime? CheckOutTime { get; set; }

    public string? CheckInImagePath { get; set; }
    public string? CheckOutImagePath { get; set; }

    public decimal TotalFee { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public bool IsMonthlyPass { get; set; } = false;
    public CustomerType CustomerType { get; set; } = CustomerType.Regular;

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    /// <summary>
    /// Thời gian xe vào quy đổi theo múi giờ máy trạm địa phương phục vụ hiển thị chính xác
    /// </summary>
    public DateTime CheckInTimeLocal => CheckInTime.Kind == DateTimeKind.Utc ? CheckInTime.ToLocalTime() : CheckInTime;

    /// <summary>
    /// Thời gian xe ra quy đổi theo múi giờ máy trạm địa phương
    /// </summary>
    public DateTime? CheckOutTimeLocal => CheckOutTime.HasValue 
        ? (CheckOutTime.Value.Kind == DateTimeKind.Utc ? CheckOutTime.Value.ToLocalTime() : CheckOutTime.Value) 
        : null;

    /// <summary>
    /// Thời lượng xe đã gửi trong bãi (hoặc thời lượng gửi thực tế nếu đã check-out)
    /// </summary>
    public TimeSpan CurrentDuration => CheckOutTime.HasValue 
        ? CheckOutTime.Value - CheckInTime 
        : DateTime.UtcNow - CheckInTime;

    /// <summary>
    /// Chuỗi định dạng hiển thị thời lượng chi tiết (ví dụ: '1 giờ 45 phút')
    /// </summary>
    public string DurationDisplay
    {
        get
        {
            var d = CurrentDuration;
            if (d.TotalSeconds < 60) return "Dưới 1 phút";
            if (d.TotalDays >= 1)
                return $"{(int)d.TotalDays} ngày {d.Hours} giờ {d.Minutes} phút";
            if (d.TotalHours >= 1)
                return $"{(int)d.TotalHours} giờ {d.Minutes} phút";
            return $"{d.Minutes} phút";
        }
    }
}
