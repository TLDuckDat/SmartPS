using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class CustomerDialogViewModel : ViewModelBase
{
    private string _fullName = string.Empty;
    private string _phoneNumber = string.Empty;
    private string? _identityCard;
    private Household? _selectedHousehold;
    private string? _initialPlate;
    private VehicleType? _selectedVehicleType;
    private bool _isEditing;
    private string? _errorMessage;

    public event Action<bool>? RequestClose;

    public ObservableCollection<Household> Households { get; } = new();
    public ObservableCollection<VehicleType> VehicleTypes { get; } = new();

    public string FullName
    {
        get => _fullName;
        set
        {
            if (SetProperty(ref _fullName, value))
                ErrorMessage = null;
        }
    }

    public string PhoneNumber
    {
        get => _phoneNumber;
        set => SetProperty(ref _phoneNumber, value);
    }

    public string? IdentityCard
    {
        get => _identityCard;
        set => SetProperty(ref _identityCard, value);
    }

    public Household? SelectedHousehold
    {
        get => _selectedHousehold;
        set
        {
            if (SetProperty(ref _selectedHousehold, value))
                ErrorMessage = null;
        }
    }

    public int? SelectedHouseholdId => (_selectedHousehold == null || _selectedHousehold.HouseholdId == -1) ? null : _selectedHousehold.HouseholdId;

    public string? InitialPlate
    {
        get => _initialPlate;
        set
        {
            if (SetProperty(ref _initialPlate, value))
                ErrorMessage = null;
        }
    }

    public VehicleType? SelectedVehicleType
    {
        get => _selectedVehicleType;
        set => SetProperty(ref _selectedVehicleType, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public CustomerDialogViewModel(
        IEnumerable<Household>? households = null,
        IEnumerable<VehicleType>? vehicleTypes = null,
        Customer? existing = null)
    {
        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

        // Tùy chọn khách bên ngoài (HouseholdId = -1)
        var externalHousehold = new Household
        {
            HouseholdId = -1,
            ApartmentCode = AppText.Get("Str_Customers_DlgExternalOption")
        };
        Households.Add(externalHousehold);

        if (households != null)
        {
            foreach (var h in households)
            {
                if (h.HouseholdId != -1)
                    Households.Add(h);
            }
        }

        if (vehicleTypes != null)
        {
            foreach (var vt in vehicleTypes)
                VehicleTypes.Add(vt);
        }

        if (VehicleTypes.Count == 0)
        {
            VehicleTypes.Add(new VehicleType { VehicleTypeId = 1, TypeName = "Xe máy" });
            VehicleTypes.Add(new VehicleType { VehicleTypeId = 2, TypeName = "Xe ô tô" });
            VehicleTypes.Add(new VehicleType { VehicleTypeId = 3, TypeName = "Xe đạp / Xe điện" });
        }

        SelectedHousehold = externalHousehold;
        SelectedVehicleType = VehicleTypes.FirstOrDefault();

        if (existing != null)
        {
            IsEditing = true;
            FullName = existing.FullName;
            PhoneNumber = existing.PhoneNumber;
            IdentityCard = existing.IdentityCard;

            if (existing.HouseholdId.HasValue)
            {
                SelectedHousehold = Households.FirstOrDefault(h => h.HouseholdId == existing.HouseholdId.Value) ?? externalHousehold;
            }
        }
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            ErrorMessage = AppText.Get("Msg_Customers_DlgNameRequired");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(InitialPlate) && InitialPlate.Trim().Length > 20)
        {
            ErrorMessage = AppText.Get("Msg_Common_PlateTooLong");
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

    public CreateCustomerRequest ToRequest()
    {
        return new CreateCustomerRequest(
            FullName.Trim(),
            PhoneNumber?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(IdentityCard) ? null : IdentityCard.Trim(),
            SelectedHouseholdId,
            string.IsNullOrWhiteSpace(InitialPlate) ? null : InitialPlate.Trim(),
            SelectedVehicleType?.VehicleTypeId
        );
    }
}
