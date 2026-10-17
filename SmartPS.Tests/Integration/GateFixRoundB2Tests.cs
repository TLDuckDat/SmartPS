using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Branch 2 fix round 1 regressions.
/// <list type="bullet">
/// <item>H2: a resident session is stored as CustomerType.Resident even when EF's value after the insert does not differ
/// from the requested value (the column default is External and EF skips the CLR default 0 on insert).</item>
/// <item>H4: a trimmed raw plate longer than 20 characters (the column size) or containing control/format characters is
/// PlateInvalid before any lookup; nothing is stored or audited; exactly 20 is accepted.</item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateFixRoundB2Tests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string JsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public GateFixRoundB2Tests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<ServiceProvider> OperatorAsync(Action<IServiceCollection>? overrides = null)
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db, overrides);
        await sp.LoginAsync(op.Username);
        return sp;
    }

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId)
        => sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vehicleTypeId });

    private Task<int> StoredCustomerTypeAsync(int sessionId)
        => _db.ScalarAsync<int>("SELECT \"CustomerType\" FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", sessionId));

    // ---- H2 -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Simulates an EF read-back that does not reveal the stored default: after the first SaveChanges that inserts a
    /// ParkingSession, the tracked CustomerType is put back to Resident (current and original value, not modified),
    /// while the row in the database holds the column default External.
    /// </summary>
    private sealed class HideStoredDefaultInterceptor : SaveChangesInterceptor
    {
        private readonly List<EntityEntry<ParkingSession>> _inserted = new();
        private int _fired;

        public int FiredCount => Volatile.Read(ref _fired);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null && _fired == 0)
            {
                _inserted.AddRange(eventData.Context.ChangeTracker.Entries<ParkingSession>()
                    .Where(e => e.State == EntityState.Added && e.Entity.CustomerType == CustomerType.Resident));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_inserted.Count > 0 && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                foreach (var entry in _inserted)
                {
                    var property = entry.Property(x => x.CustomerType);
                    property.CurrentValue = CustomerType.Resident;
                    property.OriginalValue = CustomerType.Resident;
                    property.IsModified = false;
                }

                _inserted.Clear();
            }

            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task H2_resident_session_is_stored_as_Resident_even_when_ef_read_back_hides_the_default()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var resident = await _data.CreateResidentAsync(vt);
        var interceptor = new HideStoredDefaultInterceptor();
        using var sp = await OperatorAsync(InterceptorInstall.With(_db, interceptor));

        var result = await CheckInAsync(sp, resident.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.Equal(1, interceptor.FiredCount);
        Assert.Equal((int)CustomerType.Resident, await StoredCustomerTypeAsync(result.Session!.SessionId));
    }

    [Fact]
    public async Task H2_resident_session_is_stored_as_Resident_with_the_normal_ef_read_back()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var resident = await _data.CreateResidentAsync(vt, withTicket: true);
        using var sp = await OperatorAsync();

        var result = await CheckInAsync(sp, resident.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal((int)CustomerType.Resident, await StoredCustomerTypeAsync(result.Session!.SessionId));
        Assert.Equal(1, await _db.ScalarAsync<int>("SELECT CASE WHEN \"IsMonthlyPass\" THEN 1 ELSE 0 END FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", result.Session.SessionId)));
    }

    [Fact]
    public async Task H2_visitor_session_stays_External()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        using var sp = await OperatorAsync(InterceptorInstall.With(_db, new HideStoredDefaultInterceptor()));

        var result = await CheckInAsync(sp, GateTestData.UniqueNormalizedPlate(), vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal((int)CustomerType.External, await StoredCustomerTypeAsync(result.Session!.SessionId));
    }

    // ---- H4 -------------------------------------------------------------------------------------------------------------

    /// <summary>"RVx" + dashes + rest of the normalized plate, exactly <paramref name="rawLength"/> characters.</summary>
    private static string Padded(string normalizedPlate, int rawLength)
    {
        var plate = normalizedPlate[..3] + new string('-', rawLength - normalizedPlate.Length) + normalizedPlate[3..];
        Assert.Equal(rawLength, plate.Length);
        return plate;
    }

    /// <summary>
    /// True when a JSONL line written after <paramref name="linesBefore"/> contains <paramref name="text"/>. The log is
    /// append-only and survives between runs, so only this test's lines are searched (a short plate fragment would
    /// otherwise eventually match an unrelated old line).
    /// </summary>
    private static bool InJsonlSince(int linesBefore, string text)
        => GateStorageFiles.ReadLines(JsonlPath).Skip(linesBefore).Any(l => l.Contains(text, StringComparison.Ordinal));

    private static int JsonlLineCount() => GateStorageFiles.ReadLines(JsonlPath).Length;

    private async Task AssertRejectedAsInvalidAsync(GateCheckInResult result, string normalizedPlate, TestZone zone, long idBefore, int jsonlBefore)
    {
        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.PlateInvalid, result.RejectReason);
        Assert.False(result.IsPermissionDenied);
        Assert.Null(result.Session);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(normalizedPlate));
        Assert.Equal(0, await _data.OccupiedSlotCountInZoneAsync(zone.ZoneId));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.False(InJsonlSince(jsonlBefore, normalizedPlate[4..]), "no JSONL line may be written for an invalid plate");
    }

    [Theory]
    [InlineData(21)]
    [InlineData(30)]
    public async Task H4_trimmed_raw_plate_longer_than_20_characters_is_PlateInvalid_and_nothing_is_stored(int rawLength)
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        using var sp = await OperatorAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var jsonlBefore = JsonlLineCount();

        var result = await CheckInAsync(sp, Padded(norm, rawLength), vt);

        await AssertRejectedAsInvalidAsync(result, norm, zone, idBefore, jsonlBefore);
    }

    [Fact]
    public async Task H4_raw_plate_of_exactly_20_characters_is_accepted_and_fits_the_column()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        using var sp = await OperatorAsync();

        var result = await CheckInAsync(sp, Padded(norm, 20), vt);

        Assert.True(result.Success, result.Message);
        var stored = await _db.ScalarAsync<string>("SELECT \"LicensePlate\" FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", result.Session!.SessionId));
        Assert.NotNull(stored);
        Assert.InRange(stored!.Length, 1, 20);
        Assert.Equal(norm, LicensePlateNormalizer.Normalize(stored));
    }

    [Fact]
    public async Task H4_surrounding_whitespace_is_trimmed_before_the_length_check()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        using var sp = await OperatorAsync();

        var result = await CheckInAsync(sp, new string(' ', 12) + GateTestData.Raw(norm) + new string(' ', 12), vt);

        Assert.True(result.Success, result.Message);
        var stored = await _db.ScalarAsync<string>("SELECT \"LicensePlate\" FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", result.Session!.SessionId));
        Assert.InRange(stored!.Length, 1, 20);
    }

    public static TheoryData<string> HiddenCharacters() => new()
    {
        "​", // zero-width space (Cf)
        "‍", // zero-width joiner (Cf)
        "﻿", // zero-width no-break space (Cf)
        "‮", // right-to-left override (Cf)
        "\t",     // tab (Cc)
        "\u0000", // NUL (Cc)
        "\u007F", // DEL (Cc)
        "\r\n"    // line break (Cc)
    };

    [Theory]
    [MemberData(nameof(HiddenCharacters))]
    public async Task H4_control_or_format_character_is_PlateInvalid_and_nothing_is_stored(string hidden)
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        using var sp = await OperatorAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var jsonlBefore = JsonlLineCount();

        var result = await CheckInAsync(sp, norm[..4] + hidden + norm[4..], vt);

        await AssertRejectedAsInvalidAsync(result, norm, zone, idBefore, jsonlBefore);
    }

    [Fact]
    public async Task H4_invalid_plate_is_rejected_before_any_lookup()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        await _data.AddBlacklistAsync(GateTestData.Raw(norm), "long plate");
        // Any blacklist, vehicle or ticket query would trip a non-connection failure (ClassificationFailed).
        var blacklistHook = new CommandHookInterceptor("\"BlacklistedVehicles\"", throwInstead: true, exceptionFactory: () => new InvalidOperationException("lookup ran"));
        var vehicleHook = new CommandHookInterceptor("\"Vehicles\"", throwInstead: true, exceptionFactory: () => new InvalidOperationException("lookup ran"));
        using var sp = await OperatorAsync(InterceptorInstall.With(_db, blacklistHook, vehicleHook));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var jsonlBefore = JsonlLineCount();

        var tooLong = await CheckInAsync(sp, Padded(norm, 21), vt);
        var withTab = await CheckInAsync(sp, norm[..4] + "\t" + norm[4..], vt);

        Assert.Equal(0, blacklistHook.FiredCount);
        Assert.Equal(0, vehicleHook.FiredCount);
        Assert.Equal(CheckInRejectReason.PlateInvalid, tooLong.RejectReason);
        Assert.Equal(CheckInRejectReason.PlateInvalid, withTab.RejectReason);
        Assert.False(tooLong.IsBlacklisted);
        await AssertRejectedAsInvalidAsync(tooLong, norm, zone, idBefore, jsonlBefore);
    }
}
