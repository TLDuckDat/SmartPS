using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.ParkingMap;

public class ParkingSlotItemViewModel : ViewModelBase
{
    public int SlotId { get; set; }
    public string SlotCode { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public int VehicleTypeId { get; set; }
    public string VehicleTypeName { get; set; } = string.Empty;

    private SlotStatus _status;
    public SlotStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsOccupied));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    private string? _currentLicensePlate;
    public string? CurrentLicensePlate
    {
        get => _currentLicensePlate;
        set => SetProperty(ref _currentLicensePlate, value);
    }

    private DateTime? _checkInTime;
    public DateTime? CheckInTime
    {
        get => _checkInTime;
        set => SetProperty(ref _checkInTime, value);
    }

    private string? _ticketCode;
    public string? TicketCode
    {
        get => _ticketCode;
        set => SetProperty(ref _ticketCode, value);
    }

    private string? _customerName;
    public string? CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

    public bool IsOccupied => Status == SlotStatus.Occupied;
    public string StatusBadgeText => IsOccupied ? "ĐÃ ĐỖ XE" : "CÒN TRỐNG";
    public string VehicleIcon => VehicleTypeId == 1 ? "🏍" : "🚗";
}
