using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class VehicleDialogViewModel : ViewModelBase
{
    private Customer? _selectedCustomer;
    private VehicleType? _selectedVehicleType;
    private string _licensePlate = string.Empty;
    private string? _errorMessage;

    public event Action<bool>? RequestClose;

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<VehicleType> VehicleTypes { get; } = new();

    public Customer? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
                ErrorMessage = null;
        }
    }

    public VehicleType? SelectedVehicleType
    {
        get => _selectedVehicleType;
        set
        {
            if (SetProperty(ref _selectedVehicleType, value))
                ErrorMessage = null;
        }
    }

    public string LicensePlate
    {
        get => _licensePlate;
        set
        {
            if (SetProperty(ref _licensePlate, value))
                ErrorMessage = null;
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public VehicleDialogViewModel(
        IEnumerable<Customer>? customers = null,
        IEnumerable<VehicleType>? vehicleTypes = null,
        int? preselectedCustomerId = null)
    {
        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

        if (customers != null)
        {
            foreach (var c in customers)
                Customers.Add(c);
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

        if (preselectedCustomerId.HasValue)
        {
            SelectedCustomer = Customers.FirstOrDefault(c => c.CustomerId == preselectedCustomerId.Value);
        }

        if (SelectedCustomer == null && Customers.Count > 0)
        {
            SelectedCustomer = Customers[0];
        }

        SelectedVehicleType = VehicleTypes.FirstOrDefault();
    }

    public bool Validate()
    {
        var cleanPlate = Regex.Replace(LicensePlate?.Trim() ?? string.Empty, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
        if (string.IsNullOrEmpty(cleanPlate))
        {
            ErrorMessage = AppText.Get("Msg_Customers_DlgPlateInvalid");
            return false;
        }

        if (cleanPlate.Length > 20)
        {
            ErrorMessage = AppText.Get("Msg_Customers_DlgPlateTooLong");
            return false;
        }

        if (SelectedCustomer == null)
        {
            ErrorMessage = AppText.Get("Msg_Customers_DlgChooseOwner");
            return false;
        }

        if (SelectedVehicleType == null)
        {
            ErrorMessage = "Vui lòng chọn loại phương tiện.";
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

    public CreateVehicleRequest ToRequest()
    {
        var cleanPlate = Regex.Replace(LicensePlate?.Trim() ?? string.Empty, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
        return new CreateVehicleRequest(
            SelectedCustomer!.CustomerId,
            cleanPlate,
            SelectedVehicleType!.VehicleTypeId
        );
    }
}
