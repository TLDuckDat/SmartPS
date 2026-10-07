using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Parking;
using SmartPS.Services.RolePermissions;
using SmartPS.Services.Shifts;

namespace SmartPS.Tests.Integration;

/// <summary>
/// T-R15a (R15): if writing the audit row fails, the business operation fails too and nothing is persisted.
/// Two fault injections: a temporary CHECK constraint on AuditLogs (DB-level failure at SaveChanges) and
/// <see cref="ThrowingAuditServiceDecorator"/> (failure inside AppendAsync).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AuditAtomicityTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public AuditAtomicityTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static bool ChainContains(Exception? ex, Func<Exception, bool> predicate)
    {
        while (ex is not null)
        {
            if (predicate(ex))
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }

    private static bool IsSimulated(Exception e) => e.Message.Contains("Simulated audit append failure", StringComparison.Ordinal);

    [Fact]
    public async Task TR15a_audit_insert_failure_rolls_back_user_creation()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var username = TestUsers.UniqueName("atomic");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");

        await _db.ExecuteAsync("ALTER TABLE \"AuditLogs\" ADD CONSTRAINT tmp_block CHECK (\"Action\" <> 'USER_CREATE') NOT VALID");
        try
        {
            var ex = await Record.ExceptionAsync(() => sp.Auth().RegisterAsync(new RegisterRequest
            {
                Username = username, Password = "Test@123", FullName = "Atomic", RoleId = operatorRoleId
            }));

            Assert.NotNull(ex);
            Assert.True(ChainContains(ex, e => e is PostgresException { SqlState: PostgresErrorCodes.CheckViolation }),
                $"expected the check-violation to surface, got {ex}");
        }
        finally
        {
            await _db.ExecuteAsync("ALTER TABLE \"AuditLogs\" DROP CONSTRAINT IF EXISTS tmp_block");
        }

        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.Users.AnyAsync(u => u.Username == username));
        }

        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserCreate));

        // The lock was released: a following audited operation completes promptly.
        var sw = Stopwatch.StartNew();
        Assert.True(await sp.Auth().RegisterAsync(new RegisterRequest
        {
            Username = username, Password = "Test@123", FullName = "Atomic", RoleId = operatorRoleId
        }));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"next audited write took {sw.Elapsed}");
    }

    [Fact]
    public async Task TR15a_append_failure_rolls_back_shift_open()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.ShiftOpen));
        await sp.LoginAsync(op.Username);
        var decorator = sp.GetRequiredService<ThrowingAuditServiceDecorator>();

        var ex = await Record.ExceptionAsync(() => sp.GetRequiredService<IShiftService>().OpenShiftAsync(op.UserId, 50_000m));

        Assert.NotNull(ex);
        Assert.True(ChainContains(ex, IsSimulated), $"unexpected exception {ex}");
        Assert.Equal(1, decorator.FailuresThrown);
        Assert.Null(await sp.GetRequiredService<IShiftService>().GetActiveShiftAsync(op.UserId));
        await using var ctx = _db.CreateContext();
        Assert.False(await ctx.Shifts.AnyAsync(s => s.OpenedByUserId == op.UserId));
        Assert.False(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditActions.ShiftOpen && a.Outcome == AuditOutcome.Success && a.UserId == op.UserId));
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
    }

    [Fact]
    public async Task TR15a_append_failure_rolls_back_checkout()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.ParkingCheckOut));
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await ParkingFlows.CheckInAsync(sp, plate);

        var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, op.UserId);

        Assert.False(result.Success);
        Assert.Equal(1, sp.GetRequiredService<ThrowingAuditServiceDecorator>().FailuresThrown);
        await using var ctx = _db.CreateContext();
        var session = await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == checkIn.Session!.SessionId);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Null(session.CheckOutTime);
        Assert.False(await ctx.FinancialTransactions.AnyAsync(t => t.ParkingSessionId == session.SessionId));
        Assert.False(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditActions.ParkingCheckOut && a.Outcome == AuditOutcome.Success && a.EntityId == session.SessionId.ToString()));
    }

    [Fact]
    public async Task TR15a_append_failure_on_check_in_leaves_no_db_session_and_falls_back_to_memory()
    {
        // R15 + E2: the DB write is rolled back with its audit row; the gate keeps working from memory and flags the JSONL line.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.ParkingCheckIn));
        await sp.LoginAsync(op.Username);
        var plate = ParkingFlows.UniquePlate();

        var result = await sp.GetRequiredService<SmartPS.Services.GateControl.IGateControlService>().ProcessCheckInAsync(
            new SmartPS.Models.GateControl.GateCheckInRequest { LicensePlate = plate, VehicleTypeId = await ParkingFlows.MotorbikeTypeIdAsync(_db.Factory) });

        Assert.True(result.Success, result.Message);
        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.ParkingSessions.AnyAsync(s => s.LicensePlate == plate));
            Assert.False(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditActions.ParkingCheckIn && a.Outcome == AuditOutcome.Success && a.UserId == op.UserId));
        }

        var jsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");
        var line = File.ReadAllLines(jsonlPath).Last(l => l.Contains(plate, StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(line);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("DbAudit").ValueKind);
    }

    [Fact]
    public async Task TR15a_append_failure_rolls_back_role_matrix_save()
    {
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("atom"), Permissions.ParkingView);
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.RolePermissionsUpdate));
        await sp.LoginAdminAsync();

        var ex = await Record.ExceptionAsync(() => sp.GetRequiredService<IRolePermissionService>().SaveAsync(
            new Dictionary<int, IReadOnlyCollection<string>> { [role.RoleId] = new[] { Permissions.ParkingView, Permissions.ReportView } }));

        Assert.NotNull(ex);
        Assert.True(ChainContains(ex, IsSimulated), $"unexpected exception {ex}");
        Assert.Equal(new HashSet<string> { Permissions.ParkingView }, await TestUsers.GrantsAsync(_db.Factory, role.RoleName));
    }

    [Fact]
    public async Task TR15a_append_failure_rolls_back_user_delete()
    {
        _db.RequireAvailable();
        var target = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.UserDelete));
        await sp.LoginAdminAsync();

        var ex = await Record.ExceptionAsync(() => sp.Auth().DeleteUserAsync(target.UserId));

        Assert.NotNull(ex);
        Assert.True(ChainContains(ex, IsSimulated), $"unexpected exception {ex}");
        await using var ctx = _db.CreateContext();
        Assert.True(await ctx.Users.AnyAsync(u => u.UserId == target.UserId));
    }
}
