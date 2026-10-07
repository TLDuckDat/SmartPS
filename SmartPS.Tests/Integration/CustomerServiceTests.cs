using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Integration;

/// <summary>AC-7, AC-8, R1, R4, R5, R6, A8, ADDENDUM A (Create purchase), m7 (PII in audit).</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class CustomerServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string Moto3MonthPlan = "Gói Xe Máy 3 Tháng (Tiết kiệm)";

    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public CustomerServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> ManagerAsync()
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        return (sp, manager.UserId);
    }

    private static ICustomerService Customers(IServiceProvider sp) => sp.GetRequiredService<ICustomerService>();

    private static IMonthlyTicketService Tickets(IServiceProvider sp) => sp.GetRequiredService<IMonthlyTicketService>();

    private static string Pretty(string normalizedPlate) => normalizedPlate[..3] + "-" + normalizedPlate[3..6] + "." + normalizedPlate[6..];

    private Task<long> CountAsync(string sql, params (string, object?)[] p) => _data.CountAsync(sql, p);

    private async Task<int> CreateResidentAsync(IServiceProvider sp, string apartment, params NewVehicle[] vehicles)
    {
        var result = await Customers(sp).CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Cư dân " + apartment,
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            IsResident = true,
            ApartmentCode = apartment,
            Building = "A",
            Vehicles = vehicles
        });
        Assert.True(result.Success, $"{result.Error}: {result.Message}");
        return result.Value;
    }

    [Fact]
    public async Task AC7_create_resident_with_two_vehicles_and_a_3_month_ticket()
    {
        _db.RequireAvailable();
        var (sp, managerId) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var car = await _data.CarTypeIdAsync();
        var motoPlate = ResidentVisitorData.UniqueNormalizedPlate();
        var carPlate = ResidentVisitorData.UniqueNormalizedPlate();
        var phone = ResidentVisitorData.UniquePhone();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var created = await Customers(sp).CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Phan Thị AC Bảy",
            PhoneNumber = phone[..4] + "." + phone[4..7] + "." + phone[7..],
            IsResident = true,
            ApartmentCode = "a-1599",
            Building = "A",
            Type = CustomerType.Regular,
            Vehicles = new[] { new NewVehicle(Pretty(motoPlate).ToLowerInvariant(), moto), new NewVehicle(Pretty(carPlate), car) }
        });

        Assert.True(created.Success, $"{created.Error}: {created.Message}");
        var customerId = created.Value;
        await using (var ctx = _db.CreateContext())
        {
            var customer = await ctx.Customers.AsNoTracking().SingleAsync(c => c.CustomerId == customerId);
            Assert.True(customer.IsResident);
            Assert.Equal(CustomerType.Resident, customer.Type);
            Assert.Equal("A-1599", customer.ApartmentCode);
            Assert.Equal(phone, customer.PhoneNumber);
            Assert.True(customer.IsActive);
            Assert.Contains(customer.DefaultLicensePlate, new[] { motoPlate, carPlate });

            var vehicles = await ctx.CustomerVehicles.AsNoTracking().Where(v => v.CustomerId == customerId).ToListAsync();
            Assert.Equal(2, vehicles.Count);
            Assert.All(vehicles, v => Assert.True(v.IsActive));
            Assert.Equal(new[] { carPlate, motoPlate }.OrderBy(x => x, StringComparer.Ordinal), vehicles.Select(v => v.LicensePlate).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(moto, vehicles.Single(v => v.LicensePlate == motoPlate).VehicleTypeId);
            Assert.Equal(customer.VehicleTypeId, vehicles.Single(v => v.LicensePlate == customer.DefaultLicensePlate).VehicleTypeId);
        }

        // Ticket from the 3-month motorbike plan
        var planId = await _data.PlanIdAsync(Moto3MonthPlan);
        var ticket = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(customerId, Pretty(motoPlate), planId));

        Assert.True(ticket.Success, $"{ticket.Error}: {ticket.Message}");
        var todayVn = TicketDates.TodayVn(DateTime.UtcNow);
        var expectedStart = AuditTime.VietnamDateStartUtc(todayVn);
        var expectedEnd = AuditTime.VietnamDateStartUtc(todayVn.AddMonths(3));
        long purchaseId;
        await using (var ctx = _db.CreateContext())
        {
            var plan = await ctx.MonthlyTicketPlans.AsNoTracking().SingleAsync(p => p.PlanId == planId);
            var t = await ctx.MonthlyTickets.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Value);
            Assert.Equal(customerId, t.CustomerId);
            Assert.Equal(motoPlate, t.RegisteredLicensePlate);
            Assert.Equal(planId, t.PlanId);
            Assert.Equal(moto, t.VehicleTypeId);
            Assert.Equal(expectedStart, t.StartDate);
            Assert.Equal(expectedEnd, t.EndDate);
            Assert.Equal(plan.TotalPrice, t.MonthlyPrice);
            Assert.Equal(MonthlyTicketStatus.Active, t.Status);
            Assert.Matches(@"^MT-\d{6}-\d{4}$", t.TicketCode);
            Assert.StartsWith($"MT-{todayVn:yyyyMM}-", t.TicketCode, StringComparison.Ordinal);

            // ADDENDUM A: exactly one Create purchase for the term
            var purchase = Assert.Single(await ctx.MonthlyTicketPurchases.AsNoTracking().Where(p => p.TicketId == t.TicketId).ToListAsync());
            Assert.Equal(TicketPurchaseKind.Create, purchase.Kind);
            Assert.Equal(plan.TotalPrice, purchase.Price);
            Assert.Equal(planId, purchase.PlanId);
            Assert.Equal(t.StartDate, purchase.PeriodStartUtc);
            Assert.Equal(t.EndDate, purchase.PeriodEndUtc);
            Assert.Equal(managerId, purchase.CreatedByUserId);
            Assert.True(purchase.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-5));
            purchaseId = purchase.MonthlyTicketPurchaseId;
        }

        // Audit: 1 CUSTOMER_CREATE, 2 CUSTOMER_VEHICLE_ADD, 1 TICKET_CREATE
        var create = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.CustomerCreate));
        Assert.Equal("Customer", create.EntityType);
        Assert.Equal(customerId.ToString(), create.EntityId);
        Assert.Equal(managerId, create.UserId);
        var adds = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.CustomerVehicleAdd);
        Assert.Equal(2, adds.Count);
        Assert.All(adds, r =>
        {
            Assert.Equal("CustomerVehicle", r.EntityType);
            Assert.Equal(customerId, AuditDb.Details(r).GetProperty("customerId").GetInt32());
        });
        Assert.Equal(new[] { carPlate, motoPlate }.OrderBy(x => x, StringComparer.Ordinal),
            adds.Select(r => AuditDb.Details(r).GetProperty("licensePlate").GetString()).OrderBy(x => x, StringComparer.Ordinal));
        var ticketRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.TicketCreate));
        Assert.Equal("MonthlyTicket", ticketRow.EntityType);
        Assert.Equal(ticket.Value.ToString(), ticketRow.EntityId);
        AuditDb.HasKeys(ticketRow, "ticketCode", "customerId", "licensePlate", "planId", "startDate", "endDate", "price", "purchaseId");
        Assert.Equal(purchaseId, AuditDb.Details(ticketRow).GetProperty("purchaseId").GetInt64());
    }

    [Fact]
    public async Task M7_customer_audit_rows_hold_no_raw_phone_identity_card_or_email()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var phone = ResidentVisitorData.UniquePhone();
        const string identityCard = "079188123456";
        const string email = "pii.owner@example.com";
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var created = await Customers(sp).CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Khách PII",
            PhoneNumber = phone,
            Email = email,
            IdentityCard = identityCard,
            IsResident = false,
            Notes = "ghi chú bí mật"
        });
        Assert.True(created.Success, created.Message);

        var create = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.CustomerCreate));
        AuditDb.HasKeys(create, "fullName", "phoneNumber", "isResident", "apartmentCode", "building", "type", "hasEmail", "hasIdentityCard");
        var details = AuditDb.Details(create);
        Assert.Equal("*******" + phone[^3..], details.GetProperty("phoneNumber").GetString());
        Assert.True(details.GetProperty("hasEmail").GetBoolean());
        Assert.True(details.GetProperty("hasIdentityCard").GetBoolean());
        Assert.DoesNotContain(phone, create.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(identityCard, create.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(email, create.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("ghi chú bí mật", create.Details, StringComparison.Ordinal);

        // Update email + identity card
        var idBeforeUpdate = await AuditDb.MaxIdAsync(_db.Factory);
        var updated = await Customers(sp).UpdateCustomerAsync(created.Value, new CustomerUpsertRequest
        {
            FullName = "Khách PII",
            PhoneNumber = phone,
            Email = "new.owner@example.com",
            IdentityCard = "079188999999",
            IsResident = false,
            Notes = "ghi chú bí mật"
        });
        Assert.True(updated.Success, updated.Message);

        var update = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBeforeUpdate, AuditActions.CustomerUpdate));
        Assert.Equal("Customer", update.EntityType);
        AuditDb.HasKeys(update, "before", "after", "emailChanged", "identityCardChanged", "notesChanged");
        var u = AuditDb.Details(update);
        Assert.True(u.GetProperty("emailChanged").GetBoolean());
        Assert.True(u.GetProperty("identityCardChanged").GetBoolean());
        Assert.False(u.GetProperty("notesChanged").GetBoolean());
        Assert.Equal("*******" + phone[^3..], u.GetProperty("after").GetProperty("phoneNumber").GetString());
        foreach (var secret in new[] { phone, identityCard, "079188999999", email, "new.owner@example.com", "ghi chú bí mật" })
        {
            Assert.DoesNotContain(secret, update.Details, StringComparison.Ordinal);
        }

        // Nothing written by these operations anywhere in the audit contains the identity card
        Assert.All(await AuditDb.RowsAfterAsync(_db.Factory, idBefore), r => Assert.DoesNotContain(identityCard, r.Details, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC8_adding_a_plate_owned_by_another_customer_fails_and_changes_nothing()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var owner = await CreateResidentAsync(sp, "E-0801", new NewVehicle(plate, moto));
        var other = await CreateResidentAsync(sp, "E-0802");

        var vehiclesBefore = await CountAsync("SELECT count(*) FROM \"CustomerVehicles\"");
        var customersBefore = await CountAsync("SELECT count(*) FROM \"Customers\"");
        var auditBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var add = await Customers(sp).AddVehicleAsync(other, new NewVehicle(Pretty(plate).ToLowerInvariant(), moto));
        var create = await Customers(sp).CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Khách trùng biển",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            Vehicles = new[] { new NewVehicle(plate, moto) }
        });

        Assert.False(add.Success);
        Assert.Equal(OperationError.PlateOwnedByOtherCustomer, add.Error);
        Assert.False(create.Success);
        Assert.Equal(OperationError.PlateOwnedByOtherCustomer, create.Error);
        Assert.Equal(vehiclesBefore, await CountAsync("SELECT count(*) FROM \"CustomerVehicles\""));
        Assert.Equal(customersBefore, await CountAsync("SELECT count(*) FROM \"Customers\""));
        Assert.Equal(auditBefore, await AuditDb.MaxIdAsync(_db.Factory));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"CustomerVehicles\" WHERE \"LicensePlate\" = @p AND \"CustomerId\" = @c AND \"IsActive\"", ("p", plate), ("c", owner)));
    }

    [Fact]
    public async Task Adding_a_plate_already_on_the_same_customer_is_reported()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var owner = await CreateResidentAsync(sp, "E-0803", new NewVehicle(plate, moto));

        var again = await Customers(sp).AddVehicleAsync(owner, new NewVehicle(Pretty(plate), moto));
        var invalid = await Customers(sp).AddVehicleAsync(owner, new NewVehicle("AB", moto));
        var missing = await Customers(sp).AddVehicleAsync(int.MaxValue, new NewVehicle(ResidentVisitorData.UniqueNormalizedPlate(), moto));

        Assert.Equal(OperationError.PlateAlreadyOnCustomer, again.Error);
        Assert.Contains(invalid.Error, new[] { OperationError.PlateInvalid, OperationError.Validation });
        Assert.Equal(OperationError.NotFound, missing.Error);
    }

    [Fact]
    public async Task R6_add_vehicle_succeeds_with_audit_and_keeps_the_mirror()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var customerId = await CreateResidentAsync(sp, "E-0804");
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var added = await Customers(sp).AddVehicleAsync(customerId, new NewVehicle(Pretty(plate), moto));

        Assert.True(added.Success, added.Message);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.CustomerVehicleAdd));
        Assert.Equal(added.Value.ToString(), row.EntityId);
        AuditDb.HasKeys(row, "customerId", "licensePlate", "vehicleTypeId");
        Assert.Equal(plate, AuditDb.Details(row).GetProperty("licensePlate").GetString());
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"Customers\" WHERE \"CustomerId\" = @c AND \"DefaultLicensePlate\" = @p AND \"VehicleTypeId\" = @vt",
            ("c", customerId), ("p", plate), ("vt", moto)));
    }

    [Fact]
    public async Task R5_validation_failures_insert_nothing_and_write_no_audit()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var customersBefore = await CountAsync("SELECT count(*) FROM \"Customers\"");
        var auditBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var badPhone = await Customers(sp).CreateCustomerAsync(new CustomerUpsertRequest { FullName = "Sai SĐT", PhoneNumber = "012345678" });
        var noApartment = await Customers(sp).CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Cư dân thiếu căn hộ",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            IsResident = true
        });

        Assert.False(badPhone.Success);
        Assert.Equal(OperationError.Validation, badPhone.Error);
        Assert.Contains(CustomerValidationError.PhoneInvalid, badPhone.ValidationErrors);
        Assert.False(noApartment.Success);
        Assert.Equal(OperationError.Validation, noApartment.Error);
        Assert.Contains(CustomerValidationError.ApartmentRequired, noApartment.ValidationErrors);
        Assert.Equal(customersBefore, await CountAsync("SELECT count(*) FROM \"Customers\""));
        Assert.Equal(auditBefore, await AuditDb.MaxIdAsync(_db.Factory));
    }

    [Fact]
    public async Task R5_lock_and_unlock_write_customer_update_with_isActive_before_and_after()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var customerId = await CreateResidentAsync(sp, "E-0805");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var locked = await Customers(sp).SetCustomerActiveAsync(customerId, false);
        Assert.True(locked.Success, locked.Message);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"Customers\" WHERE \"CustomerId\" = @c AND \"IsActive\"", ("c", customerId)));
        var unlocked = await Customers(sp).SetCustomerActiveAsync(customerId, true);
        Assert.True(unlocked.Success, unlocked.Message);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.CustomerUpdate);
        Assert.Equal(2, rows.Count);
        var lockDetails = AuditDb.Details(rows[0]);
        Assert.True(lockDetails.GetProperty("before").GetProperty("isActive").GetBoolean());
        Assert.False(lockDetails.GetProperty("after").GetProperty("isActive").GetBoolean());
        Assert.False(lockDetails.GetProperty("emailChanged").GetBoolean());
        Assert.False(lockDetails.GetProperty("identityCardChanged").GetBoolean());
        Assert.False(lockDetails.GetProperty("notesChanged").GetBoolean());
        var unlockDetails = AuditDb.Details(rows[1]);
        Assert.False(unlockDetails.GetProperty("before").GetProperty("isActive").GetBoolean());
        Assert.True(unlockDetails.GetProperty("after").GetProperty("isActive").GetBoolean());

        Assert.Equal(OperationError.NotFound, (await Customers(sp).SetCustomerActiveAsync(int.MaxValue, false)).Error);
    }

    [Fact]
    public async Task Update_switching_to_non_resident_forces_regular_type()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var customerId = await CreateResidentAsync(sp, "E-0806");

        var result = await Customers(sp).UpdateCustomerAsync(customerId, new CustomerUpsertRequest
        {
            FullName = "Không còn ở",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            IsResident = false,
            Type = CustomerType.Resident
        });

        Assert.True(result.Success, result.Message);
        await using var ctx = _db.CreateContext();
        var c = await ctx.Customers.AsNoTracking().SingleAsync(x => x.CustomerId == customerId);
        Assert.False(c.IsResident);
        Assert.Equal(CustomerType.Regular, c.Type);
        Assert.Equal(OperationError.NotFound, (await Customers(sp).UpdateCustomerAsync(int.MaxValue, new CustomerUpsertRequest
        {
            FullName = "x", PhoneNumber = ResidentVisitorData.UniquePhone()
        })).Error);
    }

    [Fact]
    public async Task R4_search_by_name_phone_plate_and_apartment_with_filters_and_ticket_status()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var svc = Customers(sp);

        var byName = await svc.SearchAsync(new CustomerQuery { SearchText = "Văn Hùng" });
        var byPhone = await svc.SearchAsync(new CustomerQuery { SearchText = "0988123456" });
        var byPlate = await svc.SearchAsync(new CustomerQuery { SearchText = "51F123" });
        var byApartment = await svc.SearchAsync(new CustomerQuery { SearchText = "A-1205" });

        foreach (var page in new[] { byName, byPhone, byPlate, byApartment })
        {
            var hung = Assert.Single(page.Items, i => i.PhoneNumber == "0988123456");
            Assert.True(hung.IsResident);
            Assert.Equal("A-1205", hung.ApartmentCode);
            Assert.Contains("51F12345", hung.LicensePlates);
            Assert.Contains("59T112345", hung.LicensePlates);
        }

        var residents = await svc.SearchAsync(new CustomerQuery { Filter = CustomerListFilter.Residents, PageSize = 200 });
        var nonResidents = await svc.SearchAsync(new CustomerQuery { Filter = CustomerListFilter.NonResidents, PageSize = 200 });
        var all = await svc.SearchAsync(new CustomerQuery { PageSize = 200 });
        Assert.All(residents.Items, i => Assert.True(i.IsResident));
        Assert.All(nonResidents.Items, i => Assert.False(i.IsResident));
        Assert.True(residents.TotalCount >= 5);
        Assert.Contains(nonResidents.Items, i => i.PhoneNumber == "0911222333");
        Assert.Equal(all.TotalCount, residents.TotalCount + nonResidents.TotalCount);

        TicketDisplayStatus StatusOf(string phone) => all.Items.Single(i => i.PhoneNumber == phone).TicketStatus;
        Assert.Equal(TicketDisplayStatus.Active, StatusOf("0988123456"));        // +60 days
        Assert.Equal(TicketDisplayStatus.ExpiringSoon, StatusOf("0912888999"));  // +3 days
        Assert.Equal(TicketDisplayStatus.Expired, StatusOf("0977345678"));       // -5 days
        Assert.Equal(TicketDisplayStatus.Active, StatusOf("0911222333"));
        Assert.Equal("MT-SEED-001", all.Items.Single(i => i.PhoneNumber == "0988123456").PrimaryTicketCode);
    }

    [Fact]
    public async Task R4_paging_and_summary()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var svc = Customers(sp);

        var page0 = await svc.SearchAsync(new CustomerQuery { PageSize = 2, PageIndex = 0 });
        var page1 = await svc.SearchAsync(new CustomerQuery { PageSize = 2, PageIndex = 1 });
        Assert.Equal(2, page0.Items.Count);
        Assert.Equal(2, page0.PageSize);
        Assert.Empty(page0.Items.Select(i => i.CustomerId).Intersect(page1.Items.Select(i => i.CustomerId)));
        Assert.Equal((page0.TotalCount + 1) / 2, page0.TotalPages);
        Assert.Equal(200, (await svc.SearchAsync(new CustomerQuery { PageSize = 10_000 })).PageSize);
        Assert.Equal(1, (await svc.SearchAsync(new CustomerQuery { PageSize = 0 })).PageSize);

        var summary = await svc.GetSummaryAsync();
        Assert.True(summary.TotalCustomers >= 6);
        Assert.True(summary.ResidentCount >= 5);
        Assert.True(summary.ActiveTicketCount >= 4);
        Assert.True(summary.ExpiringSoonCount >= 1);
        Assert.True(summary.ActiveTicketRevenue > 0);
    }

    [Fact]
    public async Task R4_details_include_vehicles_and_tickets()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var customerId = await _data.CustomerIdByPhoneAsync("0977345678");

        var details = await Customers(sp).GetDetailsAsync(customerId);

        Assert.NotNull(details);
        Assert.Equal("B-1510", details!.ApartmentCode);
        Assert.Equal(2, details.Vehicles.Count(v => v.IsActive));
        var ticket = Assert.Single(details.Tickets);
        Assert.Equal("MT-SEED-003", ticket.TicketCode);
        Assert.Equal(TicketDisplayStatus.Expired, ticket.DisplayStatus);
        Assert.Null(await Customers(sp).GetDetailsAsync(int.MaxValue));
    }

    [Fact]
    public async Task A8_vehicle_with_active_ticket_cannot_be_removed_until_the_ticket_is_suspended()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var owner = await CreateResidentAsync(sp, "E-0807", new NewVehicle(plate, moto));
        var vehicleId = await PostgresDatabaseFixture.ScalarAsync<int>(_db.ConnectionString,
            "SELECT \"CustomerVehicleId\" FROM \"CustomerVehicles\" WHERE \"CustomerId\" = @c AND \"LicensePlate\" = @p", ("c", owner), ("p", plate));
        var ticket = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(owner, plate, await _data.PlanIdAsync("Gói Xe Máy 1 Tháng")));
        Assert.True(ticket.Success, ticket.Message);

        var blocked = await Customers(sp).RemoveVehicleAsync(vehicleId);
        Assert.False(blocked.Success);
        Assert.Equal(OperationError.VehicleHasActiveTicket, blocked.Error);

        Assert.True((await Tickets(sp).SuspendTicketAsync(ticket.Value, "Tạm dừng")).Success);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var removed = await Customers(sp).RemoveVehicleAsync(vehicleId);

        Assert.True(removed.Success, removed.Message);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.CustomerVehicleRemove));
        Assert.Equal("CustomerVehicle", row.EntityType);
        Assert.Equal(vehicleId.ToString(), row.EntityId);
        AuditDb.HasKeys(row, "customerId", "licensePlate");
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerVehicleId\" = @id AND NOT \"IsActive\" AND \"RemovedAt\" IS NOT NULL", ("id", vehicleId)));
        Assert.Equal(OperationError.NotFound, (await Customers(sp).RemoveVehicleAsync(vehicleId)).Error);

        // The plate can now be registered by another customer
        var other = await CreateResidentAsync(sp, "E-0808");
        var readd = await Customers(sp).AddVehicleAsync(other, new NewVehicle(plate, moto));
        Assert.True(readd.Success, readd.Message);
    }

    [Fact]
    public async Task Vehicle_types_are_reference_data()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;

        var types = await Customers(sp).GetVehicleTypesAsync();

        Assert.Contains(types, t => t.TypeName == "Xe máy");
        Assert.Contains(types, t => t.TypeName == "Xe ô tô");
    }
}
