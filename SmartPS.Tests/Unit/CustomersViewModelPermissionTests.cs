using SmartPS.Models.Auth;
using SmartPS.ViewModels.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>AC-10 (UI part): Operator sees the Customers screen read-only; Manager can use every write command.</summary>
public class CustomersViewModelPermissionTests
{
    private static (CustomersViewModel Vm, FakeCustomerService Customers, FakeMonthlyTicketService Tickets, FakeBlacklistService Blacklist) Create(User user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var customers = new FakeCustomerService();
        var tickets = new FakeMonthlyTicketService();
        var blacklist = new FakeBlacklistService();
        var vm = new CustomersViewModel(customers, tickets, blacklist, new PermissionService(context), new FakeDialogService(), new FakeLocalizationService());
        return (vm, customers, tickets, blacklist);
    }

    [Fact]
    public void Constructor_does_not_load_data()
    {
        var (_, customers, tickets, blacklist) = Create(TestUsers.Manager());

        Assert.Empty(customers.Calls);
        Assert.Empty(tickets.Calls);
        Assert.Empty(blacklist.Calls);
    }

    [Fact]
    public async Task Operator_is_read_only()
    {
        var (vm, customers, _, blacklist) = Create(TestUsers.Operator());
        await vm.LoadDataAsync();

        Assert.False(vm.CanManageCustomers);
        Assert.False(vm.CanManageBlacklist);
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.NewCustomerCommand.CanExecute(null));
        Assert.False(vm.Editor.SaveCommand.CanExecute(null));

        vm.Blacklist.NewPlate = "29A-111.22";
        vm.Blacklist.NewReason = "Lý do";
        Assert.False(vm.Blacklist.AddCommand.CanExecute(null));

        // Reading still works for Customer.View holders.
        Assert.Contains(nameof(FakeCustomerService.SearchAsync), customers.Calls);
        Assert.NotEmpty(vm.Customers);
        Assert.DoesNotContain(customers.Calls, c => c is nameof(FakeCustomerService.CreateCustomerAsync) or nameof(FakeCustomerService.UpdateCustomerAsync));
        Assert.DoesNotContain(nameof(FakeBlacklistService.AddAsync), blacklist.Calls);
    }

    [Fact]
    public async Task Operator_cannot_remove_a_blacklist_entry_even_with_a_reason()
    {
        var (vm, _, _, _) = Create(TestUsers.Operator());
        await vm.LoadDataAsync();
        await vm.Blacklist.LoadAsync();

        vm.Blacklist.SelectedEntry = vm.Blacklist.Entries.FirstOrDefault();
        vm.Blacklist.RemoveReason = "Đã thanh toán";

        Assert.False(vm.Blacklist.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Manager_can_use_every_write_command()
    {
        var (vm, _, _, _) = Create(TestUsers.Manager());
        await vm.LoadDataAsync();

        Assert.True(vm.CanManageCustomers);
        Assert.True(vm.CanManageBlacklist);
        Assert.False(vm.IsReadOnly);
        Assert.True(vm.NewCustomerCommand.CanExecute(null));

        vm.Blacklist.NewPlate = "29A-111.22";
        vm.Blacklist.NewReason = "Lý do";
        Assert.True(vm.Blacklist.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task Manager_remove_needs_an_active_selection_and_a_reason()
    {
        var (vm, _, _, _) = Create(TestUsers.Manager());
        await vm.LoadDataAsync();
        await vm.Blacklist.LoadAsync();

        vm.Blacklist.SelectedEntry = vm.Blacklist.Entries.First();
        vm.Blacklist.RemoveReason = "   ";
        Assert.False(vm.Blacklist.RemoveCommand.CanExecute(null));

        vm.Blacklist.RemoveReason = "Đã thanh toán";
        Assert.True(vm.Blacklist.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void Customer_manager_without_blacklist_permission_cannot_add_blacklist()
    {
        var user = TestUsers.Build("Clerk", Permissions.CustomerView, Permissions.CustomerManage);
        var (vm, _, _, _) = Create(user);

        Assert.True(vm.CanManageCustomers);
        Assert.False(vm.CanManageBlacklist);
        vm.Blacklist.NewPlate = "29A-111.22";
        vm.Blacklist.NewReason = "Lý do";
        Assert.False(vm.Blacklist.AddCommand.CanExecute(null));
    }
}
