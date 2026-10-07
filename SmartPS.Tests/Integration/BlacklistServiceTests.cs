using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>R17 / R18 / AC-11: blacklist add (normalized, one active entry per plate), soft remove with reason, history, re-entry.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class BlacklistServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public BlacklistServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId, string Username)> ManagerAsync()
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        return (sp, manager.UserId, manager.Username);
    }

    private static IBlacklistService Blacklist(IServiceProvider sp) => sp.GetRequiredService<IBlacklistService>();

    [Fact]
    public async Task Add_stores_the_normalized_plate_audits_and_rejects_a_second_active_entry()
    {
        _db.RequireAvailable();
        var (sp, userId, _) = await ManagerAsync();
        using var spScope = sp;
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var added = await Blacklist(sp).AddAsync(ResidentVisitorData.Decorate(plate), "  Nợ phí 3 lần  ");

        Assert.True(added.Success, $"{added.Error}: {added.Message}");
        await using (var ctx = _db.CreateContext())
        {
            var entry = await ctx.BlacklistEntries.AsNoTracking().SingleAsync(b => b.BlacklistEntryId == added.Value);
            Assert.Equal(plate, entry.LicensePlate);
            Assert.Equal("Nợ phí 3 lần", entry.Reason);
            Assert.True(entry.IsActive);
            Assert.Equal(userId, entry.CreatedByUserId);
            Assert.Null(entry.RemovedAt);
        }

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.BlacklistAdd));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("BlacklistEntry", row.EntityType);
        Assert.Equal(added.Value.ToString(), row.EntityId);
        AuditDb.HasKeys(row, "licensePlate", "reason");
        Assert.Equal(plate, AuditDb.Details(row).GetProperty("licensePlate").GetString());

        var auditAfterAdd = await AuditDb.MaxIdAsync(_db.Factory);
        var duplicate = await Blacklist(sp).AddAsync(plate.ToLowerInvariant(), "lần nữa");
        Assert.False(duplicate.Success);
        Assert.Equal(OperationError.BlacklistAlreadyActive, duplicate.Error);
        Assert.Equal(auditAfterAdd, await AuditDb.MaxIdAsync(_db.Factory));
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"BlacklistEntries\" WHERE \"LicensePlate\" = @p", ("p", plate)));
    }

    [Fact]
    public async Task Add_requires_a_reason_and_a_valid_plate()
    {
        _db.RequireAvailable();
        var (sp, _, _) = await ManagerAsync();
        using var spScope = sp;
        var before = await _data.CountAsync("SELECT count(*) FROM \"BlacklistEntries\"");

        Assert.Equal(OperationError.ReasonRequired, (await Blacklist(sp).AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "   ")).Error);
        Assert.Equal(OperationError.PlateInvalid, (await Blacklist(sp).AddAsync("A-1", "lý do")).Error);
        Assert.Equal(before, await _data.CountAsync("SELECT count(*) FROM \"BlacklistEntries\""));
    }

    [Fact]
    public async Task AC11_remove_requires_a_reason_keeps_history_and_the_vehicle_can_enter_again()
    {
        _db.RequireAvailable();
        var (sp, userId, username) = await ManagerAsync();
        using var spScope = sp;
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var moto = await _data.MotorbikeTypeIdAsync();
        var added = await Blacklist(sp).AddAsync(plate, "Nợ phí");
        Assert.True(added.Success, added.Message);

        // Blocked while active
        var blocked = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = moto });
        Assert.True(blocked.IsBlacklisted);

        // Reason required
        var noReason = await Blacklist(sp).RemoveAsync(added.Value, "");
        Assert.Equal(OperationError.ReasonRequired, noReason.Error);
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"BlacklistEntries\" WHERE \"BlacklistEntryId\" = @id AND \"IsActive\"", ("id", added.Value)));

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var removed = await Blacklist(sp).RemoveAsync(added.Value, "Đã thanh toán đủ");

        Assert.True(removed.Success, removed.Message);
        await using (var ctx = _db.CreateContext())
        {
            var entry = await ctx.BlacklistEntries.AsNoTracking().SingleAsync(b => b.BlacklistEntryId == added.Value);
            Assert.False(entry.IsActive);
            Assert.NotNull(entry.RemovedAt);
            Assert.Equal(userId, entry.RemovedByUserId);
            Assert.Equal("Đã thanh toán đủ", entry.RemoveReason);
            Assert.Equal("Nợ phí", entry.Reason);
        }

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.BlacklistRemove));
        Assert.Equal("BlacklistEntry", row.EntityType);
        Assert.Equal(added.Value.ToString(), row.EntityId);
        AuditDb.HasKeys(row, "licensePlate", "removeReason");
        Assert.Equal("Đã thanh toán đủ", AuditDb.Details(row).GetProperty("removeReason").GetString());

        Assert.Equal(OperationError.BlacklistNotActive, (await Blacklist(sp).RemoveAsync(added.Value, "lần nữa")).Error);
        Assert.Equal(OperationError.NotFound, (await Blacklist(sp).RemoveAsync(int.MaxValue, "x")).Error);

        // History
        var activeOnly = await Blacklist(sp).GetEntriesAsync(new BlacklistQuery());
        var history = await Blacklist(sp).GetEntriesAsync(new BlacklistQuery(IncludeInactive: true));
        Assert.DoesNotContain(activeOnly, e => e.BlacklistEntryId == added.Value);
        Assert.All(activeOnly, e => Assert.True(e.IsActive));
        var old = Assert.Single(history, e => e.BlacklistEntryId == added.Value);
        Assert.False(old.IsActive);
        Assert.Equal(username, old.RemovedByUsername);
        Assert.Equal(username, old.CreatedByUsername);
        Assert.Equal("Đã thanh toán đủ", old.RemoveReason);

        // Check-in succeeds again
        var checkIn = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = moto });
        Assert.True(checkIn.Success, checkIn.Message);
        Assert.False(checkIn.IsBlacklisted);

        // And the plate can be blacklisted again (only one ACTIVE entry per plate)
        var again = await Blacklist(sp).AddAsync(plate, "Tái phạm");
        Assert.True(again.Success, again.Message);
        Assert.Equal(2, await _data.CountAsync("SELECT count(*) FROM \"BlacklistEntries\" WHERE \"LicensePlate\" = @p", ("p", plate)));
    }

    [Fact]
    public async Task Search_filters_by_plate_fragment()
    {
        _db.RequireAvailable();
        var (sp, _, _) = await ManagerAsync();
        using var spScope = sp;

        var entries = await Blacklist(sp).GetEntriesAsync(new BlacklistQuery(SearchText: "29a-999"));

        var seed = Assert.Single(entries);
        Assert.Equal("29A99999", seed.LicensePlate);
        Assert.Equal("admin", seed.CreatedByUsername);
    }
}
