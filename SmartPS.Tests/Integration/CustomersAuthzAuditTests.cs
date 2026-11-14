using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Integration;

/// <summary>
/// KAN-33b: Parking.Configure is demanded in CustomersService / TicketsService / VehiclesService (reads and writes),
/// and every write appends an audit row with Details { old, new } in the same transaction as the change.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class CustomersAuthzAuditTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public CustomersAuthzAuditTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    private static string UniquePlate() => "30K" + Random.Shared.Next(10000, 99999);

    private async Task<ServiceProvider> AdminAsync()
    {
        _db.RequireAvailable();
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        return sp;
    }

    private async Task<ServiceProvider> OperatorAsync()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return sp;
    }

    private async Task<(Customer Customer, Vehicle Vehicle)> SeedCustomerWithVehicleAsync(string? plate = null)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        var customer = new Customer { FullName = "Authz " + Guid.NewGuid().ToString("N")[..6], PhoneNumber = "0900000000" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var vehicle = new Vehicle { LicensePlate = plate ?? UniquePlate(), VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (customer, vehicle);
    }

    private async Task<MonthlyTicket> SeedTicketAsync(Customer customer, Vehicle vehicle)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var ticket = new MonthlyTicket
        {
            CustomerId = customer.CustomerId,
            VehicleId = vehicle.VehicleId,
            RegisteredLicensePlate = vehicle.LicensePlate,
            VehicleTypeId = vehicle.VehicleTypeId,
            DurationMonths = 1,
            MonthlyPrice = 100_000m,
            StartDate = now,
            EndDate = now.AddMonths(1),
            TicketCode = "MT-T-" + Guid.NewGuid().ToString("N")[..8],
            Status = MonthlyTicketStatus.Active,
            CreatedAt = now
        };
        db.MonthlyTickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static JsonElement Prop(JsonElement e, string name)
    {
        Assert.True(e.TryGetProperty(name, out var v), $"missing '{name}' in {e.GetRawText()}");
        return v;
    }

    private async Task<(AuditLog Row, JsonElement Details)> SingleRowAsync(long idBefore, string action, string entityType)
    {
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, action);
        var row = Assert.Single(rows);
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal(entityType, row.EntityType);
        Assert.False(string.IsNullOrWhiteSpace(row.EntityId));
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);
        var details = AuditDb.Details(row);
        Assert.True(details.TryGetProperty("old", out _) && details.TryGetProperty("new", out _), row.Details);
        return (row, details);
    }

    // ---------- Permission: every public member is denied without Parking.Configure ----------

    private static readonly IReadOnlyDictionary<string, Func<ServiceProvider, Task>> Operations = new Dictionary<string, Func<ServiceProvider, Task>>
    {
        ["Customers.GetCustomers"] = sp => sp.GetRequiredService<ICustomersService>().GetCustomersAsync(),
        ["Customers.GetHouseholds"] = sp => sp.GetRequiredService<ICustomersService>().GetHouseholdsAsync(),
        ["Customers.GetBlacklist"] = sp => sp.GetRequiredService<ICustomersService>().GetBlacklistAsync(),
        ["Customers.AddHousehold"] = sp => sp.GetRequiredService<ICustomersService>().AddHouseholdAsync(new CreateHouseholdRequest("ZZ-DENY", null, null)),
        ["Customers.DeleteHousehold"] = sp => sp.GetRequiredService<ICustomersService>().DeleteHouseholdAsync(1),
        ["Customers.AddCustomer"] = sp => sp.GetRequiredService<ICustomersService>().AddCustomerAsync(new CreateCustomerRequest("Denied", "0900", null, null, null, null)),
        ["Customers.DeleteCustomer"] = sp => sp.GetRequiredService<ICustomersService>().DeleteCustomerAsync(1),
        ["Customers.AddBlacklist"] = sp => sp.GetRequiredService<ICustomersService>().AddBlacklistAsync("29A12345", "x", null, null),
        ["Customers.ToggleBlacklist"] = sp => sp.GetRequiredService<ICustomersService>().ToggleBlacklistAsync(1),
        ["Customers.DeleteBlacklist"] = sp => sp.GetRequiredService<ICustomersService>().DeleteBlacklistAsync(1),
        ["Tickets.GetTickets"] = sp => sp.GetRequiredService<ITicketsService>().GetTicketsAsync(),
        ["Tickets.GetTicketHistories"] = sp => sp.GetRequiredService<ITicketsService>().GetTicketHistoriesAsync(1),
        ["Tickets.GetPricingRules"] = sp => sp.GetRequiredService<ITicketsService>().GetPricingRulesAsync(),
        ["Tickets.CalculateTicketFee"] = sp => sp.GetRequiredService<ITicketsService>().CalculateTicketFeeAsync(1, 1),
        ["Tickets.IssueTicket"] = sp => sp.GetRequiredService<ITicketsService>().IssueTicketAsync(new IssueTicketRequest(1, 1, 1, PaymentMethod.Cash)),
        ["Tickets.RenewTicket"] = sp => sp.GetRequiredService<ITicketsService>().RenewTicketAsync(new RenewTicketRequest(1, 1, PaymentMethod.Cash)),
        ["Tickets.ChangePlate"] = sp => sp.GetRequiredService<ITicketsService>().ChangePlateAsync(new ChangePlateRequest(1, 2, "x")),
        ["Tickets.ToggleLockTicket"] = sp => sp.GetRequiredService<ITicketsService>().ToggleLockTicketAsync(1, "x"),
        ["Tickets.CancelTicket"] = sp => sp.GetRequiredService<ITicketsService>().CancelTicketAsync(1, "x"),
        ["Vehicles.CheckVehicleLimit"] = sp => sp.GetRequiredService<IVehiclesService>().CheckVehicleLimitAsync(1),
        ["Vehicles.GetVehicles"] = sp => sp.GetRequiredService<IVehiclesService>().GetVehiclesAsync(),
        ["Vehicles.GetVehiclesByCustomer"] = sp => sp.GetRequiredService<IVehiclesService>().GetVehiclesByCustomerAsync(1),
        ["Vehicles.GetVehicleTypes"] = sp => sp.GetRequiredService<IVehiclesService>().GetVehicleTypesAsync(),
        ["Vehicles.AddVehicle"] = sp => sp.GetRequiredService<IVehiclesService>().AddVehicleAsync(new CreateVehicleRequest(1, "29A00001", 1)),
        ["Vehicles.DeleteVehicle"] = sp => sp.GetRequiredService<IVehiclesService>().DeleteVehicleAsync(1),
    };

    public static TheoryData<string> OperationNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Operations.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task Without_ParkingConfigure_every_operation_throws_and_logs_ACCESS_DENIED(string operation)
    {
        using var sp = await OperatorAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Operations[operation](sp));

        Assert.Equal(new[] { Permissions.ParkingConfigure }, ex.RequiredPermissions);
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(new[] { Permissions.ParkingConfigure }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task Denied_write_does_not_change_data()
    {
        var (customer, vehicle) = await SeedCustomerWithVehicleAsync();
        using var sp = await OperatorAsync();

        await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.GetRequiredService<ICustomersService>().DeleteCustomerAsync(customer.CustomerId));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.GetRequiredService<IVehiclesService>().DeleteVehicleAsync(vehicle.VehicleId));

        await using var db = await _db.Factory.CreateDbContextAsync();
        Assert.True(await db.Customers.AnyAsync(c => c.CustomerId == customer.CustomerId));
        Assert.True(await db.Vehicles.AnyAsync(v => v.VehicleId == vehicle.VehicleId && v.IsActive));
    }

    [Fact]
    public async Task Manager_with_ParkingConfigure_is_allowed()
    {
        _db.RequireAvailable();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);

        var result = await sp.GetRequiredService<ICustomersService>().AddHouseholdAsync(new CreateHouseholdRequest("MG-" + Guid.NewGuid().ToString("N")[..6], null, null));

        Assert.True(result.Success, result.Message);
    }

    // ---------- Audit Old/New for each write ----------

    [Fact]
    public async Task AddHousehold_audits_new_only()
    {
        using var sp = await AdminAsync();
        var code = "AH-" + Guid.NewGuid().ToString("N")[..6];
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<ICustomersService>().AddHouseholdAsync(new CreateHouseholdRequest(code, 3, "ghi chu"));

        Assert.True(result.Success, result.Message);
        var (row, d) = await SingleRowAsync(idBefore, AuditActions.HouseholdCreate, "Household");
        Assert.Equal(JsonValueKind.Null, Prop(d, "old").ValueKind);
        Assert.Equal(code, Prop(Prop(d, "new"), "apartmentCode").GetString());
        Assert.Equal(3, Prop(Prop(d, "new"), "maxVehicles").GetInt32());
        await using var db = await _db.Factory.CreateDbContextAsync();
        Assert.Equal(row.EntityId, (await db.Households.SingleAsync(h => h.ApartmentCode == code)).HouseholdId.ToString());
    }

    [Fact]
    public async Task DeleteHousehold_audits_old_with_detached_customers()
    {
        using var sp = await AdminAsync();
        var code = "DH-" + Guid.NewGuid().ToString("N")[..6];
        int householdId;
        int customerId;
        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            var h = new Household { ApartmentCode = code };
            db.Households.Add(h);
            await db.SaveChangesAsync();
            var c = new Customer { FullName = "DH", PhoneNumber = "0900", HouseholdId = h.HouseholdId, Type = CustomerType.Resident };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            householdId = h.HouseholdId;
            customerId = c.CustomerId;
        }

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var result = await sp.GetRequiredService<ICustomersService>().DeleteHouseholdAsync(householdId);

        Assert.True(result.Success, result.Message);
        var (_, d) = await SingleRowAsync(idBefore, AuditActions.HouseholdDelete, "Household");
        Assert.Equal(code, Prop(Prop(d, "old"), "apartmentCode").GetString());
        Assert.Equal(new[] { customerId }, Prop(Prop(d, "old"), "detachedCustomerIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal(JsonValueKind.Null, Prop(d, "new").ValueKind);
    }

    [Fact]
    public async Task AddCustomer_with_initial_vehicle_audits_new_and_writes_data_together()
    {
        using var sp = await AdminAsync();
        var plate = UniquePlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<ICustomersService>().AddCustomerAsync(new CreateCustomerRequest("Audit Customer", "0911", null, null, plate, 1));

        Assert.True(result.Success, result.Message);
        var (_, d) = await SingleRowAsync(idBefore, AuditActions.CustomerCreate, "Customer");
        Assert.Equal(JsonValueKind.Null, Prop(d, "old").ValueKind);
        Assert.Equal("Audit Customer", Prop(Prop(d, "new"), "fullName").GetString());
        Assert.Equal(plate, Prop(Prop(Prop(d, "new"), "initialVehicle"), "licensePlate").GetString());
        await using var db = await _db.Factory.CreateDbContextAsync();
        Assert.True(await db.Vehicles.AnyAsync(v => v.LicensePlate == plate));
    }

    [Fact]
    public async Task DeleteCustomer_audits_old()
    {
        using var sp = await AdminAsync();
        // A customer that owns a vehicle row cannot be deleted at all (Vehicle.OwnerCustomer is Restrict), so seed one without vehicles.
        Customer customer;
        await using (var seed = await _db.Factory.CreateDbContextAsync())
        {
            customer = new Customer { FullName = "Del " + Guid.NewGuid().ToString("N")[..6], PhoneNumber = "0900000001" };
            seed.Customers.Add(customer);
            await seed.SaveChangesAsync();
        }

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<ICustomersService>().DeleteCustomerAsync(customer.CustomerId);

        Assert.True(result.Success, result.Message);
        var (row, d) = await SingleRowAsync(idBefore, AuditActions.CustomerDelete, "Customer");
        Assert.Equal(customer.CustomerId.ToString(), row.EntityId);
        Assert.Equal(customer.FullName, Prop(Prop(d, "old"), "fullName").GetString());
        Assert.Empty(Prop(Prop(d, "old"), "deactivatedVehicleIds").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, Prop(d, "new").ValueKind);
    }

    [Fact]
    public async Task Blacklist_add_toggle_delete_audit_old_and_new()
    {
        using var sp = await AdminAsync();
        var customers = sp.GetRequiredService<ICustomersService>();
        var plate = UniquePlate();

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await customers.AddBlacklistAsync(plate, "no tien", "ghi chu", null)).Success);
        var (addRow, add) = await SingleRowAsync(idBefore, AuditActions.BlacklistAdd, "BlacklistedVehicle");
        Assert.Equal(JsonValueKind.Null, Prop(add, "old").ValueKind);
        Assert.Equal(plate, Prop(Prop(add, "new"), "licensePlate").GetString());
        Assert.True(Prop(Prop(add, "new"), "isActive").GetBoolean());
        var id = int.Parse(addRow.EntityId!);

        idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await customers.ToggleBlacklistAsync(id)).Success);
        var (_, toggle) = await SingleRowAsync(idBefore, AuditActions.BlacklistToggle, "BlacklistedVehicle");
        Assert.True(Prop(Prop(toggle, "old"), "isActive").GetBoolean());
        Assert.False(Prop(Prop(toggle, "new"), "isActive").GetBoolean());

        // Re-adding a disabled plate reactivates it: old snapshot present.
        idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await customers.AddBlacklistAsync(plate, "lai", null, null)).Success);
        var (_, readd) = await SingleRowAsync(idBefore, AuditActions.BlacklistAdd, "BlacklistedVehicle");
        Assert.False(Prop(Prop(readd, "old"), "isActive").GetBoolean());
        Assert.True(Prop(Prop(readd, "new"), "isActive").GetBoolean());
        Assert.Equal("lai", Prop(Prop(readd, "new"), "reason").GetString());

        idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await customers.DeleteBlacklistAsync(id)).Success);
        var (_, del) = await SingleRowAsync(idBefore, AuditActions.BlacklistDelete, "BlacklistedVehicle");
        Assert.True(Prop(Prop(del, "old"), "isActive").GetBoolean());
        Assert.False(Prop(Prop(del, "new"), "isActive").GetBoolean());
    }

    [Fact]
    public async Task Vehicle_add_and_delete_audit_old_and_new()
    {
        using var sp = await AdminAsync();
        var vehicles = sp.GetRequiredService<IVehiclesService>();
        var customer = (await SeedCustomerWithVehicleAsync()).Customer;
        var plate = UniquePlate();

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await vehicles.AddVehicleAsync(new CreateVehicleRequest(customer.CustomerId, plate, 1))).Success);
        var (row, add) = await SingleRowAsync(idBefore, AuditActions.VehicleCreate, "Vehicle");
        Assert.Equal(JsonValueKind.Null, Prop(add, "old").ValueKind);
        Assert.Equal(plate, Prop(Prop(add, "new"), "licensePlate").GetString());

        idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await vehicles.DeleteVehicleAsync(int.Parse(row.EntityId!))).Success);
        var (_, del) = await SingleRowAsync(idBefore, AuditActions.VehicleDelete, "Vehicle");
        Assert.Equal(plate, Prop(Prop(del, "old"), "licensePlate").GetString());
        Assert.True(Prop(Prop(del, "new"), "deleted").GetBoolean());
    }

    [Fact]
    public async Task Ticket_issue_audits_new_and_history_is_in_the_same_transaction()
    {
        using var sp = await AdminAsync();
        var (customer, vehicle) = await SeedCustomerWithVehicleAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<ITicketsService>().IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));

        Assert.True(result.Success, result.Message);
        var (row, d) = await SingleRowAsync(idBefore, AuditActions.TicketIssue, "MonthlyTicket");
        Assert.Equal(JsonValueKind.Null, Prop(d, "old").ValueKind);
        Assert.Equal(result.Message, Prop(Prop(d, "new"), "ticketCode").GetString());
        Assert.Equal("Active", Prop(Prop(d, "new"), "status").GetString());
        Assert.Equal("Cash", Prop(Prop(d, "context"), "paymentMethod").GetString());
        await using var db = await _db.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.MonthlyTicketHistories.CountAsync(h => h.TicketId == int.Parse(row.EntityId!)));
    }

    [Fact]
    public async Task Ticket_renew_audits_old_and_new_end_date()
    {
        using var sp = await AdminAsync();
        var (customer, vehicle) = await SeedCustomerWithVehicleAsync();
        var ticket = await SeedTicketAsync(customer, vehicle);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<ITicketsService>().RenewTicketAsync(new RenewTicketRequest(ticket.TicketId, 2, PaymentMethod.Cash));

        Assert.True(result.Success, result.Message);
        var (_, d) = await SingleRowAsync(idBefore, AuditActions.TicketRenew, "MonthlyTicket");
        var oldEnd = Prop(Prop(d, "old"), "endDate").GetDateTime();
        var newEnd = Prop(Prop(d, "new"), "endDate").GetDateTime();
        Assert.True(newEnd > oldEnd);
        Assert.Equal(1, Prop(Prop(d, "old"), "durationMonths").GetInt32());
        Assert.Equal(3, Prop(Prop(d, "new"), "durationMonths").GetInt32());
        Assert.Equal(2, Prop(Prop(d, "context"), "addedMonths").GetInt32());
    }

    [Fact]
    public async Task Ticket_change_plate_audits_old_and_new_plate()
    {
        using var sp = await AdminAsync();
        var (customer, vehicle) = await SeedCustomerWithVehicleAsync();
        var ticket = await SeedTicketAsync(customer, vehicle);
        Vehicle other;
        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            other = new Vehicle { LicensePlate = UniquePlate(), VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
            db.Vehicles.Add(other);
            await db.SaveChangesAsync();
        }

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var result = await sp.GetRequiredService<ITicketsService>().ChangePlateAsync(new ChangePlateRequest(ticket.TicketId, other.VehicleId, "doi xe"));

        Assert.True(result.Success, result.Message);
        var (_, d) = await SingleRowAsync(idBefore, AuditActions.TicketChangePlate, "MonthlyTicket");
        Assert.Equal(vehicle.LicensePlate, Prop(Prop(d, "old"), "registeredLicensePlate").GetString());
        Assert.Equal(other.LicensePlate, Prop(Prop(d, "new"), "registeredLicensePlate").GetString());
        Assert.Equal("doi xe", Prop(Prop(d, "context"), "reason").GetString());
    }

    [Fact]
    public async Task Ticket_lock_toggle_and_cancel_audit_status_change()
    {
        using var sp = await AdminAsync();
        var tickets = sp.GetRequiredService<ITicketsService>();
        var (customer, vehicle) = await SeedCustomerWithVehicleAsync();
        var ticket = await SeedTicketAsync(customer, vehicle);

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await tickets.ToggleLockTicketAsync(ticket.TicketId, "kiem tra")).Success);
        var (_, lockRow) = await SingleRowAsync(idBefore, AuditActions.TicketLockToggle, "MonthlyTicket");
        Assert.Equal("Active", Prop(Prop(lockRow, "old"), "status").GetString());
        Assert.Equal("Suspended", Prop(Prop(lockRow, "new"), "status").GetString());

        idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        Assert.True((await tickets.CancelTicketAsync(ticket.TicketId, "huy")).Success);
        var (_, cancel) = await SingleRowAsync(idBefore, AuditActions.TicketCancel, "MonthlyTicket");
        Assert.Equal("Suspended", Prop(Prop(cancel, "old"), "status").GetString());
        Assert.Equal("Cancelled", Prop(Prop(cancel, "new"), "status").GetString());
    }

    [Fact]
    public async Task Failed_validation_writes_no_audit_row()
    {
        using var sp = await AdminAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<ICustomersService>().AddHouseholdAsync(new CreateHouseholdRequest("  ", null, null));

        Assert.False(result.Success);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_data_change()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.HouseholdCreate));
        await sp.LoginAdminAsync();
        var code = "RB-" + Guid.NewGuid().ToString("N")[..6];

        await Assert.ThrowsAnyAsync<Exception>(() => sp.GetRequiredService<ICustomersService>().AddHouseholdAsync(new CreateHouseholdRequest(code, null, null)));

        await using var db = await _db.Factory.CreateDbContextAsync();
        Assert.False(await db.Households.AnyAsync(h => h.ApartmentCode == code));
    }
}
