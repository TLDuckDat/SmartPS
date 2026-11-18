using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class IssueTicketDialogViewModel : ViewModelBase
{
    private readonly List<Vehicle> _allVehicles;
    private readonly List<PricingRule> _pricingRules;

    private Customer? _selectedCustomer;
    private Vehicle? _selectedVehicle;
    private int _durationMonths = 1;
    private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
    private decimal _estimatedFeePreview;
    private string? _errorMessage;

    public event Action<bool>? RequestClose;

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<Vehicle> AvailableVehicles { get; } = new();

    public Customer? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                OnCustomerChanged();
                ErrorMessage = null;
            }
        }
    }

    public Vehicle? SelectedVehicle
    {
        get => _selectedVehicle;
        set
        {
            if (SetProperty(ref _selectedVehicle, value))
            {
                UpdateFeePreview();
                ErrorMessage = null;
            }
        }
    }

    public int DurationMonths
    {
        get => _durationMonths;
        set
        {
            if (SetProperty(ref _durationMonths, value))
            {
                UpdateFeePreview();
                ErrorMessage = null;
            }
        }
    }

    public PaymentMethod SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set => SetProperty(ref _selectedPaymentMethod, value);
    }

    public decimal EstimatedFeePreview
    {
        get => _estimatedFeePreview;
        private set
        {
            if (SetProperty(ref _estimatedFeePreview, value))
            {
                OnPropertyChanged(nameof(EstimatedFeeDisplay));
            }
        }
    }

    public string EstimatedFeeDisplay => $"{EstimatedFeePreview:N0} đ";

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public IssueTicketDialogViewModel(
        IEnumerable<Customer>? customers = null,
        IEnumerable<Vehicle>? vehicles = null,
        IEnumerable<PricingRule>? pricingRules = null,
        int? preselectedCustomerId = null)
    {
        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

        _allVehicles = vehicles?.ToList() ?? new List<Vehicle>();
        _pricingRules = pricingRules?.ToList() ?? new List<PricingRule>();

        if (customers != null)
        {
            foreach (var c in customers)
                Customers.Add(c);
        }

        if (preselectedCustomerId.HasValue)
        {
            SelectedCustomer = Customers.FirstOrDefault(c => c.CustomerId == preselectedCustomerId.Value);
        }

        if (SelectedCustomer == null && Customers.Count > 0)
        {
            SelectedCustomer = Customers[0];
        }
        else
        {
            OnCustomerChanged();
        }
    }

    private void OnCustomerChanged()
    {
        AvailableVehicles.Clear();
        if (SelectedCustomer != null)
        {
            var userVehicles = _allVehicles.Where(v => v.OwnerCustomerId == SelectedCustomer.CustomerId && v.IsActive).ToList();
            foreach (var v in userVehicles)
                AvailableVehicles.Add(v);

            SelectedVehicle = AvailableVehicles.FirstOrDefault();
        }
        else
        {
            SelectedVehicle = null;
        }

        UpdateFeePreview();
    }

    public void UpdateFeePreview()
    {
        if (SelectedVehicle == null)
        {
            EstimatedFeePreview = 0;
            return;
        }

        var rule = _pricingRules.FirstOrDefault(r => r.VehicleTypeId == SelectedVehicle.VehicleTypeId);
        if (rule != null)
        {
            EstimatedFeePreview = DurationMonths switch
            {
                1 => rule.Monthly1Price,
                3 => rule.Monthly3Price > 0 ? rule.Monthly3Price : rule.Monthly1Price * 3,
                6 => rule.Monthly6Price > 0 ? rule.Monthly6Price : rule.Monthly1Price * 6,
                _ => rule.Monthly1Price * DurationMonths
            };
        }
        else
        {
            EstimatedFeePreview = 0;
        }
    }

    public bool Validate()
    {
        if (SelectedCustomer == null)
        {
            ErrorMessage = "Vui lòng chọn khách hàng.";
            return false;
        }

        if (SelectedVehicle == null)
        {
            ErrorMessage = AppText.Get("Str_Customers_DlgNoVehicle");
            return false;
        }

        if (DurationMonths <= 0)
        {
            ErrorMessage = "Thời hạn vé không hợp lệ.";
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

    public IssueTicketRequest ToRequest()
    {
        return new IssueTicketRequest(
            SelectedCustomer!.CustomerId,
            SelectedVehicle!.VehicleId,
            DurationMonths,
            SelectedPaymentMethod
        );
    }
}
