using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// B6 / addendum 2: failures of the blacklist, owner and ticket lookups at check-in.
/// <list type="bullet">
/// <item>A connection failure takes the offline path (memory + JSONL, Visitor, no DB session).</item>
/// <item>Any other failure (query bug, constraint, …) fails CLOSED: no session at all, RejectReason = ClassificationFailed.</item>
/// <item>A blacklist row added after classification is caught by the re-check inside the audited transaction.</item>
/// </list>
/// Hooks match SQL text: blacklist queries contain <c>"BlacklistedVehicles"</c>, owner queries <c>"Vehicles"</c>
/// (with the leading quote, so the blacklist table does not match), ticket queries <c>"MonthlyTickets"</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateClassificationFailureTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string BlacklistSql = "\"BlacklistedVehicles\"";
    private const string VehiclesSql = "\"Vehicles\"";
    private const string TicketsSql = "\"MonthlyTickets\"";

    private static readonly string JsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");
    private static readonly string OfflinePath = Path.Combine(AppContext.BaseDirectory, "Storage", "offline_active_sessions.json");

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public GateClassificationFailureTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private static JsonElement? LastJsonl(string plate, string action)
    {
        if (!File.Exists(JsonlPath))
        {
            return null;
        }

        var line = File.ReadAllLines(JsonlPath)
            .LastOrDefault(l => l.Contains(plate, StringComparison.Ordinal) && l.Contains($"\"{action}\"", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    private static bool PlateInOfflineSessions(string plate)
        => File.Exists(OfflinePath) && File.ReadAllText(OfflinePath).Contains(plate, StringComparison.Ordinal);

    private async Task<ServiceProvider> HookedOperatorAsync(CommandHookInterceptor hook)
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db, CommandHookInterceptor.Install(_db, hook));
        await sp.LoginAsync(op.Username);
        return sp;
    }

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId)
        => sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vehicleTypeId });

    private async Task AssertFailedClosedAsync(GateCheckInResult result, string plate, TestZone zone, long idBefore)
    {
        Assert.False(result.Success, $"non-connection failure was not fail-closed: {result.Message}");
        Assert.Equal(CheckInRejectReason.ClassificationFailed, result.RejectReason);
        Assert.False(result.IsPermissionDenied);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Null(result.Session);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Equal(0, await _data.OccupiedSlotCountInZoneAsync(zone.ZoneId));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        Assert.Null(LastJsonl(plate, "CHECK_IN"));
        Assert.False(PlateInOfflineSessions(plate), "no memory/offline session may be created when classification fails closed");
    }

    private static Func<Exception> QueryBug => () => new InvalidOperationException("Simulated query bug");

    // ---- connection failure: offline path ---------------------------------------------------------------------------

    [Fact]
    public async Task Blacklist_lookup_connection_failure_falls_back_to_memory_and_writes_no_db_session()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var hook = new CommandHookInterceptor(BlacklistSql, occurrence: 1, throwInstead: true);
        using var sp = await HookedOperatorAsync(hook);
        var plate = GateTestData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var occupiedBefore = await _data.OccupiedSlotCountAsync();

        var result = await CheckInAsync(sp, plate, vt);

        Assert.Equal(1, hook.FiredCount);
        Assert.True(result.Success, result.Message);
        Assert.False(result.IsBlacklisted);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.Session!.IsMonthlyPass);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        Assert.Equal(occupiedBefore, await _data.OccupiedSlotCountAsync());

        var line = LastJsonl(plate, "CHECK_IN");
        Assert.NotNull(line);
        Assert.Equal(JsonValueKind.False, line!.Value.GetProperty("DbAudit").ValueKind);
    }

    [Fact]
    public async Task Owner_lookup_connection_failure_takes_the_offline_visitor_path()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var resident = await _data.CreateResidentAsync(vt, withTicket: true);
        var hook = new CommandHookInterceptor(VehiclesSql, occurrence: 1, throwInstead: true);
        using var sp = await HookedOperatorAsync(hook);

        var result = await CheckInAsync(sp, resident.NormalizedPlate, vt);

        Assert.Equal(1, hook.FiredCount);
        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.Session!.IsMonthlyPass);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(resident.NormalizedPlate));
    }

    // ---- non-connection failure: fail closed --------------------------------------------------------------------------

    [Fact]
    public async Task Blacklist_lookup_non_connection_failure_fails_closed()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var hook = new CommandHookInterceptor(BlacklistSql, occurrence: 1, throwInstead: true, exceptionFactory: QueryBug);
        using var sp = await HookedOperatorAsync(hook);
        var plate = GateTestData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, plate, vt);

        Assert.Equal(1, hook.FiredCount);
        await AssertFailedClosedAsync(result, plate, zone, idBefore);
    }

    [Fact]
    public async Task Blacklist_lookup_postgres_error_that_is_not_a_connection_error_fails_closed()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        // 42P01 undefined_table: the blacklist table is unreadable while the server is reachable.
        var hook = new CommandHookInterceptor(BlacklistSql, occurrence: 1, throwInstead: true,
            exceptionFactory: () => new PostgresException("relation does not exist", "ERROR", "ERROR", "42P01"));
        using var sp = await HookedOperatorAsync(hook);
        var plate = GateTestData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, plate, vt);

        Assert.Equal(1, hook.FiredCount);
        await AssertFailedClosedAsync(result, plate, zone, idBefore);
    }

    [Fact]
    public async Task Owner_lookup_non_connection_failure_fails_closed()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var hook = new CommandHookInterceptor(VehiclesSql, occurrence: 1, throwInstead: true, exceptionFactory: QueryBug);
        using var sp = await HookedOperatorAsync(hook);
        var plate = GateTestData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, plate, vt);

        Assert.Equal(1, hook.FiredCount);
        await AssertFailedClosedAsync(result, plate, zone, idBefore);
    }

    [Fact]
    public async Task Ticket_lookup_non_connection_failure_fails_closed()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var external = await _data.CreateExternalAsync(vt, withTicket: true);
        var hook = new CommandHookInterceptor(TicketsSql, occurrence: 1, throwInstead: true, exceptionFactory: QueryBug);
        using var sp = await HookedOperatorAsync(hook);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, external.NormalizedPlate, vt);

        Assert.Equal(1, hook.FiredCount);
        await AssertFailedClosedAsync(result, external.NormalizedPlate, zone, idBefore);
    }

    [Fact]
    public async Task After_a_fail_closed_attempt_the_plate_is_not_considered_inside()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var hook = new CommandHookInterceptor(BlacklistSql, occurrence: 1, throwInstead: true, exceptionFactory: QueryBug);
        var plate = GateTestData.UniqueNormalizedPlate();
        using (var sp = IntegrationServices.Create(_db, CommandHookInterceptor.Install(_db, hook)))
        {
            await sp.LoginAsync(op.Username);
            Assert.False((await CheckInAsync(sp, plate, vt)).Success);
        }

        using var sp2 = IntegrationServices.Create(_db);
        await sp2.LoginAsync(op.Username);
        var retry = await CheckInAsync(sp2, plate, vt);

        Assert.True(retry.Success, retry.Message);
        Assert.Equal(1, await _data.SessionCountForPlateAsync(plate, activeOnly: true));
    }

    // ---- in-transaction re-check ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Blacklist_row_added_after_classification_is_caught_inside_the_transaction()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = GateTestData.UniqueNormalizedPlate();

        // The first ticket query runs during classification, after the first blacklist lookup; the row is inserted
        // (raw plate, unhooked connection) right before it, so only the in-transaction re-check can see it.
        var hook = new CommandHookInterceptor(TicketsSql, occurrence: 1,
            before: async () => await _data.AddBlacklistAsync(GateTestData.Raw(plate), "Thêm trong lúc phân loại"));
        using var sp = await HookedOperatorAsync(hook);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, plate.ToLowerInvariant(), vt);

        Assert.Equal(1, hook.FiredCount);
        Assert.False(result.Success);
        Assert.True(result.IsBlacklisted);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal("Thêm trong lúc phân loại", result.BlacklistReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(zone.SlotIds[0]));
        Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }
}
