using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Integration;

/// <summary>AC-10 / R8 / R18 / A4: reads need Customer.View (throw), writes need Customer.Manage or Blacklist.Manage (denied result + ACCESS_DENIED).</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class CustomerPermissionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public CustomerPermissionTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> LoginAsync(string roleName)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, roleName);
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        return (sp, user.UserId);
    }

    private async Task AssertDeniedAsync(Func<Task<OperationResult>> call, string permission, int userId)
    {
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await call();

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal(OperationError.PermissionDenied, result.Error);
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(userId, denied.UserId);
        Assert.Equal(new[] { permission }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task AC10_operator_can_read_but_every_write_is_denied_and_audited()
    {
        _db.RequireAvailable();
        var (sp, opId) = await LoginAsync("Operator");
        using var spScope = sp;
        var customers = sp.GetRequiredService<ICustomerService>();
        var tickets = sp.GetRequiredService<IMonthlyTicketService>();
        var blacklist = sp.GetRequiredService<IBlacklistService>();
        var moto = await _data.MotorbikeTypeIdAsync();
        var s = await _data.CreateSubscriberAsync(moto, isResident: true);
        var planId = await _data.PlanIdAsync("Gói Xe Máy 1 Tháng");
        var entryId = await _data.AddBlacklistAsync(ResidentVisitorData.UniqueNormalizedPlate());
        var customersBefore = await _data.CountAsync("SELECT count(*) FROM \"Customers\"");

        // Reads work with Customer.View
        var page = await customers.SearchAsync(new CustomerQuery());
        Assert.True(page.TotalCount > 0);
        Assert.NotNull(await customers.GetDetailsAsync(s.CustomerId));
        await customers.GetSummaryAsync();
        Assert.NotEmpty(await blacklist.GetEntriesAsync(new BlacklistQuery()));

        var request = new CustomerUpsertRequest { FullName = "Bị chặn", PhoneNumber = ResidentVisitorData.UniquePhone() };
        await AssertDeniedAsync(async () => await customers.CreateCustomerAsync(request), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(() => customers.UpdateCustomerAsync(s.CustomerId, request), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(() => customers.SetCustomerActiveAsync(s.CustomerId, false), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(async () => await customers.AddVehicleAsync(s.CustomerId, new NewVehicle(ResidentVisitorData.UniqueNormalizedPlate(), moto)), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(() => customers.RemoveVehicleAsync(s.CustomerVehicleId), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(async () => await tickets.CreateTicketAsync(new CreateTicketRequest(s.CustomerId, s.Plate, planId)), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(() => tickets.RenewTicketAsync(s.TicketId!.Value, planId), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(() => tickets.SuspendTicketAsync(s.TicketId!.Value, "x"), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(() => tickets.ResumeTicketAsync(s.TicketId!.Value), Permissions.CustomerManage, opId);
        await AssertDeniedAsync(async () => await blacklist.AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "lý do"), Permissions.BlacklistManage, opId);
        await AssertDeniedAsync(() => blacklist.RemoveAsync(entryId, "lý do"), Permissions.BlacklistManage, opId);

        // Nothing changed
        Assert.Equal(customersBefore, await _data.CountAsync("SELECT count(*) FROM \"Customers\""));
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerVehicleId\" = @id AND \"IsActive\"", ("id", s.CustomerVehicleId)));
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"BlacklistEntries\" WHERE \"BlacklistEntryId\" = @id AND \"IsActive\"", ("id", entryId)));
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketId\" = @id AND \"Status\" = 0", ("id", s.TicketId!.Value)));
    }

    [Fact]
    public async Task Role_without_Customer_View_cannot_read()
    {
        _db.RequireAvailable();
        var roleName = TestUsers.UniqueName("noview");
        await TestUsers.CreateRoleAsync(_db.Factory, roleName, Permissions.ParkingView);
        var (sp, userId) = await LoginAsync(roleName);
        using var spScope = sp;
        var customers = sp.GetRequiredService<ICustomerService>();
        var blacklist = sp.GetRequiredService<IBlacklistService>();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => customers.SearchAsync(new CustomerQuery()));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => customers.GetSummaryAsync());
        await Assert.ThrowsAsync<PermissionDeniedException>(() => customers.GetDetailsAsync(1));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => blacklist.GetEntriesAsync(new BlacklistQuery()));

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(userId, r.UserId);
            Assert.Equal(new[] { Permissions.CustomerView }, AuditDb.StringArray(AuditDb.Details(r), "requiredPermissions"));
        });

        // Reference data stays readable
        Assert.NotEmpty(await customers.GetVehicleTypesAsync());
        Assert.NotEmpty(await sp.GetRequiredService<IMonthlyTicketService>().GetActivePlansAsync());
    }

    [Fact]
    public async Task Customer_manager_without_Blacklist_Manage_cannot_change_the_blacklist()
    {
        _db.RequireAvailable();
        var roleName = TestUsers.UniqueName("clerk");
        await TestUsers.CreateRoleAsync(_db.Factory, roleName, Permissions.CustomerView, Permissions.CustomerManage);
        var (sp, userId) = await LoginAsync(roleName);
        using var spScope = sp;

        var created = await sp.GetRequiredService<ICustomerService>().CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Khách của thư ký",
            PhoneNumber = ResidentVisitorData.UniquePhone()
        });
        Assert.True(created.Success, created.Message);

        await AssertDeniedAsync(async () => await sp.GetRequiredService<IBlacklistService>().AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "x"),
            Permissions.BlacklistManage, userId);
    }

    [Fact]
    public async Task Manager_can_do_everything()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Manager");
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();

        var created = await sp.GetRequiredService<ICustomerService>().CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Khách quản lý",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            Vehicles = new[] { new NewVehicle(ResidentVisitorData.UniqueNormalizedPlate(), moto) }
        });
        var blacklisted = await sp.GetRequiredService<IBlacklistService>().AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "x");
        var search = await sp.GetRequiredService<ICustomerService>().SearchAsync(new CustomerQuery { SearchText = "Khách quản lý" });

        Assert.True(created.Success, created.Message);
        Assert.True(blacklisted.Success, blacklisted.Message);
        Assert.Contains(search.Items, i => i.CustomerId == created.Value);
        Assert.Equal(CustomerType.Regular, search.Items.Single(i => i.CustomerId == created.Value).Type);
    }

    [Fact]
    public async Task Admin_has_the_new_permissions_through_the_bypass()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();

        foreach (var permission in TestUsers.CustomerPermissions)
        {
            Assert.True(sp.Perms().HasPermission(permission), permission);
        }
    }
}
