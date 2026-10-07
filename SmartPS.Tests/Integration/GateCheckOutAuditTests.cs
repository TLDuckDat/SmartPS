using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>AC-9, R14 (PARKING_CHECKIN / PARKING_CHECKOUT), R15 (T-R15b), T-ACTOR, R5 for the gate, E2 JSONL flag on the online path.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateCheckOutAuditTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string JsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");

    private readonly PostgresDatabaseFixture _db;

    public GateCheckOutAuditTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static JsonElement? LastJsonl(string plate, string action)
    {
        if (!File.Exists(JsonlPath))
        {
            return null;
        }

        var line = File.ReadAllLines(JsonlPath).LastOrDefault(l => l.Contains(plate, StringComparison.Ordinal) && l.Contains($"\"{action}\"", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    private async Task<SessionStatus> SessionStatusAsync(int sessionId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.ParkingSessions.Where(s => s.SessionId == sessionId).Select(s => s.Status).SingleAsync();
    }

    [Fact]
    public async Task AC9_successful_cash_checkout_writes_exactly_one_PARKING_CHECKOUT()
    {
        // AC-9: Given a successful check-out, Then exactly one PARKING_CHECKOUT Success with plate, fee and method.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var shift = await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var (request, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, op.UserId);

        Assert.True(result.Success, result.Message);
        Assert.False(result.IsPermissionDenied);
        Assert.Equal(SessionStatus.Completed, await SessionStatusAsync(checkIn.Session!.SessionId));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("ParkingSession", row.EntityType);
        Assert.Equal(checkIn.Session.SessionId.ToString(), row.EntityId);
        Assert.Equal(op.UserId, row.UserId);
        Assert.Equal("Operator", row.RoleName);
        AuditDb.HasKeys(row, "sessionId", "licensePlate", "ticketCode", "fee", "paymentMethod", "shiftId");
        var details = AuditDb.Details(row);
        Assert.Equal(plate, details.GetProperty("licensePlate").GetString());
        Assert.Equal(request.TotalFee, details.GetProperty("fee").GetDecimal());
        Assert.True(request.TotalFee > 0);
        Assert.Equal("Cash", details.GetProperty("paymentMethod").GetString());
        Assert.Equal(shift.ShiftId, details.GetProperty("shiftId").GetInt32());

        var jsonl = LastJsonl(plate, "CHECK_OUT");
        Assert.NotNull(jsonl);
        Assert.Equal(JsonValueKind.True, jsonl!.Value.GetProperty("DbAudit").ValueKind);
    }

    [Fact]
    public async Task Check_in_writes_PARKING_CHECKIN_in_the_same_transaction()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var plate = ParkingFlows.UniquePlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);

        Assert.Equal(op.UserId, checkIn.Session!.CreatedByUserId); // A13
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal(checkIn.Session.SessionId.ToString(), row.EntityId);
        Assert.Equal(op.UserId, row.UserId);
        AuditDb.HasKeys(row, "licensePlate", "ticketCode", "vehicleTypeId", "slotCode", "isMonthlyPass");
        Assert.Equal(plate, AuditDb.String(AuditDb.Details(row), "licensePlate"));

        var jsonl = LastJsonl(plate, "CHECK_IN");
        Assert.NotNull(jsonl);
        Assert.Equal(JsonValueKind.True, jsonl!.Value.GetProperty("DbAudit").ValueKind);
    }

    [Fact]
    public async Task AC9_checkout_without_open_shift_fails_and_writes_no_success_row()
    {
        // AC-9 / T-R15b: Given check-out fails (no open shift), Then no PARKING_CHECKOUT Success.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, op.UserId);

        Assert.False(result.Success);
        Assert.False(result.IsPermissionDenied);
        Assert.Equal(SessionStatus.Active, await SessionStatusAsync(checkIn.Session!.SessionId));
        Assert.DoesNotContain(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut), r => r.Outcome == AuditOutcome.Success);
        await using var ctx = _db.CreateContext();
        Assert.False(await ctx.FinancialTransactions.AnyAsync(t => t.ParkingSessionId == checkIn.Session.SessionId));
    }

    [Fact]
    public async Task Invalid_vietqr_cash_path_fails_without_audit_success()
    {
        // T-R15b: business-rule rejection inside the audited block leaves no Success row.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = checkIn.Session!.SessionId,
            ActorUserId = op.UserId,
            PaymentMethod = PaymentMethod.VietQR,
            TotalFee = 5000m
        });

        Assert.False(result.Success);
        Assert.Equal(SessionStatus.Active, await SessionStatusAsync(checkIn.Session.SessionId));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
    }

    [Fact]
    public async Task TACTOR_checkout_with_someone_elses_actor_id_is_denied()
    {
        // T-ACTOR (m6): ActorUserId must be the logged-in user.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var other = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = checkIn.Session!.SessionId,
            ActorUserId = other.UserId,
            PaymentMethod = PaymentMethod.Cash,
            TotalFee = 5000m
        });

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal(SessionStatus.Active, await SessionStatusAsync(checkIn.Session.SessionId));
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(op.UserId, denied.UserId);
        var details = AuditDb.Details(denied);
        Assert.Equal("ActorMismatch", AuditDb.String(details, "reason"));
        Assert.Equal(other.UserId, details.GetProperty("actorUserIdRequested").GetInt32());
    }

    [Fact]
    public async Task User_without_Parking_CheckOut_cannot_complete_checkout()
    {
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("entry"),
            Permissions.ParkingView, Permissions.ParkingCheckIn, Permissions.ShiftOpen);
        var user = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        await ParkingFlows.OpenShiftAsync(sp, user.UserId);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, user.UserId);

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal(SessionStatus.Active, await SessionStatusAsync(checkIn.Session!.SessionId));
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(new[] { Permissions.ParkingCheckOut }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task User_without_Parking_CheckIn_cannot_check_in()
    {
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("viewer"), Permissions.ParkingView);
        var user = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        var plate = ParkingFlows.UniquePlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest
        {
            LicensePlate = plate,
            VehicleTypeId = await ParkingFlows.MotorbikeTypeIdAsync(_db.Factory)
        });

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.ParkingSessions.AnyAsync(s => s.LicensePlate == plate));
        }

        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(new[] { Permissions.ParkingCheckIn }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }
}
