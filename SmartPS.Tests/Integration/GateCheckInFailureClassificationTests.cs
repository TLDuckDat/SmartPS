using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Fix round 2 — with the database reachable, a check-in that fails for a non-connectivity reason must fail:
/// FY1 (CH33) a foreign-key violation is not a connection loss; FY2 (CH32) a cancellation is not a connection loss.
/// In both cases: no DB session, no PARKING_CHECKIN Success row, no JSONL line, no offline/memory session.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateCheckInFailureClassificationTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public GateCheckInFailureClassificationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static bool PlateInJsonl(string plate)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");
        return File.Exists(path) && File.ReadAllLines(path).Any(l => l.Contains(plate, StringComparison.Ordinal));
    }

    private static bool PlateInOfflineSessions(string plate)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Storage", "offline_active_sessions.json");
        return File.Exists(path) && File.ReadAllText(path).Contains(plate, StringComparison.Ordinal);
    }

    private async Task AssertNothingPersistedAsync(string plate, long idBefore)
    {
        Assert.Equal(0, await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSessions\" WHERE \"LicensePlate\" = @p", ("p", plate)));
        Assert.DoesNotContain(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn), r => r.Outcome == AuditOutcome.Success);
        Assert.False(PlateInJsonl(plate), "no JSONL line may be written for a failed check-in");
        Assert.False(PlateInOfflineSessions(plate), "no memory/offline session may be created for a failed check-in");
    }

    [Fact]
    public async Task FY1_CH33_foreign_key_violation_fails_the_check_in_instead_of_going_offline()
    {
        // The operator's account is deleted while still logged in (fresh account, no history), so inserting the
        // session violates FK ParkingSessions.CreatedByUserId → Users (23503) while the database is reachable.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await _db.ExecuteAsync("DELETE FROM \"Users\" WHERE \"UserId\" = @u", ("u", op.UserId));
        var plate = ParkingFlows.UniquePlate();
        var typeId = await ParkingFlows.MotorbikeTypeIdAsync(_db.Factory);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(
            new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = typeId });

        Assert.False(result.Success, $"FK violation was treated as offline success (SessionId={result.Session?.SessionId})");
        Assert.False(result.IsPermissionDenied);
        Assert.Null(result.Session);
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT 1"));
        await AssertNothingPersistedAsync(plate, idBefore);
    }

    [Fact]
    public async Task FY2_CH32_cancelled_online_check_in_propagates_cancellation_or_fails()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var plate = ParkingFlows.UniquePlate();
        var typeId = await ParkingFlows.MotorbikeTypeIdAsync(_db.Factory);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        GateCheckInResult? result = null;
        Exception? thrown = null;
        try
        {
            result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(
                new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = typeId }, new CancellationToken(canceled: true));
        }
        catch (OperationCanceledException ex)
        {
            thrown = ex;
        }

        Assert.True(thrown is not null || result is { Success: false },
            $"cancelled check-in returned Success={result?.Success}, Message='{result?.Message}'");
        await AssertNothingPersistedAsync(plate, idBefore);
    }
}
