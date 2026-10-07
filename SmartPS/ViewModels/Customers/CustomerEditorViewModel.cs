using System.Collections.ObjectModel;
using SmartPS.Constants;
using SmartPS.Models.Parking;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

/// <summary>Chuyển kết quả thao tác thành thông báo đã dịch.</summary>
internal static class OperationMessages
{
    public static string Describe(ILocalizationService localization, OperationResult result)
    {
        if (result.IsPermissionDenied)
        {
            return localization.GetString("Msg_Auth_PermissionDenied");
        }

        if (result.ValidationErrors.Count > 0)
        {
            return string.Join(Environment.NewLine,
                result.ValidationErrors.Select(e => localization.GetString($"Msg_CustomerValidation_{e}")));
        }

        return localization.GetString($"Msg_Op_{result.Error}");
    }
}

/// <summary>Giá trị chọn trong ComboBox với nhãn đã dịch.</summary>
public sealed class OptionItem<T>
{
    public OptionItem(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public T Value { get; }

    public string DisplayName { get; }
}

/// <summary>Một xe trong biểu mẫu; xe chưa lưu (khách mới) có CustomerVehicleId bằng 0.</summary>
public sealed class VehicleRowViewModel
{
    public VehicleRowViewModel(int customerVehicleId, string licensePlate, int vehicleTypeId, string vehicleTypeName)
    {
        CustomerVehicleId = customerVehicleId;
        LicensePlate = licensePlate;
        VehicleTypeId = vehicleTypeId;
        VehicleTypeName = vehicleTypeName;
    }

    public int CustomerVehicleId { get; }

    public string LicensePlate { get; }

    public int VehicleTypeId { get; }

    public string VehicleTypeName { get; }

    public bool IsPending => CustomerVehicleId == 0;
}

/// <summary>Một vé tháng trong biểu mẫu kèm nhãn trạng thái đã dịch.</summary>
public sealed class TicketRowViewModel
{
    public TicketRowViewModel(MonthlyTicketDto dto, string statusText)
    {
        Dto = dto;
        StatusText = statusText;
    }

    public MonthlyTicketDto Dto { get; }

    public string StatusText { get; }

    public string TicketCode => Dto.TicketCode;

    public string LicensePlate => Dto.LicensePlate;

    public string PlanName => Dto.PlanName ?? string.Empty;

    public string StartText => AuditDate(Dto.StartDateUtc);

    public string EndText => TicketDates.LastValidDateVn(Dto.EndDateUtc).ToString("dd/MM/yyyy");

    public decimal Price => Dto.Price;

    public bool IsSuspended => Dto.Status == MonthlyTicketStatus.Suspended;

    public bool IsActiveStatus => Dto.Status == MonthlyTicketStatus.Active;

    private static string AuditDate(DateTime utc)
        => SmartPS.Services.Audit.AuditTime.ToVietnamTime(utc).ToString("dd/MM/yyyy");
}

/// <summary>Biểu mẫu thêm/sửa khách hàng kèm xe và vé tháng. Mọi lệnh ghi chỉ chạy được khi có Customer.Manage.</summary>
public class CustomerEditorViewModel : ViewModelBase
{
    private readonly ICustomerService _customers;
    private readonly IMonthlyTicketService _tickets;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialog;
    private readonly ILocalizationService _localization;
    private readonly Func<Task> _onChanged;

    private IReadOnlyList<VehicleType> _vehicleTypes = Array.Empty<VehicleType>();
    private IReadOnlyList<MonthlyTicketPlan> _plans = Array.Empty<MonthlyTicketPlan>();

