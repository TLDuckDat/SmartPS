using System.Windows.Input;
using SmartPS.Models.Parking;
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
        public Task<IReadOnlyList<MonthlyTicket>> GetTicketsAsync(string? keyword = null, bool onlyExpiringSoon = false, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MonthlyTicket>>(Array.Empty<MonthlyTicket>());
        public Task<IReadOnlyList<MonthlyTicketHistory>> GetTicketHistoriesAsync(int ticketId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MonthlyTicketHistory>>(Array.Empty<MonthlyTicketHistory>());
        public Task<IReadOnlyList<PricingRule>> GetPricingRulesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PricingRule>>(Array.Empty<PricingRule>());
        public Task<decimal?> CalculateTicketFeeAsync(int vehicleTypeId, int durationMonths, CancellationToken ct = default) => Task.FromResult<decimal?>(null);
        public Task<OperationResult> IssueTicketAsync(IssueTicketRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> RenewTicketAsync(RenewTicketRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> ChangePlateAsync(ChangePlateRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> ToggleLockTicketAsync(int ticketId, string reason, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> CancelTicketAsync(int ticketId, string reason, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FakeVehicles : IVehiclesService
    {
        public Task<bool> CheckVehicleLimitAsync(int householdId, int? excludeVehicleId = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Vehicle>>(Array.Empty<Vehicle>());
        public Task<IReadOnlyList<Vehicle>> GetVehiclesByCustomerAsync(int customerId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Vehicle>>(Array.Empty<Vehicle>());
        public Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VehicleType>>(Array.Empty<VehicleType>());
        public Task<OperationResult> AddVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public Task<OperationResult> DeleteVehicleAsync(int vehicleId, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());
        public string NormalizePlate(string licensePlate) => licensePlate;
    }

    private static (CustomersViewModel Vm, FakeCustomers Customers) Create(Models.Auth.User? user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var customers = new FakeCustomers();
        var vm = new CustomersViewModel(customers, new FakeTickets(), new FakeVehicles(), new FakeDialogService(), new PermissionService(context));
        return (vm, customers);
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
}
