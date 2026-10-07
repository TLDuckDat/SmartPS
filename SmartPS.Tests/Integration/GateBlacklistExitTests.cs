using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>R16 / A10: a vehicle blacklisted while parked gets a warning at exit, still leaves normally, and the exit is audited.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateBlacklistExitTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateBlacklistExitTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    [Fact]
    public async Task Cash_exit_of_a_vehicle_blacklisted_while_parked_warns_and_audits_next_to_checkout()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var checkIn = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new SmartPS.Models.GateControl.GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
        Assert.True(checkIn.Success, checkIn.Message);
        var entryId = await _data.AddBlacklistAsync(plate, "Báo mất cắp sau khi vào bãi");

        var calc = await sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(ResidentVisitorData.Decorate(plate));

        Assert.True(calc.Success, calc.Message);
        Assert.True(calc.IsBlacklisted);
        Assert.Equal("Báo mất cắp sau khi vào bãi", calc.BlacklistReason);
        Assert.Equal(entryId, calc.BlacklistEntryId);

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, op.UserId);

        Assert.True(result.Success, result.Message);
        var checkout = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        var warning = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistExitWarning));
        Assert.Equal(AuditOutcome.Success, warning.Outcome);
        Assert.Equal("ParkingSession", warning.EntityType);
        Assert.Equal(checkIn.Session!.SessionId.ToString(), warning.EntityId);
        Assert.Equal(checkout.EntityId, warning.EntityId);
        Assert.Equal(op.UserId, warning.UserId);
        AuditDb.HasKeys(warning, "licensePlate", "reason", "blacklistEntryId");
        var details = AuditDb.Details(warning);
        Assert.Equal("Báo mất cắp sau khi vào bãi", details.GetProperty("reason").GetString());
        Assert.Equal(entryId, details.GetProperty("blacklistEntryId").GetInt32());
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate, activeOnly: true));
    }

    [Fact]
    public async Task Exit_without_blacklist_writes_no_warning()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var plate = ParkingFlows.UniquePlate();
        await ParkingFlows.CheckInAsync(sp, plate);

        var calc = await sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(plate);
        Assert.False(calc.IsBlacklisted);
        Assert.Null(calc.BlacklistReason);
        Assert.Null(calc.BlacklistEntryId);

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, op.UserId);

        Assert.True(result.Success, result.Message);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistExitWarning));
    }

    [Fact]
    public async Task VietQR_paid_webhook_of_a_blacklisted_vehicle_writes_the_exit_warning()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var (sessionId, paymentId, plate) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
        var entryId = await _data.AddBlacklistAsync(LicensePlateNormalizer.Normalize(plate), "Nợ phí");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var webhook = await ParkingFlows.SendPaidWebhookAsync(sp, paymentId);

        Assert.True(webhook.Accepted, webhook.Message);
        Assert.True(webhook.CheckoutCompleted, webhook.Message);
        var checkout = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        var warning = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistExitWarning));
        Assert.Equal(sessionId.ToString(), warning.EntityId);
        Assert.Equal(checkout.EntityId, warning.EntityId);
        Assert.Equal(checkout.UserId, warning.UserId);
        Assert.Equal(op.UserId, warning.UserId);
        Assert.Equal(entryId, AuditDb.Details(warning).GetProperty("blacklistEntryId").GetInt32());
    }
}
