using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Auth;
using SmartPS.Models.GateControl;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>
/// T-E2 (E2): with the database offline the gate keeps working from memory, writes the legacy JSONL line
/// flagged <c>"DbAudit":false</c>, and does not throw. Also R5/A13 denial paths that return results (m8).
/// </summary>
public class GateOfflineJsonlTests
{
    private const string OfflineConnection = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Pooling=false";

    private static readonly string JsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");

    private sealed class OfflineFactory : IDbContextFactory<SmartPsDbContext>
    {
        public SmartPsDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<SmartPsDbContext>().UseNpgsql(OfflineConnection).Options);
    }

    private static GateControlService CreateGate(User user, IAuditService? audit = null)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var factory = new OfflineFactory();
        var auditService = audit ?? new AuditService(factory, context);
        var guard = new AuthorizationGuard(new PermissionService(context), context, auditService);
        return new GateControlService(guard, auditService, factory);
    }

    private static List<JsonElement> JsonlLinesFor(string plate)
    {
        if (!File.Exists(JsonlPath))
        {
            return new List<JsonElement>();
        }

        return File.ReadAllLines(JsonlPath)
            .Where(l => l.Contains(plate, StringComparison.Ordinal))
            .Select(l =>
            {
                using var doc = JsonDocument.Parse(l);
                return doc.RootElement.Clone();
            })
            .ToList();
    }

    private static string UniquePlate() => "E2" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    [Fact]
    public async Task Offline_check_in_succeeds_and_jsonl_line_is_flagged_DbAudit_false()
    {
        // E2: Given DB offline, When check-in, Then success from memory and JSONL has DbAudit=false.
        var user = TestUsers.Operator();
        var gate = CreateGate(user);
        var plate = UniquePlate();

        var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = 1 });

        Assert.True(result.Success, result.Message);
        Assert.False(result.IsPermissionDenied);
        Assert.NotNull(result.Session);
        Assert.Equal(user.UserId, result.Session!.CreatedByUserId); // A13: filled with the current user

        var line = Assert.Single(JsonlLinesFor(plate));
        Assert.Equal("CHECK_IN", line.GetProperty("Action").GetString());
        Assert.Equal(JsonValueKind.False, line.GetProperty("DbAudit").ValueKind);
    }

    [Fact]
    public async Task Check_in_without_Parking_CheckIn_returns_denied_result_and_audits()
    {
        // R5 + m8: result-returning service converts the denial into IsPermissionDenied (never throws).
        var audit = new FakeAuditService();
        var gate = CreateGate(TestUsers.Build("Viewer", Permissions.ParkingView), audit);
        var plate = UniquePlate();

        var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = 1 });

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Null(result.Session);
        Assert.Empty(JsonlLinesFor(plate));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
    }

    [Fact]
    public async Task Check_in_on_behalf_of_another_user_is_denied()
    {
        // A13 / m6: non-null CreatedByUserId different from the current user is an actor mismatch.
        var audit = new FakeAuditService();
        var user = TestUsers.Operator();
        var gate = CreateGate(user, audit);
        var plate = UniquePlate();

        var result = await gate.ProcessCheckInAsync(new GateCheckInRequest
        {
            LicensePlate = plate,
            VehicleTypeId = 1,
            CreatedByUserId = user.UserId + 1
        });

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Empty(JsonlLinesFor(plate));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
    }

    [Fact]
    public async Task Check_in_when_not_logged_in_is_denied()
    {
        // R4
        var audit = new FakeAuditService();
        var context = new CurrentUserContext();
        var factory = new OfflineFactory();
        var gate = new GateControlService(new AuthorizationGuard(new PermissionService(context), context, audit), audit, factory);
        var plate = UniquePlate();

        var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = 1 });

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Empty(JsonlLinesFor(plate));
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Check_out_with_mismatched_actor_returns_denied_result()
    {
        // T-ACTOR (unit part): CompleteCheckOutAsync with ActorUserId != current user → IsPermissionDenied.
        var audit = new FakeAuditService();
        var user = TestUsers.Operator();
        var gate = CreateGate(user, audit);

        var result = await gate.CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = 1,
            ActorUserId = user.UserId + 5,
            TotalFee = 5000m
        });

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        Assert.Equal("ActorMismatch", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task FX12_CH28_after_a_connection_failure_the_audited_transaction_is_not_attempted()
    {
        // Once the request has already observed that the database is unreachable (active-session lookup etc.),
        // check-in goes straight to the memory/JSONL path instead of trying to open an audited transaction.
        var user = TestUsers.Operator();
        var context = new CurrentUserContext();
        context.SetUser(user);
        var factory = new OfflineFactory();
        var counting = new HookAuditServiceDecorator(new AuditService(factory, context));
        var guard = new AuthorizationGuard(new PermissionService(context), context, counting);
        var gate = new GateControlService(guard, counting, factory);
        var plate = UniquePlate();

        var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = 1 });

        Assert.True(result.Success, result.Message);
        Assert.False(result.IsPermissionDenied);
        Assert.Equal(0, counting.BeginCalls);
        var line = Assert.Single(JsonlLinesFor(plate));
        Assert.Equal(JsonValueKind.False, line.GetProperty("DbAudit").ValueKind);
    }
}
