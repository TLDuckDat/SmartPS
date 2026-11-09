using System;
using System.Windows.Input;
using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class HouseholdDialogViewModel : ViewModelBase
{
    private string _apartmentCode = string.Empty;
    private int? _maxVehicles = null;
    private string? _notes;
    private string? _errorMessage;

    public event Action<bool>? RequestClose;

    public string ApartmentCode
    {
        get => _apartmentCode;
        set
        {
            if (SetProperty(ref _apartmentCode, value))
            {
                ErrorMessage = null;
            }
        }
    }

    public int? MaxVehicles
    {
        get => _maxVehicles;
        set
        {
            if (SetProperty(ref _maxVehicles, value))
            {
                ErrorMessage = null;
            }
        }
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public HouseholdDialogViewModel()
    {
        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ApartmentCode))
        {
            ErrorMessage = AppText.Get("Msg_Customers_DlgAptRequired");
            return false;
        }

        if (MaxVehicles.HasValue && MaxVehicles.Value < 1)
        {
            ErrorMessage = AppText.Get("Msg_Customers_DlgAptMaxVehicles");
            return false;
        }

        ErrorMessage = null;
        return true;
    }

    private void OnSave()
    {
        if (!Validate()) return;
        RequestClose?.Invoke(true);
    }

    public CreateHouseholdRequest ToRequest()
    {
        return new CreateHouseholdRequest(
            ApartmentCode.Trim(),
            MaxVehicles,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        );
    }
}
