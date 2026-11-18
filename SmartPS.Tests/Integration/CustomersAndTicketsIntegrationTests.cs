using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Tests.TestSupport;
using Xunit;

namespace SmartPS.Tests.Integration;

[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class CustomersAndTicketsIntegrationTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public CustomersAndTicketsIntegrationTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    private async Task<(ServiceProvider Sp, ITicketsService Tickets, IVehiclesService Vehicles, ICustomersService Customers)> SetupServicesAsync()
    {
        _db.RequireAvailable();
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();

        return (
            sp,
            sp.GetRequiredService<ITicketsService>(),
            sp.GetRequiredService<IVehiclesService>(),
            sp.GetRequiredService<ICustomersService>()
        );
    }

    private static string UniquePlate() => "29A" + Random.Shared.Next(10000, 99999);

    [Fact]
    public async Task Case01_IssueTicket_ValidInput_CreatesTicketAndIssueHistoryRecord()
    {
        var (sp, tickets, vehicles, customers) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 1", PhoneNumber = "0912345601" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var plate = UniquePlate();
        var vehicle = new Vehicle { LicensePlate = plate, VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // 1. Cấp vé mới 1 tháng
        var req = new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash);
        var result = await tickets.IssueTicketAsync(req);

        Assert.True(result.Success);

        // Kiểm tra vé trong Database
        var createdTicket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.VehicleId == vehicle.VehicleId);
        Assert.NotNull(createdTicket);
        Assert.Equal(MonthlyTicketStatus.Active, createdTicket.Status);
        Assert.Equal(plate, createdTicket.RegisteredLicensePlate);
        Assert.True(createdTicket.MonthlyPrice > 0);

        // Kiểm tra bảng lịch sử: có đúng 1 bản ghi Issue
        var histories = await db.MonthlyTicketHistories.Where(h => h.TicketId == createdTicket.TicketId).ToListAsync();
        Assert.Single(histories);
        var history = histories[0];
        Assert.Equal(TicketHistoryAction.Issue, history.Action);
        Assert.Equal(plate, history.NewLicensePlate);
        Assert.Equal(vehicle.VehicleId, history.NewVehicleId);
        Assert.Equal(createdTicket.MonthlyPrice, history.Amount);
    }

    [Fact]
    public async Task Case02_IssueTicket_WhenVehicleAlreadyHasActiveTicket_Fails()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 2", PhoneNumber = "0912345602" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var plate = UniquePlate();
        var vehicle = new Vehicle { LicensePlate = plate, VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // Cấp lần 1: Thành công
        var res1 = await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));
        Assert.True(res1.Success);

        // Cấp lần 2 cho cùng xe: Phải thất bại
        var res2 = await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 3, PaymentMethod.Cash));
        Assert.False(res2.Success);
        Assert.Contains("vé tháng", res2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Case03_RenewTicket_ActiveTicket_ExtendsEndDateAndLogsHistory()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 3", PhoneNumber = "0912345603" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var vehicle = new Vehicle { LicensePlate = UniquePlate(), VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // Cấp vé 1 tháng
        var resIssue = await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));
        Assert.True(resIssue.Success);

        var ticket = await db.MonthlyTickets.FirstAsync(t => t.VehicleId == vehicle.VehicleId);
        var oldEndDate = ticket.EndDate;

        // Gia hạn thêm 2 tháng
        var resRenew = await tickets.RenewTicketAsync(new RenewTicketRequest(ticket.TicketId, 2, PaymentMethod.VietQR));
        Assert.True(resRenew.Success);

        // Kiểm tra vé sau gia hạn
        await using var verifyDb = await _db.Factory.CreateDbContextAsync();
        var updatedTicket = await verifyDb.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket.TicketId);
        Assert.True(updatedTicket.EndDate > oldEndDate);
        Assert.Equal(3, updatedTicket.DurationMonths);

        // Kiểm tra lịch sử có dòng Renew
        var renewHistory = await verifyDb.MonthlyTicketHistories
            .AsNoTracking()
            .Where(h => h.TicketId == ticket.TicketId && h.Action == TicketHistoryAction.Renew)
            .FirstOrDefaultAsync();
        Assert.NotNull(renewHistory);
        Assert.Equal(2, renewHistory.AddedMonths);
        Assert.Equal(PaymentMethod.VietQR, renewHistory.PaymentMethod);
    }

    [Fact]
    public async Task Case04_RenewTicket_ExpiredTicket_CalculatesFromUtcNow()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 4", PhoneNumber = "0912345604" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var vehicle = new Vehicle { LicensePlate = UniquePlate(), VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // Tạo vé đã quá hạn từ 10 ngày trước
        var pastDate = DateTime.UtcNow.AddDays(-10);
        var ticket = new MonthlyTicket
        {
            CustomerId = customer.CustomerId,
            VehicleId = vehicle.VehicleId,
            RegisteredLicensePlate = vehicle.LicensePlate,
            VehicleTypeId = 1,
            DurationMonths = 1,
            MonthlyPrice = 100000m,
            StartDate = pastDate.AddMonths(-1),
            EndDate = pastDate,
            TicketCode = $"MT-OLD-{Random.Shared.Next(1000, 9999)}",
            Status = MonthlyTicketStatus.Active,
            CreatedAt = pastDate.AddMonths(-1)
        };
        db.MonthlyTickets.Add(ticket);
        await db.SaveChangesAsync();

        // Gia hạn 1 tháng cho vé đã quá hạn
        var resRenew = await tickets.RenewTicketAsync(new RenewTicketRequest(ticket.TicketId, 1, PaymentMethod.Cash));
        Assert.True(resRenew.Success);

        await using var verifyDb = await _db.Factory.CreateDbContextAsync();
        var updatedTicket = await verifyDb.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket.TicketId);
        // Hạn mới phải tính cộng từ thời điểm hiện tại
        Assert.True(updatedTicket.EndDate > DateTime.UtcNow.AddDays(25));
    }

    [Fact]
    public async Task Case05_ToggleLock_ActiveAndSuspended_SwitchesStateAndLogsHistory()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 5", PhoneNumber = "0912345605" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var vehicle = new Vehicle { LicensePlate = UniquePlate(), VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));
        var ticket = await db.MonthlyTickets.FirstAsync(t => t.VehicleId == vehicle.VehicleId);

        // Khóa vé
        var resLock = await tickets.ToggleLockTicketAsync(ticket.TicketId, "Tạm khóa do nghi vấn");
        Assert.True(resLock.Success);

        await using var verifyDb1 = await _db.Factory.CreateDbContextAsync();
        var lockedTicket = await verifyDb1.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket.TicketId);
        Assert.Equal(MonthlyTicketStatus.Suspended, lockedTicket.Status);

        var lockHistory = await verifyDb1.MonthlyTicketHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.TicketId == ticket.TicketId && h.Action == TicketHistoryAction.Lock);
        Assert.NotNull(lockHistory);

        // Mở khóa vé
        var resUnlock = await tickets.ToggleLockTicketAsync(ticket.TicketId, "Đã xác minh, mở lại");
        Assert.True(resUnlock.Success);

        await using var verifyDb2 = await _db.Factory.CreateDbContextAsync();
        var unlockedTicket = await verifyDb2.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket.TicketId);
        Assert.Equal(MonthlyTicketStatus.Active, unlockedTicket.Status);
    }

    [Fact]
    public async Task Case06_CancelTicket_SoftDelete_PreservesRecordAndLogsHistory()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 6", PhoneNumber = "0912345606" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var vehicle = new Vehicle { LicensePlate = UniquePlate(), VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));
        var ticket = await db.MonthlyTickets.FirstAsync(t => t.VehicleId == vehicle.VehicleId);

        // Hủy vé
        var resCancel = await tickets.CancelTicketAsync(ticket.TicketId, "Khách yêu cầu chấm dứt hợp đồng");
        Assert.True(resCancel.Success);

        // Vé VẪN TỒN TẠI trong DB, chỉ đổi trạng thái sang Cancelled
        await using var verifyDb = await _db.Factory.CreateDbContextAsync();
        var cancelledTicket = await verifyDb.MonthlyTickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == ticket.TicketId);
        Assert.NotNull(cancelledTicket);
        Assert.Equal(MonthlyTicketStatus.Cancelled, cancelledTicket.Status);

        var cancelHistory = await verifyDb.MonthlyTicketHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.TicketId == ticket.TicketId && h.Action == TicketHistoryAction.Cancel);
        Assert.NotNull(cancelHistory);
        Assert.Equal("Khách yêu cầu chấm dứt hợp đồng", cancelHistory.Reason);

        // Thao tác gia hạn trên vé đã hủy phải bị từ chối
        var resRenew = await tickets.RenewTicketAsync(new RenewTicketRequest(ticket.TicketId, 1, PaymentMethod.Cash));
        Assert.False(resRenew.Success);
    }

    [Fact]
    public async Task Case07_ChangePlate_PreservesHistoryChainAndUpdatesVehicle()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 7", PhoneNumber = "0912345607" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var plateA = UniquePlate();
        var vehicleA = new Vehicle { LicensePlate = plateA, VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicleA);

        var plateB = UniquePlate();
        var vehicleB = new Vehicle { LicensePlate = plateB, VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicleB);
        await db.SaveChangesAsync();

        // 1. Cấp vé cho xe A
        await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicleA.VehicleId, 1, PaymentMethod.Cash));
        var ticket = await db.MonthlyTickets.FirstAsync(t => t.VehicleId == vehicleA.VehicleId);

        // 2. Gia hạn vé thêm 1 tháng
        await tickets.RenewTicketAsync(new RenewTicketRequest(ticket.TicketId, 1, PaymentMethod.Cash));

        // 3. Đổi vé sang xe B
        var resChange = await tickets.ChangePlateAsync(new ChangePlateRequest(ticket.TicketId, vehicleB.VehicleId, "Bán xe A mua xe B"));
        Assert.True(resChange.Success);

        // Vé cập nhật sang xe B và biển B
        await using var verifyDb = await _db.Factory.CreateDbContextAsync();
        var updatedTicket = await verifyDb.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket.TicketId);
        Assert.Equal(vehicleB.VehicleId, updatedTicket.VehicleId);
        Assert.Equal(plateB, updatedTicket.RegisteredLicensePlate);

        // TOÀN BỘ LỊCH SỬ PHẢI ĐƯỢC GIỮ NGUYÊN (Issue + Renew + ChangePlate = 3 bản ghi)
        var allHistories = await verifyDb.MonthlyTicketHistories
            .AsNoTracking()
            .Where(h => h.TicketId == ticket.TicketId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync();
        Assert.Equal(3, allHistories.Count);
        Assert.Equal(TicketHistoryAction.Issue, allHistories[0].Action);
        Assert.Equal(TicketHistoryAction.Renew, allHistories[1].Action);
        Assert.Equal(TicketHistoryAction.ChangePlate, allHistories[2].Action);
        Assert.Equal(plateA, allHistories[2].OldLicensePlate);
        Assert.Equal(plateB, allHistories[2].NewLicensePlate);
    }

    [Fact]
    public async Task Case08_VehicleLimit_HouseholdReachedQuota_RejectsAddingNewVehicle()
    {
        var (sp, _, vehicles, customers) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        // Tạo hộ có quota tối đa 2 xe
        var apt = "APT-TEST-" + Random.Shared.Next(1000, 9999);
        var resHh = await customers.AddHouseholdAsync(new CreateHouseholdRequest(apt, 2, "Hộ test quota"));
        Assert.True(resHh.Success);
        var hh = await db.Households.FirstAsync(h => h.ApartmentCode == apt);

        // Tạo chủ hộ
        var cust = new Customer { FullName = "Chủ Hộ Quota", PhoneNumber = "0988888888", HouseholdId = hh.HouseholdId };
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        // Thêm xe 1: Thành công
        var resV1 = await vehicles.AddVehicleAsync(new CreateVehicleRequest(cust.CustomerId, UniquePlate(), 1));
        Assert.True(resV1.Success);

        // Thêm xe 2: Thành công (đạt 2/2)
        var plate2 = UniquePlate();
        var resV2 = await vehicles.AddVehicleAsync(new CreateVehicleRequest(cust.CustomerId, plate2, 1));
        Assert.True(resV2.Success);

        // Thêm xe 3: PHẢI BỊ TỪ CHỐI BÁO VƯỢT QUOTA
        var resV3 = await vehicles.AddVehicleAsync(new CreateVehicleRequest(cust.CustomerId, UniquePlate(), 1));
        Assert.False(resV3.Success);
        Assert.Contains("giới hạn", resV3.Message, StringComparison.OrdinalIgnoreCase);

        // Xóa xe 2 -> Quota lại còn 1/2 -> Thêm xe 3 thành công
        var v2 = await db.Vehicles.FirstAsync(v => v.LicensePlate == plate2);
        var resDel = await vehicles.DeleteVehicleAsync(v2.VehicleId);
        Assert.True(resDel.Success);

        var resV3Retry = await vehicles.AddVehicleAsync(new CreateVehicleRequest(cust.CustomerId, UniquePlate(), 1));
        Assert.True(resV3Retry.Success);
    }

    [Fact]
    public async Task Case09_SuspendedTicket_PreventsIssuingNewTicket_AndPreventsDuplicateActiveUnlock()
    {
        var (sp, tickets, _, _) = await SetupServicesAsync();
        using var scope = sp;
        await using var db = await _db.Factory.CreateDbContextAsync();

        var customer = new Customer { FullName = "Nguyễn Văn Test 9", PhoneNumber = "0912345609" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var plate = UniquePlate();
        var vehicle = new Vehicle { LicensePlate = plate, VehicleTypeId = 1, OwnerCustomerId = customer.CustomerId, IsActive = true };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        // 1. Cấp vé 1
        var res1 = await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));
        Assert.True(res1.Success);

        var ticket1 = await db.MonthlyTickets.FirstAsync(t => t.VehicleId == vehicle.VehicleId);

        // 2. Tạm khóa vé 1
        var lockRes = await tickets.ToggleLockTicketAsync(ticket1.TicketId, "Khóa tạm thời");
        Assert.True(lockRes.Success);

        var lockedTicket = await db.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket1.TicketId);
        Assert.Equal(MonthlyTicketStatus.Suspended, lockedTicket.Status);

        // 3. Cố tình cấp vé mới cho cùng chiếc xe này -> PHẢI BỊ CHẶN (Cách 1)
        var issueWhileSuspended = await tickets.IssueTicketAsync(new IssueTicketRequest(customer.CustomerId, vehicle.VehicleId, 1, PaymentMethod.Cash));
        Assert.False(issueWhileSuspended.Success);
        Assert.Contains("tạm khóa", issueWhileSuspended.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Mở khóa lại vé 1 -> Thành công bình thường
        var unlockRes = await tickets.ToggleLockTicketAsync(ticket1.TicketId, "Mở khóa");
        Assert.True(unlockRes.Success);
        var activeTicket = await db.MonthlyTickets.AsNoTracking().FirstAsync(t => t.TicketId == ticket1.TicketId);
        Assert.Equal(MonthlyTicketStatus.Active, activeTicket.Status);
    }
}
