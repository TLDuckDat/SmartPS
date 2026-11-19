using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.Overview;

/// <summary>
/// Một dòng "Xe vào ra gần đây". Xe đã ra hiển thị phí đã thu; xe đang trong bãi hiển thị phí tạm tính
/// được cập nhật định kỳ.
/// </summary>
public sealed class RecentSessionItem : ViewModelBase
{
    public RecentSessionItem(ParkingSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public ParkingSession Session { get; }

    // Chuyển tiếp các thuộc tính mà bảng đang dùng
    public string TicketCode => Session.TicketCode;
    public string LicensePlate => Session.LicensePlate;
    public VehicleType? VehicleType => Session.VehicleType;
    public ParkingSlot? Slot => Session.Slot;
    public SessionStatus Status => Session.Status;
    public DateTime CheckInTimeLocal => Session.CheckInTimeLocal;
    public DateTime? CheckOutTimeLocal => Session.CheckOutTimeLocal;
    public decimal TotalFee => Session.TotalFee;

    public bool IsActive => Session.Status == SessionStatus.Active;

    private decimal? _estimatedFee;
    /// <summary>Phí tạm tính (chỉ có với xe đang trong bãi và nạp được bảng giá).</summary>
    public decimal? EstimatedFee
    {
        get => _estimatedFee;
        private set
        {
            if (SetProperty(ref _estimatedFee, value))
            {
                OnPropertyChanged(nameof(Fee));
                OnPropertyChanged(nameof(IsEstimated));
            }
        }
    }

    /// <summary>Giá trị hiển thị ở cột tiền: phí đã thu (xe đã ra) hoặc phí tạm tính (xe đang trong bãi).</summary>
    public decimal Fee => IsActive && EstimatedFee.HasValue ? EstimatedFee.Value : Session.TotalFee;

    public bool IsEstimated => IsActive && EstimatedFee.HasValue;

    public void UpdateEstimate(decimal? fee) => EstimatedFee = IsActive ? fee : null;
}
