using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Customers;

namespace SmartPS.ViewModels.ParkingMap;

/// <summary>Một khu trên sơ đồ bãi: nhãn đối tượng, số chỗ trống/tổng, các ô và ô chọn đối tượng (chỉ sửa được khi có Parking.Configure).</summary>
public class ParkingZoneGroupViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;

    public ParkingZoneGroupViewModel(
        ZoneGroupData data,
        IEnumerable<ParkingSlotItemViewModel> slots,
        bool canConfigure,
        ILocalizationService localization,
        Func<ParkingZoneGroupViewModel, Task> saveAudienceAsync)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        CanEdit = canConfigure && data.ZoneId.HasValue;

        foreach (var slot in slots)
        {
            Slots.Add(slot);
        }

        foreach (var audience in Enum.GetValues<ZoneAudience>())
        {
            AudienceOptions.Add(new OptionItem<ZoneAudience>(audience, _localization.GetString($"Str_Map_Audience_{audience}")));
        }

        _selectedAudience = data.Audience;
        SaveAudienceCommand = new AsyncRelayCommand(() => saveAudienceAsync(this), () => CanEdit && SelectedAudience != data.Audience);
    }

    public ZoneGroupData Data { get; }

    public int? ZoneId => Data.ZoneId;

    public string ZoneCode => Data.ZoneCode;

    public string ZoneName => string.IsNullOrWhiteSpace(Data.ZoneName) ? _localization.GetString("Str_Map_Unzoned") : Data.ZoneName;

    public ZoneAudience Audience => Data.Audience;

    public string AudienceText => _localization.GetString($"Str_Map_Audience_{Data.Audience}");

    public bool IsResidentOnly => Data.Audience == ZoneAudience.ResidentOnly;

    public bool IsVisitorOnly => Data.Audience == ZoneAudience.VisitorOnly;

    public string SummaryText => _localization.GetString("Str_Map_ZoneSummary", Data.AvailableCount, Data.TotalCount);

    public string MaintenanceText => Data.MaintenanceCount > 0 ? _localization.GetString("Str_Map_MaintenanceCount", Data.MaintenanceCount) : string.Empty;

    public bool CanEdit { get; }

    public ObservableCollection<ParkingSlotItemViewModel> Slots { get; } = new();

    public ObservableCollection<OptionItem<ZoneAudience>> AudienceOptions { get; } = new();

    private ZoneAudience _selectedAudience;
    public ZoneAudience SelectedAudience
    {
        get => _selectedAudience;
        set
        {
            if (SetProperty(ref _selectedAudience, value))
            {
                SaveAudienceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveAudienceCommand { get; }
}
