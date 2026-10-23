using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Incidents;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.Incidents;
using SmartPS.Services.Settings;

namespace SmartPS.Tests.Integration;

/// <summary>Incidents are stored in the database: gate staff report them, Incident.Manage resolves them, overstays are scanned from active sessions.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class IncidentServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public IncidentServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private async Task<ServiceProvider> LoginAsync(string roleName)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, roleName);
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        return sp;
    }

    [Fact]
    public async Task Operator_reports_an_incident_that_is_listed_as_open_with_a_yearly_code()
    {
        _db.RequireAvailable();
        using var sp = await LoginAsync("Operator");
        var incidents = sp.GetRequiredService<IIncidentService>();

        var first = await incidents.ReportAsync(IncidentType.LostTicket, " 51f-123.45 ", "Khách làm mất vé lượt");
        var second = await incidents.ReportAsync(IncidentType.Other, null, "Ghi chú khác");

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.Matches(@"^SC-\d{4}-\d{3}$", first.Message);
        Assert.NotEqual(first.Message, second.Message);

        var listed = (await incidents.GetIncidentsAsync()).Single(i => i.IncidentCode == first.Message);
        Assert.Equal(IncidentType.LostTicket, listed.Type);
        Assert.Equal("51F-123.45", listed.LicensePlate);
        Assert.Equal(IncidentStatus.Open, listed.Status);
        Assert.False(string.IsNullOrEmpty(listed.ReportedBy));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Report_without_description_is_a_validation_error(string description)
    {
        _db.RequireAvailable();
        using var sp = await LoginAsync("Operator");

        var result = await sp.GetRequiredService<IIncidentService>().ReportAsync(IncidentType.Other, null, description);

        Assert.False(result.Success);
        Assert.Equal(OperationError.Validation, result.Error);
    }

    [Fact]
    public async Task Operator_cannot_resolve_but_Manager_can_and_only_once()
    {
        _db.RequireAvailable();
        string code;
        using (var operatorSp = await LoginAsync("Operator"))
        {
            var report = await operatorSp.GetRequiredService<IIncidentService>().ReportAsync(IncidentType.UnreadablePlate, "30A-678.90", "Biển số dính bùn");
            Assert.True(report.Success, report.Message);
            code = report.Message;

            var incident = (await operatorSp.GetRequiredService<IIncidentService>().GetIncidentsAsync()).Single(i => i.IncidentCode == code);
            var denied = await operatorSp.GetRequiredService<IIncidentService>().ResolveAsync(incident.IncidentId, "tự xử lý");
            Assert.True(denied.IsPermissionDenied);
        }

        using var managerSp = await LoginAsync("Manager");
        var service = managerSp.GetRequiredService<IIncidentService>();
        var target = (await service.GetIncidentsAsync()).Single(i => i.IncidentCode == code);

        Assert.Equal(OperationError.Validation, (await service.ResolveAsync(target.IncidentId, " ")).Error);

        var resolved = await service.ResolveAsync(target.IncidentId, "Đã đối chiếu ảnh lúc vào");
        Assert.True(resolved.Success, resolved.Message);

        var after = (await service.GetIncidentsAsync()).Single(i => i.IncidentCode == code);
        Assert.Equal(IncidentStatus.Resolved, after.Status);
        Assert.Equal("Đã đối chiếu ảnh lúc vào", after.ResolutionNotes);
        Assert.NotNull(after.ResolvedAt);
        Assert.False(string.IsNullOrEmpty(after.ResolvedBy));

        Assert.Equal(OperationError.Validation, (await service.ResolveAsync(target.IncidentId, "lần hai")).Error);
    }

    [Fact]
    public async Task Overstay_scan_reports_each_overstaying_plate_once()
    {
        _db.RequireAvailable();
        using var sp = await LoginAsync("Manager");
        var admin = IntegrationServices.Create(_db);
        await admin.LoginAdminAsync();
        Assert.True((await admin.GetRequiredService<ISystemSettingsService>()
            .UpdateOperationalSettingsAsync(new OperationalSettings(24, 2))).Success);

        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            var vehicleTypeId = await db.VehicleTypes.Select(v => v.VehicleTypeId).FirstAsync();
            db.ParkingSessions.AddRange(
                Session("OVS-OLD-1", "88A-111.11", vehicleTypeId, DateTime.UtcNow.AddHours(-30)),
                Session("OVS-NEW-1", "88A-222.22", vehicleTypeId, DateTime.UtcNow.AddHours(-2)));
            await db.SaveChangesAsync();
        }

        var service = sp.GetRequiredService<IIncidentService>();
        var firstScan = await service.ScanOverstayAsync();
        var secondScan = await service.ScanOverstayAsync();

        Assert.True(firstScan.Success, firstScan.Message);
        Assert.Equal("1", firstScan.Message);
        Assert.Equal("0", secondScan.Message);

        var overstays = (await service.GetIncidentsAsync()).Where(i => i.Type == IncidentType.Overstay).ToList();
        var single = Assert.Single(overstays);
        Assert.Equal("88A-111.11", single.LicensePlate);
        Assert.Contains("24 giờ", single.Description);
        admin.Dispose();
    }

    private static ParkingSession Session(string ticket, string plate, int vehicleTypeId, DateTime checkInUtc) => new()
    {
        TicketCode = ticket,
        LicensePlate = plate,
        VehicleTypeId = vehicleTypeId,
        CheckInTime = checkInUtc,
        Status = SessionStatus.Active
    };
}
