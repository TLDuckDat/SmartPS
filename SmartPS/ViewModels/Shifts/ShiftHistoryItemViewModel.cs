using SmartPS.Models.Shifts;
using SmartPS.Services.Localization;
using SmartPS.ViewModels;

namespace SmartPS.ViewModels.Shifts;

public class ShiftHistoryItemViewModel : ViewModelBase
{
    private readonly ILocalizationService _localizationService;
    public Shift Shift { get; }

    public int ShiftId => Shift.ShiftId;
    public string ShiftCode => $"SHIFT-{Shift.ShiftId:D6}";
    public string EmployeeName => Shift.OpenedByUser?.FullName ?? $"User #{Shift.OpenedByUserId}";
    public DateTime OpenedAtLocal => Shift.OpenedAt.Kind == DateTimeKind.Utc ? Shift.OpenedAt.ToLocalTime() : Shift.OpenedAt;
    public DateTime? ClosedAtLocal => Shift.ClosedAt.HasValue
        ? (Shift.ClosedAt.Value.Kind == DateTimeKind.Utc ? Shift.ClosedAt.Value.ToLocalTime() : Shift.ClosedAt.Value)
        : null;
    public string BeginningCashFormatted => FormatAmount(Shift.BeginningCash);
    public string ExpectedCashFormatted => Shift.ExpectedCash.HasValue ? FormatAmount(Shift.ExpectedCash.Value) : "--";
    public string ActualCashFormatted => Shift.ActualCash.HasValue ? FormatAmount(Shift.ActualCash.Value) : "--";
    public string DifferenceFormatted => Shift.Difference.HasValue ? FormatAmount(Shift.Difference.Value) : "--";
    public string StatusText => Shift.Status switch
    {
        ShiftStatus.Active => _localizationService.GetString("Str_Shifts_StatusActive"),
        ShiftStatus.Locked => _localizationService.GetString("Str_Shifts_StatusLocked"),
        ShiftStatus.Reviewed => _localizationService.GetString("Str_Shifts_StatusReviewed"),
        _ => Shift.Status.ToString()
    };

    public ShiftHistoryItemViewModel(Shift shift, ILocalizationService localizationService)
    {
        Shift = shift ?? throw new ArgumentNullException(nameof(shift));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(BeginningCashFormatted));
        OnPropertyChanged(nameof(ExpectedCashFormatted));
        OnPropertyChanged(nameof(ActualCashFormatted));
        OnPropertyChanged(nameof(DifferenceFormatted));
        OnPropertyChanged(nameof(StatusText));
    }

    private string FormatAmount(decimal amount)
        => _localizationService.GetString("Str_Shifts_CurrencyFormat", amount);
}
