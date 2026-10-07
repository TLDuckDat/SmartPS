using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Auth;
using SmartPS.Models.GateControl;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>
/// N3 / A6: with the database offline the new classification and slot-preview paths never throw and fall back to
/// legacy memory behaviour; m6: <c>ClassifyVehicleAsync</c> is guarded by Parking.CheckIn OR Parking.CheckOut.
/// </summary>
public class GateOfflineResidentVisitorTests
{
    private const string OfflineConnection = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Pooling=false";

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

    private static string UniquePlate() => "OF" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    [Fact]
    public async Task Offline_ClassifyVehicleAsync_returns_visitor_without_throwing()
    {
        var gate = CreateGate(TestUsers.Operator());
        var plate = UniquePlate();

        var c = await gate.ClassifyVehicleAsync(plate.ToLowerInvariant());

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(plate, c.NormalizedPlate);
        Assert.False(c.IsBlacklisted);
    }

    [Fact]
    public async Task Offline_check_in_succeeds_as_visitor()
    {
        var gate = CreateGate(TestUsers.Operator());
        var plate = UniquePlate();

        var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = 1 });

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.Equal(CheckInRejectReason.None, result.RejectReason);
        Assert.False(result.IsBlacklisted);
        Assert.NotNull(result.Session);
        Assert.False(result.Session!.IsMonthlyPass);
    }

    [Fact]
    public async Task Offline_SuggestAvailableSlotAsync_for_resident_returns_a_memory_slot()
    {
        var gate = CreateGate(TestUsers.Operator());

        var slot = await gate.SuggestAvailableSlotAsync(1, VehicleCategory.Resident);

        Assert.NotNull(slot);
        Assert.Equal(1, slot!.VehicleTypeId);
    }

    [Fact]
    public async Task Offline_legacy_SuggestAvailableSlotAsync_overload_still_works()
    {
        var gate = CreateGate(TestUsers.Operator());

        var slot = await gate.SuggestAvailableSlotAsync(1);

        Assert.NotNull(slot);
    }

    [Fact]
    public async Task M6_ClassifyVehicleAsync_without_gate_permissions_returns_visitor_and_audits_one_denial()
    {
        var audit = new FakeAuditService();
        var gate = CreateGate(TestUsers.Build("Viewer", Permissions.ParkingView), audit);
        var plate = UniquePlate();

        var c = await gate.ClassifyVehicleAsync(plate);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(plate, c.NormalizedPlate);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        var required = doc.RootElement.GetProperty("requiredPermissions").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Contains(Permissions.ParkingCheckIn, required);
        Assert.Contains(Permissions.ParkingCheckOut, required);
    }

    [Theory]
    [InlineData("Parking.CheckIn")]
    [InlineData("Parking.CheckOut")]
    public async Task M6_either_gate_permission_is_enough_to_classify(string permission)
    {
        var audit = new FakeAuditService();
        var gate = CreateGate(TestUsers.Build("Lane", permission), audit);

        var c = await gate.ClassifyVehicleAsync(UniquePlate());

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.DoesNotContain(audit.Entries, e => e.Action == AuditActions.AccessDenied);
    }
}
