using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class ChangePlateDialogViewModel : ViewModelBase
{
    private readonly MonthlyTicket _ticket;
    private Vehicle? _selectedNewVehicle;
    private string _reason = "Đổi phương tiện / biển số";
    private string? _errorMessage;

    public event Action<bool>? RequestClose;

    public ObservableCollection<Vehicle> AvailableVehicles { get; } = new();

    public int TicketId => _ticket.TicketId;
    public string TicketCode => _ticket.TicketCode;
    public string CurrentPlate => _ticket.RegisteredLicensePlate;
    public string CustomerInfo => _ticket.Customer?.FullName ?? "N/A";

    public Vehicle? SelectedNewVehicle
    {
        get => _selectedNewVehicle;
        set
        {
            if (SetProperty(ref _selectedNewVehicle, value))
                ErrorMessage = null;
        }
    }

    public string Reason
    {
        get => _reason;
        set => SetProperty(ref _reason, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public ChangePlateDialogViewModel(MonthlyTicket ticket, IEnumerable<Vehicle>? customerVehicles = null)
    {
        _ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));

        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

        if (customerVehicles != null)
        {
            foreach (var v in customerVehicles)
            {
                // Chỉ lấy xe còn hoạt động và khác xe hiện tại của vé
                if (v.VehicleId != _ticket.VehicleId && v.IsActive)
                {
                    AvailableVehicles.Add(v);
                }
            }
        }

        SelectedNewVehicle = AvailableVehicles.FirstOrDefault();
    }

    public bool Validate()
    {
        if (SelectedNewVehicle == null)
        {
            ErrorMessage = "Vui lòng chọn phương tiện mới để gán cho vé.";
            return false;
        }

        if (SelectedNewVehicle.VehicleId == _ticket.VehicleId)
        {
            ErrorMessage = "Phương tiện mới trùng với phương tiện hiện tại của vé.";
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

    public ChangePlateRequest ToRequest()
    {
        return new ChangePlateRequest(
            TicketId,
            SelectedNewVehicle!.VehicleId,
            string.IsNullOrWhiteSpace(Reason) ? "Đổi phương tiện / biển số" : Reason.Trim()
        );
    }
}
