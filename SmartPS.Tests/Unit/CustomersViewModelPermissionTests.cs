using System.Windows.Input;
using SmartPS.Models.Parking;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>KAN-33b: the Customers screen locks its buttons (command CanExecute) without Parking.Configure.</summary>
public class CustomersViewModelPermissionTests
{
    private sealed class FakeCustomers : ICustomersService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<Customer>> GetCustomersAsync(string? keyword = null, CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<Customer>>(Array.Empty<Customer>()); }
        public Task<IReadOnlyList<Household>> GetHouseholdsAsync(string? keyword = null, CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<Household>>(Array.Empty<Household>()); }
        public Task<IReadOnlyList<BlacklistedVehicle>> GetBlacklistAsync(string? keyword = null, CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<BlacklistedVehicle>>(Array.Empty<BlacklistedVehicle>()); }
        public Task<OperationResult> AddHouseholdAsync(CreateHouseholdRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> DeleteHouseholdAsync(int householdId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> AddCustomerAsync(CreateCustomerRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> DeleteCustomerAsync(int customerId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> AddBlacklistAsync(string plate, string reason, string? notes, int? customerId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> ToggleBlacklistAsync(int blacklistId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> DeleteBlacklistAsync(int blacklistId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FakeTickets : ITicketsService
    {
        public int Calls { get; private set; }
        public Exception? HistoriesException { get; set; }

        public Task<IReadOnlyList<MonthlyTicket>> GetTicketsAsync(string? keyword = null, bool onlyExpiringSoon = false, CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<MonthlyTicket>>(Array.Empty<MonthlyTicket>()); }
        public Task<IReadOnlyList<MonthlyTicketHistory>> GetTicketHistoriesAsync(int ticketId, CancellationToken ct = default)
        {
            Calls++;
            return HistoriesException != null ? Task.FromException<IReadOnlyList<MonthlyTicketHistory>>(HistoriesException) : Task.FromResult<IReadOnlyList<MonthlyTicketHistory>>(Array.Empty<MonthlyTicketHistory>());
        }
        public Task<IReadOnlyList<PricingRule>> GetPricingRulesAsync(CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<PricingRule>>(Array.Empty<PricingRule>()); }
        public Task<decimal?> CalculateTicketFeeAsync(int vehicleTypeId, int durationMonths, CancellationToken ct = default) => Task.FromResult<decimal?>(null);
        public Task<OperationResult> IssueTicketAsync(IssueTicketRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> RenewTicketAsync(RenewTicketRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> ChangePlateAsync(ChangePlateRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> ToggleLockTicketAsync(int ticketId, string reason, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> CancelTicketAsync(int ticketId, string reason, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FakeVehicles : IVehiclesService
    {
        public int Calls { get; private set; }

        public Task<bool> CheckVehicleLimitAsync(int householdId, int? excludeVehicleId = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<Vehicle>>(Array.Empty<Vehicle>()); }
        public Task<IReadOnlyList<Vehicle>> GetVehiclesByCustomerAsync(int customerId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Vehicle>>(Array.Empty<Vehicle>());
        public Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<VehicleType>>(Array.Empty<VehicleType>()); }
        public Task<OperationResult> AddVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> DeleteVehicleAsync(int vehicleId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public string NormalizePlate(string licensePlate) => licensePlate;
    }

    private sealed record Harness(CustomersViewModel Vm, FakeCustomers Customers, FakeTickets Tickets, FakeVehicles Vehicles, FakeDialogService Dialogs, CurrentUserContext Context)
    {
        public int ServiceCalls => Customers.Calls + Tickets.Calls + Vehicles.Calls;
    }

    private static Harness CreateHarness(Models.Auth.User? user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var customers = new FakeCustomers();
        var tickets = new FakeTickets();
        var vehicles = new FakeVehicles();
        var dialogs = new FakeDialogService();
        var vm = new CustomersViewModel(customers, tickets, vehicles, dialogs, new PermissionService(context), null!);
        return new Harness(vm, customers, tickets, vehicles, dialogs, context);
    }

    private static (CustomersViewModel Vm, FakeCustomers Customers) Create(Models.Auth.User? user)
    {
        var h = CreateHarness(user);
        return (h.Vm, h.Customers);
    }

    private static IEnumerable<(string Name, ICommand Command)> Commands(CustomersViewModel vm) => new (string, ICommand)[]
    {
        (nameof(vm.RefreshCommand), vm.RefreshCommand),
        (nameof(vm.AddHouseholdCommand), vm.AddHouseholdCommand),
        (nameof(vm.AddCustomerCommand), vm.AddCustomerCommand),
        (nameof(vm.AddVehicleCommand), vm.AddVehicleCommand),
        (nameof(vm.AddTicketCommand), vm.AddTicketCommand),
        (nameof(vm.AddBlacklistCommand), vm.AddBlacklistCommand),
        (nameof(vm.DeleteHouseholdCommand), vm.DeleteHouseholdCommand),
        (nameof(vm.DeleteCustomerCommand), vm.DeleteCustomerCommand),
        (nameof(vm.DeleteTicketCommand), vm.DeleteTicketCommand),
        (nameof(vm.CancelTicketCommand), vm.CancelTicketCommand),
        (nameof(vm.ToggleTicketCommand), vm.ToggleTicketCommand),
        (nameof(vm.RenewTicketCommand), vm.RenewTicketCommand),
        (nameof(vm.ChangePlateCommand), vm.ChangePlateCommand),
        (nameof(vm.DeleteRowVehicleCommand), vm.DeleteRowVehicleCommand),
        (nameof(vm.ToggleBlacklistCommand), vm.ToggleBlacklistCommand),
        (nameof(vm.DeleteBlacklistCommand), vm.DeleteBlacklistCommand),
    };

    [Fact]
    public void Without_ParkingConfigure_every_button_command_is_locked_and_nothing_is_loaded()
    {
        var (vm, customers) = Create(TestUsers.Operator());

        Assert.False(vm.CanManage);
        foreach (var (name, command) in Commands(vm))
        {
            Assert.False(command.CanExecute(null), $"{name} must be locked without Parking.Configure");
        }

        Assert.Equal(0, customers.Calls);
    }

    [Fact]
    public void Without_login_every_button_command_is_locked()
    {
        var (vm, _) = Create(null);

        Assert.False(vm.CanManage);
        Assert.All(Commands(vm), c => Assert.False(c.Command.CanExecute(null), c.Name));
    }

    [Fact]
    public void With_ParkingConfigure_every_button_command_is_enabled()
    {
        var (vm, _) = Create(TestUsers.Manager());

        Assert.True(vm.CanManage);
        Assert.All(Commands(vm), c => Assert.True(c.Command.CanExecute(null), c.Name));
    }

    [Fact]
    public void Admin_without_explicit_grants_is_enabled()
    {
        var (vm, _) = Create(TestUsers.AdminWithoutGrants());

        Assert.True(vm.CanManage);
    }

    [Fact]
    public async Task LoadDataAsync_without_ParkingConfigure_returns_early_without_calling_any_service()
    {
        var h = CreateHarness(TestUsers.Operator());

        await h.Vm.LoadDataAsync();

        Assert.Equal(0, h.ServiceCalls);
        Assert.Empty(h.Dialogs.Warnings);
    }

    [Fact]
    public async Task LoadDataAsync_with_ParkingConfigure_calls_the_services()
    {
        var h = CreateHarness(TestUsers.Manager());
        var before = h.ServiceCalls;

        await h.Vm.LoadDataAsync();

        Assert.True(h.ServiceCalls > before);
    }

    [Fact]
    public async Task LoadTicketHistoriesAsync_shows_permission_warning_instead_of_swallowing_PermissionDenied()
    {
        var h = CreateHarness(TestUsers.Operator());
        h.Tickets.HistoriesException = new PermissionDeniedException(new[] { Permissions.ParkingConfigure }, "op");

        await h.Vm.LoadTicketHistoriesAsync(new MonthlyTicket { TicketId = 1 });

        Assert.Single(h.Dialogs.Warnings);
        Assert.Empty(h.Vm.SelectedTicketHistories);
        Assert.False(h.Vm.IsLoadingHistories);
    }

    [Fact]
    public async Task LoadTicketHistoriesAsync_still_ignores_other_exceptions()
    {
        var h = CreateHarness(TestUsers.Manager());
        h.Tickets.HistoriesException = new InvalidOperationException("boom");

        await h.Vm.LoadTicketHistoriesAsync(new MonthlyTicket { TicketId = 1 });

        Assert.Empty(h.Dialogs.Warnings);
        Assert.False(h.Vm.IsLoadingHistories);
    }

    /// <summary>Row buttons in the DataGrids pass the row item as CommandParameter.</summary>
    private static IEnumerable<(string Name, ICommand Command, object Parameter)> RowCommands(CustomersViewModel vm) => new (string, ICommand, object)[]
    {
        (nameof(vm.DeleteTicketCommand), vm.DeleteTicketCommand, new MonthlyTicket { TicketId = 1 }),
        (nameof(vm.CancelTicketCommand), vm.CancelTicketCommand, new MonthlyTicket { TicketId = 1 }),
        (nameof(vm.ToggleTicketCommand), vm.ToggleTicketCommand, new MonthlyTicket { TicketId = 1 }),
        (nameof(vm.DeleteRowVehicleCommand), vm.DeleteRowVehicleCommand, new CustomerItemViewModel { CustomerId = 1, VehicleId = 1 }),
        (nameof(vm.ToggleBlacklistCommand), vm.ToggleBlacklistCommand, new BlacklistedVehicle { BlacklistId = 1 }),
        (nameof(vm.DeleteBlacklistCommand), vm.DeleteBlacklistCommand, new BlacklistedVehicle { BlacklistId = 1 }),
    };

    [Fact]
    public void Row_buttons_with_CommandParameter_are_locked_without_ParkingConfigure()
    {
        var (vm, _) = Create(TestUsers.Operator());

        Assert.All(RowCommands(vm), c => Assert.False(c.Command.CanExecute(c.Parameter), c.Name));
    }

    [Fact]
    public void Row_buttons_with_CommandParameter_are_enabled_with_ParkingConfigure()
    {
        var (vm, _) = Create(TestUsers.Manager());

        Assert.All(RowCommands(vm), c => Assert.True(c.Command.CanExecute(c.Parameter), c.Name));
    }

    [Fact]
    public void Commands_follow_the_current_user_when_the_user_changes_or_logs_out()
    {
        var h = CreateHarness(TestUsers.Manager());
        Assert.All(RowCommands(h.Vm), c => Assert.True(c.Command.CanExecute(c.Parameter), c.Name));

        h.Context.SetUser(TestUsers.Operator());
        Assert.All(RowCommands(h.Vm), c => Assert.False(c.Command.CanExecute(c.Parameter), c.Name));

        h.Context.SetUser(TestUsers.Manager());
        Assert.All(RowCommands(h.Vm), c => Assert.True(c.Command.CanExecute(c.Parameter), c.Name));

        h.Context.SetUser(null);
        Assert.All(RowCommands(h.Vm), c => Assert.False(c.Command.CanExecute(c.Parameter), c.Name));
    }
}
