using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>AC-14 / E6: two check-ins racing for the last slot ⇒ exactly one wins; allocator locking and the DB backstop index.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class SlotConcurrencyTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public SlotConcurrencyTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<ServiceProvider> OperatorAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return sp;
    }

    [Fact]
    public async Task AC14_two_parallel_check_ins_for_the_last_slot_only_one_succeeds_repeated()
    {
        _db.RequireAvailable();
        using var sp1 = await OperatorAsync();
        using var sp2 = await OperatorAsync();

        for (var round = 0; round < 5; round++)
        {
            var vt = await _data.CreateIsolatedVehicleTypeAsync();
            var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
            var plateA = ResidentVisitorData.UniqueNormalizedPlate();
            var plateB = ResidentVisitorData.UniqueNormalizedPlate();
            using var start = new ManualResetEventSlim(false);

            Task<GateCheckInResult> Run(IServiceProvider sp, string plate) => Task.Run(async () =>
            {
                start.Wait();
                return await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
            });

            var a = Run(sp1, plateA);
            var b = Run(sp2, plateB);
            start.Set();
            var results = await Task.WhenAll(a, b);

            Assert.Equal(1, results.Count(r => r.Success));
            var loser = results.Single(r => !r.Success);
            Assert.Equal(CheckInRejectReason.NoSlotAvailable, loser.RejectReason);
            Assert.False(loser.IsPermissionDenied);
            Assert.Equal(1, await _data.ActiveSessionCountForVehicleTypeAsync(vt));
            Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"ParkingSessions\" WHERE \"SlotId\" = @s AND \"Status\" = 0", ("s", zone.SlotIds[0])));
            Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(zone.SlotIds[0]));
        }
    }

    [Fact]
    public async Task Locked_slot_is_skipped_by_a_concurrent_transaction_and_cannot_be_occupied_twice()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        await using var db1 = _db.CreateContext();
        await using var db2 = _db.CreateContext();
        await using var tx1 = await db1.Database.BeginTransactionAsync();

        var locked = await GateSlotAllocator.LockNextAsync(db1, vt, VehicleCategory.Visitor, Array.Empty<int>());
        Assert.NotNull(locked);
        Assert.Equal(zone.SlotIds[0], locked!.SlotId);
        Assert.Equal(ZoneAudience.Mixed, locked.Audience);
        Assert.Equal(zone.ZoneCode, locked.ZoneCode);
        Assert.True(await GateSlotAllocator.TryOccupyAsync(db1, locked.SlotId, "PLATEA1"));

        await using (var tx2 = await db2.Database.BeginTransactionAsync())
        {
            // SKIP LOCKED: the only slot is locked by tx1
            Assert.Null(await GateSlotAllocator.LockNextAsync(db2, vt, VehicleCategory.Visitor, Array.Empty<int>()));

            await tx1.CommitAsync();

            // After tx1 commits the conditional update finds the slot occupied
            Assert.False(await GateSlotAllocator.TryOccupyAsync(db2, locked.SlotId, "PLATEB2"));
            await tx2.RollbackAsync();
        }

        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(zone.SlotIds[0]));
        Assert.Equal("PLATEA1", await _data.SlotPlateAsync(zone.SlotIds[0]));
    }

    [Fact]
    public async Task LockNextAsync_respects_audience_and_exclusions()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);

        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        Assert.Equal(residentZone.SlotIds[0], (await GateSlotAllocator.LockNextAsync(db, vt, VehicleCategory.Resident, Array.Empty<int>()))!.SlotId);
        Assert.Equal(mixed.SlotIds[0], (await GateSlotAllocator.LockNextAsync(db, vt, VehicleCategory.Visitor, Array.Empty<int>()))!.SlotId);
        Assert.Equal(mixed.SlotIds[1], (await GateSlotAllocator.LockNextAsync(db, vt, VehicleCategory.Visitor, new[] { mixed.SlotIds[0] }))!.SlotId);
        Assert.Null(await GateSlotAllocator.LockNextAsync(db, vt, VehicleCategory.Visitor, mixed.SlotIds.ToArray()));
        Assert.Null(await GateSlotAllocator.LockNextAsync(db, vt, VehicleCategory.Blacklisted, Array.Empty<int>()));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AllocateAsync_occupies_rejects_and_follows_A5()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var noSlotsVt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        await using var db = _db.CreateContext();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var (slot, reason) = await GateSlotAllocator.AllocateAsync(db, vt, VehicleCategory.Visitor, null, "ALLOC01");
            Assert.Equal(CheckInRejectReason.None, reason);
            Assert.Equal(mixed.SlotIds[0], slot!.SlotId);

            var (none, full) = await GateSlotAllocator.AllocateAsync(db, vt, VehicleCategory.Visitor, null, "ALLOC02");
            Assert.Null(none);
            Assert.Equal(CheckInRejectReason.NoSlotAvailable, full);

            var (denied, audience) = await GateSlotAllocator.AllocateAsync(db, vt, VehicleCategory.Visitor, residentZone.SlotIds[0], "ALLOC03");
            Assert.Null(denied);
            Assert.Equal(CheckInRejectReason.SlotAudienceNotAllowed, audience);

            var (missing, notFound) = await GateSlotAllocator.AllocateAsync(db, vt, VehicleCategory.Resident, int.MaxValue, "ALLOC04");
            Assert.Null(missing);
            Assert.Equal(CheckInRejectReason.SlotNotFound, notFound);

            var (legacy, legacyReason) = await GateSlotAllocator.AllocateAsync(db, noSlotsVt, VehicleCategory.Visitor, null, "ALLOC05");
            Assert.Null(legacy);
            Assert.Equal(CheckInRejectReason.None, legacyReason);

            await tx.CommitAsync();
        }

        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(mixed.SlotIds[0]));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
    }

    [Fact]
    public async Task Duplicate_active_session_on_a_slot_violates_the_partial_unique_index()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        ParkingSession NewSession(string plate) => new()
        {
            TicketCode = "TK-DUP-" + plate,
            LicensePlate = plate,
            VehicleTypeId = vt,
            SlotId = zone.SlotIds[0],
            CheckInTime = DateTime.UtcNow,
            Status = SessionStatus.Active
        };

        await using (var db = _db.CreateContext())
        {
            db.ParkingSessions.Add(NewSession(ResidentVisitorData.UniqueNormalizedPlate()));
            await db.SaveChangesAsync();
        }

        Exception? caught = null;
        await using (var db = _db.CreateContext())
        {
            db.ParkingSessions.Add(NewSession(ResidentVisitorData.UniqueNormalizedPlate()));
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        }

        Assert.NotNull(caught);
        Assert.IsAssignableFrom<DbUpdateException>(caught);
        Assert.True(GateSlotAllocator.IsActiveSlotUniqueViolation(caught!));
        Assert.Equal("IX_ParkingSessions_SlotId_Active", GateSlotAllocator.ActiveSlotIndexName);

        // A completed session on the same slot is allowed
        await using (var db = _db.CreateContext())
        {
            var done = NewSession(ResidentVisitorData.UniqueNormalizedPlate());
            done.Status = SessionStatus.Completed;
            done.CheckOutTime = DateTime.UtcNow;
            db.ParkingSessions.Add(done);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Other_unique_violations_are_not_reported_as_slot_conflicts()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        Exception? caught = null;
        await using (var db = _db.CreateContext())
        {
            db.ParkingSlots.Add(new ParkingSlot { SlotCode = zone.SlotCodes[0], ZoneName = "dup", VehicleTypeId = vt });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        }

        Assert.NotNull(caught);
        Assert.False(GateSlotAllocator.IsActiveSlotUniqueViolation(caught!));
        Assert.False(GateSlotAllocator.IsActiveSlotUniqueViolation(new InvalidOperationException("x")));
    }

    [Fact]
    public async Task Allocator_calls_outside_a_transaction_throw()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        await using var db = _db.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => GateSlotAllocator.LockNextAsync(db, vt, VehicleCategory.Visitor, Array.Empty<int>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GateSlotAllocator.LockByIdAsync(db, zone.SlotIds[0]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GateSlotAllocator.TryOccupyAsync(db, zone.SlotIds[0], "NOTX1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GateSlotAllocator.AllocateAsync(db, vt, VehicleCategory.Visitor, null, "NOTX2"));

        // VehicleTypeHasSlotsAsync does not need a transaction
        Assert.True(await GateSlotAllocator.VehicleTypeHasSlotsAsync(db, vt));
        Assert.False(await GateSlotAllocator.VehicleTypeHasSlotsAsync(db, await _data.CreateIsolatedVehicleTypeAsync()));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(zone.SlotIds[0]));
    }

    [Fact]
    public async Task LockByIdAsync_returns_the_slot_with_its_audience_and_session_flag()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);

        await using var db = _db.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var slot = await GateSlotAllocator.LockByIdAsync(db, zone.SlotIds[0]);
        Assert.NotNull(slot);
        Assert.Equal(zone.SlotCodes[0], slot!.SlotCode);
        Assert.Equal(ZoneAudience.VisitorOnly, slot.Audience);
        Assert.Equal(vt, slot.VehicleTypeId);
        Assert.Equal(SlotStatus.Available, slot.Status);
        Assert.False(slot.HasActiveSession);
        Assert.Null(await GateSlotAllocator.LockByIdAsync(db, int.MaxValue));
        await tx.RollbackAsync();
    }
}