    public CustomerEditorViewModel(
        ICustomerService customers,
        IMonthlyTicketService tickets,
        IPermissionService permissions,
        IDialogService dialogService,
        ILocalizationService localization,
        Func<Task>? onChanged = null)
    {
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialog = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _onChanged = onChanged ?? (() => Task.CompletedTask);

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanManage && IsOpen);
        CancelCommand = new RelayCommand(Close);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => CanManage && IsOpen && !IsNew);
        AddVehicleCommand = new AsyncRelayCommand(AddVehicleAsync, () => CanManage && IsOpen && !string.IsNullOrWhiteSpace(NewVehiclePlate));
        RemoveVehicleCommand = new AsyncRelayCommand(RemoveVehicleAsync, p => CanManage && p is VehicleRowViewModel);
        CreateTicketCommand = new AsyncRelayCommand(CreateTicketAsync, () => CanManage && IsOpen && !IsNew && SelectedVehicle != null && SelectedPlan != null);
        RenewTicketCommand = new AsyncRelayCommand(p => TicketActionAsync(p, TicketAction.Renew), p => CanManage && p is MonthlyTicketDto);
        SuspendTicketCommand = new AsyncRelayCommand(p => TicketActionAsync(p, TicketAction.Suspend), p => CanManage && p is MonthlyTicketDto);
        ResumeTicketCommand = new AsyncRelayCommand(p => TicketActionAsync(p, TicketAction.Resume), p => CanManage && p is MonthlyTicketDto);

