using System;
using System.Windows.Input;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class RenewTicketDialogViewModel : ViewModelBase
{
    private readonly MonthlyTicket _ticket;
    private readonly PricingRule? _rule;

    private int _addedMonths = 1;
    private PaymentMethod _selectedPaymentMethod = PaymentMethod.Cash;
    private decimal _estimatedFee;
    private DateTime _newEndDate;
    private string? _errorMessage;

    public event Action<bool>? RequestClose;

    public int TicketId => _ticket.TicketId;
    public string TicketCode => _ticket.TicketCode;
    public string CustomerInfo => $"{_ticket.Customer?.FullName ?? "N/A"} ({_ticket.RegisteredLicensePlate})";
    public string CurrentExpiryDisplay => _ticket.EndDate.ToString("dd/MM/yyyy HH:mm");

    public int AddedMonths
    {
        get => _addedMonths;
        set
        {
            if (SetProperty(ref _addedMonths, value))
            {
                UpdatePreview();
                ErrorMessage = null;
            }
        }
    }

    public PaymentMethod SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set => SetProperty(ref _selectedPaymentMethod, value);
    }

    public decimal EstimatedFee
    {
        get => _estimatedFee;
        private set
        {
            if (SetProperty(ref _estimatedFee, value))
            {
                OnPropertyChanged(nameof(EstimatedFeeDisplay));
            }
        }
    }

    public string EstimatedFeeDisplay => $"{EstimatedFee:N0} VNĐ";

    public DateTime NewEndDate
    {
        get => _newEndDate;
        private set
        {
            if (SetProperty(ref _newEndDate, value))
            {
                OnPropertyChanged(nameof(NewEndDateDisplay));
            }
        }
    }

    public string NewEndDateDisplay => NewEndDate.ToString("dd/MM/yyyy HH:mm");

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public RenewTicketDialogViewModel(MonthlyTicket ticket, PricingRule? rule)
    {
        _ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
        _rule = rule;

        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));

        UpdatePreview();
    }

    public void UpdatePreview()
    {
        decimal fee = 0;
        if (_rule != null)
        {
            fee = AddedMonths switch
            {
                1 => _rule.Monthly1Price,
                3 => _rule.Monthly3Price > 0 ? _rule.Monthly3Price : _rule.Monthly1Price * 3,
                6 => _rule.Monthly6Price > 0 ? _rule.Monthly6Price : _rule.Monthly1Price * 6,
                _ => _rule.Monthly1Price * AddedMonths
            };
        }
        else
        {
            fee = 100000m * AddedMonths;
        }

        var now = DateTime.UtcNow;
        DateTime baseDate = _ticket.EndDate < now ? now : _ticket.EndDate;
        DateTime newExp = baseDate.AddMonths(AddedMonths);

        EstimatedFee = fee;
        NewEndDate = newExp;
    }

    public bool Validate()
    {
        if (AddedMonths <= 0)
        {
            ErrorMessage = "Số tháng gia hạn không hợp lệ.";
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

    public RenewTicketRequest ToRequest()
    {
        return new RenewTicketRequest(
            TicketId,
            AddedMonths,
            SelectedPaymentMethod
        );
    }
}