        RebuildOptions();
        _localization.LanguageChanged += RebuildOptions;
    }

    private enum TicketAction
    {
        Renew,
        Suspend,
        Resume
    }

    public bool CanManage => _permissions.HasPermission(Permissions.CustomerManage);

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand AddVehicleCommand { get; }
    public AsyncRelayCommand RemoveVehicleCommand { get; }
    public AsyncRelayCommand CreateTicketCommand { get; }
    public AsyncRelayCommand RenewTicketCommand { get; }
    public AsyncRelayCommand SuspendTicketCommand { get; }
    public AsyncRelayCommand ResumeTicketCommand { get; }

    public ObservableCollection<VehicleRowViewModel> Vehicles { get; } = new();
    public ObservableCollection<TicketRowViewModel> Tickets { get; } = new();
    public ObservableCollection<OptionItem<CustomerType>> TypeOptions { get; } = new();
    public ObservableCollection<VehicleType> VehicleTypeOptions { get; } = new();
    public ObservableCollection<MonthlyTicketPlan> PlanOptions { get; } = new();

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value))
            {
                RaiseCommands();
            }
        }
    }

    private bool _isNew;
    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (SetProperty(ref _isNew, value))
            {
                OnPropertyChanged(nameof(CanShowTickets));
                RaiseCommands();
            }
        }
    }

    public bool CanShowTickets => IsOpen && !IsNew;

    private int _customerId;
    public int CustomerId
    {
        get => _customerId;
        private set => SetProperty(ref _customerId, value);
    }

    private string _fullName = string.Empty;
    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value ?? string.Empty); }

    private string _phoneNumber = string.Empty;
    public string PhoneNumber { get => _phoneNumber; set => SetProperty(ref _phoneNumber, value ?? string.Empty); }

    private string _email = string.Empty;
    public string Email { get => _email; set => SetProperty(ref _email, value ?? string.Empty); }

    private string _identityCard = string.Empty;
    public string IdentityCard { get => _identityCard; set => SetProperty(ref _identityCard, value ?? string.Empty); }

    private bool _isResident;
    public bool IsResident { get => _isResident; set => SetProperty(ref _isResident, value); }

    private string _apartmentCode = string.Empty;
    public string ApartmentCode { get => _apartmentCode; set => SetProperty(ref _apartmentCode, value ?? string.Empty); }

    private string _building = string.Empty;
    public string Building { get => _building; set => SetProperty(ref _building, value ?? string.Empty); }

    private CustomerType _selectedType = CustomerType.Regular;
    public CustomerType SelectedType { get => _selectedType; set => SetProperty(ref _selectedType, value); }

    private string _notes = string.Empty;
    public string Notes { get => _notes; set => SetProperty(ref _notes, value ?? string.Empty); }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ToggleActiveText));
            }
        }
    }

    public string ToggleActiveText => _localization.GetString(IsActive ? "Str_Cust_Lock" : "Str_Cust_Unlock");

    private string _newVehiclePlate = string.Empty;
    public string NewVehiclePlate
    {
        get => _newVehiclePlate;
        set
        {
            if (SetProperty(ref _newVehiclePlate, value ?? string.Empty))
            {
                AddVehicleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private VehicleType? _newVehicleType;
    public VehicleType? NewVehicleType { get => _newVehicleType; set => SetProperty(ref _newVehicleType, value); }

    private VehicleRowViewModel? _selectedVehicle;
    public VehicleRowViewModel? SelectedVehicle
    {
        get => _selectedVehicle;
        set
        {
            if (SetProperty(ref _selectedVehicle, value))
            {
                FilterPlans();
                CreateTicketCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private MonthlyTicketPlan? _selectedPlan;
    public MonthlyTicketPlan? SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            if (SetProperty(ref _selectedPlan, value))
            {
                CreateTicketCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private DateTime? _startDate = DateTime.Today;
    public DateTime? StartDate { get => _startDate; set => SetProperty(ref _startDate, value); }

    private string _suspendReason = string.Empty;
    public string SuspendReason { get => _suspendReason; set => SetProperty(ref _suspendReason, value ?? string.Empty); }

    /// <summary>Mở biểu mẫu trống để thêm khách hàng mới.</summary>
    public void StartNew()
    {
        if (!CanManage)
        {
            return;
        }

        ResetFields();
        CustomerId = 0;
        IsNew = true;
        IsActive = true;
        IsOpen = true;
        OnPropertyChanged(nameof(CanShowTickets));
        _ = LoadReferenceDataAsync();
    }

    /// <summary>Tải hồ sơ khách hàng (xe, vé tháng) vào biểu mẫu.</summary>
    public async Task OpenAsync(int customerId)
    {
        try
        {
            await LoadReferenceDataAsync();
            var details = await _customers.GetDetailsAsync(customerId);
            if (details is null)
            {
                Close();
                return;
            }

            ResetFields();
            CustomerId = details.CustomerId;
            FullName = details.FullName;
            PhoneNumber = details.PhoneNumber;
            Email = details.Email ?? string.Empty;
            IdentityCard = details.IdentityCard ?? string.Empty;
            IsResident = details.IsResident;
            ApartmentCode = details.ApartmentCode ?? string.Empty;
            Building = details.Building ?? string.Empty;
            SelectedType = details.Type == CustomerType.Resident ? CustomerType.Regular : details.Type;
            Notes = details.Notes ?? string.Empty;
            IsActive = details.IsActive;

            foreach (var vehicle in details.Vehicles.Where(v => v.IsActive))
            {
                Vehicles.Add(new VehicleRowViewModel(vehicle.CustomerVehicleId, vehicle.LicensePlate, vehicle.VehicleTypeId, vehicle.VehicleTypeName));
            }

            foreach (var ticket in details.Tickets)
            {
                Tickets.Add(new TicketRowViewModel(ticket, _localization.GetString($"Str_TicketStatus_{ticket.DisplayStatus}")));
            }

            IsNew = false;
            IsOpen = true;
            OnPropertyChanged(nameof(CanShowTickets));
        }
        catch (PermissionDeniedException)
        {
            _dialog.ShowWarning(_localization.GetString("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(_localization.GetString("Msg_Cust_LoadError", ex.Message));
        }
    }

    public void Close()
    {
        IsOpen = false;
        IsNew = false;
        OnPropertyChanged(nameof(CanShowTickets));
    }

    // ---- commands ------------------------------------------------------------------------------------------------

    private CustomerUpsertRequest BuildRequest()
        => new()
        {
            FullName = FullName,
            PhoneNumber = PhoneNumber,
            Email = Email,
            IdentityCard = IdentityCard,
            IsResident = IsResident,
            ApartmentCode = ApartmentCode,
            Building = Building,
            Type = SelectedType,
            Notes = Notes,
            Vehicles = IsNew
                ? Vehicles.Where(v => v.IsPending).Select(v => new NewVehicle(v.LicensePlate, v.VehicleTypeId)).ToList()
                : Array.Empty<NewVehicle>()
        };

    private async Task SaveAsync()
    {
        var request = BuildRequest();
        if (IsNew)
        {
            var created = await _customers.CreateCustomerAsync(request);
            if (!created.Success)
            {
                _dialog.ShowError(OperationMessages.Describe(_localization, created));
                return;
            }

            _dialog.ShowSuccess(_localization.GetString("Msg_Cust_SaveSuccess"));
            await _onChanged();
            await OpenAsync(created.Value);
            return;
        }

        var updated = await _customers.UpdateCustomerAsync(CustomerId, request);
        if (!updated.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, updated));
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Cust_SaveSuccess"));
        await _onChanged();
        await OpenAsync(CustomerId);
    }

    private async Task ToggleActiveAsync()
    {
        if (IsActive && !_dialog.ShowConfirm(_localization.GetString("Msg_Cust_ConfirmLock")))
        {
            return;
        }

        var result = await _customers.SetCustomerActiveAsync(CustomerId, !IsActive);
        if (!result.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, result));
            return;
        }

        await _onChanged();
        await OpenAsync(CustomerId);
    }

    private async Task AddVehicleAsync()
    {
        var type = NewVehicleType ?? VehicleTypeOptions.FirstOrDefault();
        if (type is null)
        {
            _dialog.ShowError(_localization.GetString("Msg_CustomerValidation_VehicleTypeRequired"));
            return;
        }

        var vehicle = new NewVehicle(NewVehiclePlate, type.VehicleTypeId);
        if (IsNew)
        {
            // Khách mới chưa có hồ sơ: gom xe, lưu cùng lúc khi bấm Lưu
            var normalized = SmartPS.Services.GateControl.LicensePlateNormalizer.Normalize(NewVehiclePlate);
            if (!SmartPS.Services.GateControl.LicensePlateNormalizer.IsValid(normalized))
            {
                _dialog.ShowError(_localization.GetString("Msg_CustomerValidation_PlateInvalid"));
                return;
            }

            if (Vehicles.Any(v => v.LicensePlate == normalized))
            {
                _dialog.ShowError(_localization.GetString("Msg_CustomerValidation_DuplicatePlateInRequest"));
                return;
            }

            Vehicles.Add(new VehicleRowViewModel(0, normalized, type.VehicleTypeId, type.TypeName));
            NewVehiclePlate = string.Empty;
            return;
        }

        var result = await _customers.AddVehicleAsync(CustomerId, vehicle);
        if (!result.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, result));
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Cust_VehicleAdded"));
        NewVehiclePlate = string.Empty;
        await _onChanged();
        await OpenAsync(CustomerId);
    }

    private async Task RemoveVehicleAsync(object? parameter)
    {
        if (parameter is not VehicleRowViewModel vehicle)
        {
            return;
        }

        if (vehicle.IsPending)
        {
            Vehicles.Remove(vehicle);
            return;
        }

        if (!_dialog.ShowConfirm(_localization.GetString("Msg_Cust_ConfirmRemoveVehicle", vehicle.LicensePlate)))
        {
            return;
        }

        var result = await _customers.RemoveVehicleAsync(vehicle.CustomerVehicleId);
        if (!result.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, result));
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Cust_VehicleRemoved"));
        await _onChanged();
        await OpenAsync(CustomerId);
    }

    private async Task CreateTicketAsync()
    {
        if (SelectedVehicle is null || SelectedPlan is null)
        {
            return;
        }

        DateOnly? start = StartDate.HasValue ? DateOnly.FromDateTime(StartDate.Value) : null;
        var result = await _tickets.CreateTicketAsync(new CreateTicketRequest(CustomerId, SelectedVehicle.LicensePlate, SelectedPlan.PlanId, start));
        await FinishTicketActionAsync(result, "Msg_Cust_TicketCreated");
    }

    private async Task TicketActionAsync(object? parameter, TicketAction action)
    {
        if (parameter is not MonthlyTicketDto ticket)
        {
            return;
        }

        OperationResult result;
        string successKey;
        switch (action)
        {
            case TicketAction.Renew:
                var planId = SelectedPlan?.PlanId ?? ticket.PlanId;
                if (planId is null)
                {
                    _dialog.ShowError(_localization.GetString("Msg_Op_PlanNotFound"));
                    return;
                }

                result = await _tickets.RenewTicketAsync(ticket.TicketId, planId.Value);
                successKey = "Msg_Cust_TicketRenewed";
                break;
            case TicketAction.Suspend:
                result = await _tickets.SuspendTicketAsync(ticket.TicketId, string.IsNullOrWhiteSpace(SuspendReason) ? null : SuspendReason);
                successKey = "Msg_Cust_TicketSuspended";
                break;
            default:
                result = await _tickets.ResumeTicketAsync(ticket.TicketId);
                successKey = "Msg_Cust_TicketResumed";
                break;
        }

        await FinishTicketActionAsync(result, successKey);
    }

    private async Task FinishTicketActionAsync(OperationResult result, string successKey)
    {
        if (!result.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, result));
            return;
        }

        _dialog.ShowSuccess(_localization.GetString(successKey));
        await _onChanged();
        await OpenAsync(CustomerId);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private async Task LoadReferenceDataAsync()
    {
        if (_vehicleTypes.Count > 0 && _plans.Count > 0)
        {
            return;
        }

        try
        {
            _vehicleTypes = await _customers.GetVehicleTypesAsync();
            _plans = await _tickets.GetActivePlansAsync();
            VehicleTypeOptions.Clear();
            foreach (var type in _vehicleTypes)
            {
                VehicleTypeOptions.Add(type);
            }

            NewVehicleType ??= VehicleTypeOptions.FirstOrDefault();
            FilterPlans();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(_localization.GetString("Msg_Cust_LoadError", ex.Message));
        }
    }

    private void FilterPlans()
    {
        var vehicleTypeId = SelectedVehicle?.VehicleTypeId;
        PlanOptions.Clear();
        foreach (var plan in _plans.Where(p => vehicleTypeId is null || p.VehicleTypeId == vehicleTypeId))
        {
            PlanOptions.Add(plan);
        }

        if (SelectedPlan is not null && !PlanOptions.Contains(SelectedPlan))
        {
            SelectedPlan = null;
        }
    }

    private void ResetFields()
    {
        FullName = string.Empty;
        PhoneNumber = string.Empty;
        Email = string.Empty;
        IdentityCard = string.Empty;
        IsResident = false;
        ApartmentCode = string.Empty;
        Building = string.Empty;
        SelectedType = CustomerType.Regular;
        Notes = string.Empty;
        NewVehiclePlate = string.Empty;
        SuspendReason = string.Empty;
        StartDate = DateTime.Today;
        SelectedVehicle = null;
        SelectedPlan = null;
        Vehicles.Clear();
        Tickets.Clear();
    }

    private void RebuildOptions()
    {
        TypeOptions.Clear();
        foreach (var type in new[] { CustomerType.Regular, CustomerType.Loyal, CustomerType.VIP })
        {
            TypeOptions.Add(new OptionItem<CustomerType>(type, _localization.GetString($"Str_CustType_{type}")));
        }

        OnPropertyChanged(nameof(ToggleActiveText));
    }

    private void RaiseCommands()
    {
        SaveCommand.RaiseCanExecuteChanged();
        ToggleActiveCommand.RaiseCanExecuteChanged();
        AddVehicleCommand.RaiseCanExecuteChanged();
        CreateTicketCommand.RaiseCanExecuteChanged();
    }
}
