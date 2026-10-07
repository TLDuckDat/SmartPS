using SmartPS.Models.Parking;
using SmartPS.Services.ParkingZones;
using SmartPS.ViewModels.ParkingMap;

namespace SmartPS.Tests.Unit;

/// <summary>R20 / AC-12: the parking map groups slots by zone (no SlotCode prefix rules), shows Maintenance separately, counts per zone.</summary>
public class ParkingMapBuilderTests
{
    private static readonly VehicleType Moto = new() { VehicleTypeId = 1, TypeName = "Xe máy" };
    private static readonly VehicleType Car = new() { VehicleTypeId = 2, TypeName = "Xe ô tô" };

    private static readonly ParkingZoneInfo ZoneA = new(1, "ZONE_A", "Khu A", ZoneAudience.Mixed, 1, 20);
    private static readonly ParkingZoneInfo ZoneB = new(2, "ZONE_B", "Khu B", ZoneAudience.VisitorOnly, 2, 10);
    private static readonly ParkingZoneInfo ZoneR = new(3, "ZONE_R", "Khu Cư dân (B2)", ZoneAudience.ResidentOnly, null, 6);
    private static readonly ParkingZoneInfo ZoneEmpty = new(4, "ZONE_E", "Khu trống", ZoneAudience.Mixed, null, 0);

    private static ParkingSlot Slot(int id, string code, ParkingZoneInfo? zone, VehicleType type, SlotStatus status = SlotStatus.Available, string? plate = null)
        => new()
        {
            SlotId = id,
            SlotCode = code,
            ZoneId = zone?.ZoneId,
            ZoneName = zone?.ZoneName ?? "Khu cũ",
            VehicleTypeId = type.VehicleTypeId,
            VehicleType = type,
            Status = status,
            CurrentLicensePlate = plate
        };

    private static ParkingSession ActiveSession(int slotId, string plate, CustomerType type = CustomerType.Regular, bool monthly = false)
        => new()
        {
            SessionId = 100 + slotId,
            SlotId = slotId,
            LicensePlate = plate,
            TicketCode = "TK-" + slotId,
            VehicleTypeId = 1,
            CheckInTime = new DateTime(2026, 10, 8, 1, 0, 0, DateTimeKind.Utc),
            Status = SessionStatus.Active,
            CustomerType = type,
            IsMonthlyPass = monthly,
            Customer = type == CustomerType.Resident ? new Customer { CustomerId = 5, FullName = "Cư dân A", Type = CustomerType.Resident } : null
        };

    private static readonly ParkingZoneInfo[] Zones = { ZoneR, ZoneB, ZoneA, ZoneEmpty };

    private static IReadOnlyList<ParkingSlot> Slots() => new[]
    {
        Slot(1, "A-01", ZoneA, Moto),
        Slot(2, "A-02", ZoneA, Moto, SlotStatus.Maintenance),
        Slot(3, "A-03", ZoneA, Moto),                       // has an active session but slot still says Available
        Slot(4, "A-04", ZoneA, Moto, SlotStatus.Occupied, "29X11111"),
        Slot(5, "X-01", ZoneB, Car),                        // non A/B prefix in the car zone
        Slot(6, "A-99", ZoneB, Car),                        // "A" prefix but belongs to ZONE_B
        Slot(7, "R-M01", ZoneR, Moto, SlotStatus.Occupied, "51F12345"),
        Slot(8, "R-C01", ZoneR, Car),
        Slot(9, "OLD-1", null, Moto),                       // legacy slot without zone
    };

    private static IReadOnlyList<ZoneGroupData> Build()
        => ParkingMapBuilder.BuildGroups(Slots(), new[] { ActiveSession(3, "59T22222"), ActiveSession(7, "51F12345", CustomerType.Resident, monthly: true) }, Zones);

    [Fact]
    public void Zones_are_ordered_by_zone_code_and_include_empty_zones()
    {
        var zoned = Build().Where(g => g.ZoneId.HasValue).Select(g => g.ZoneCode).ToList();

        Assert.Equal(new[] { "ZONE_A", "ZONE_B", "ZONE_E", "ZONE_R" }, zoned);
        var empty = Build().Single(g => g.ZoneId == ZoneEmpty.ZoneId);
        Assert.Empty(empty.Slots);
        Assert.Equal(0, empty.TotalCount);
    }

    [Fact]
    public void Slots_are_grouped_by_zone_id_not_by_code_prefix()
    {
        var groups = Build();

        Assert.Equal(new[] { 5, 6 }, groups.Single(g => g.ZoneId == ZoneB.ZoneId).Slots.Select(s => s.SlotId).OrderBy(x => x));
        Assert.Equal(new[] { 1, 2, 3, 4 }, groups.Single(g => g.ZoneId == ZoneA.ZoneId).Slots.Select(s => s.SlotId).OrderBy(x => x));
        Assert.Equal(new[] { 7, 8 }, groups.Single(g => g.ZoneId == ZoneR.ZoneId).Slots.Select(s => s.SlotId).OrderBy(x => x));
    }

    [Fact]
    public void Zone_audience_comes_from_the_zone()
    {
        var groups = Build();

        Assert.Equal(ZoneAudience.Mixed, groups.Single(g => g.ZoneId == ZoneA.ZoneId).Audience);
        Assert.Equal(ZoneAudience.VisitorOnly, groups.Single(g => g.ZoneId == ZoneB.ZoneId).Audience);
        Assert.Equal(ZoneAudience.ResidentOnly, groups.Single(g => g.ZoneId == ZoneR.ZoneId).Audience);
        Assert.Equal("Khu Cư dân (B2)", groups.Single(g => g.ZoneId == ZoneR.ZoneId).ZoneName);
    }

    [Fact]
    public void Maintenance_is_kept_and_not_counted_as_available()
    {
        var a = Build().Single(g => g.ZoneId == ZoneA.ZoneId);

        Assert.Equal(SlotStatus.Maintenance, a.Slots.Single(s => s.SlotId == 2).EffectiveStatus);
        Assert.Equal(4, a.TotalCount);
        Assert.Equal(1, a.AvailableCount);   // only A-01
        Assert.Equal(2, a.OccupiedCount);    // A-03 (session) + A-04 (marked occupied)
        Assert.Equal(1, a.MaintenanceCount);
    }

    [Fact]
    public void Active_session_makes_a_slot_occupied_and_exposes_session_data()
    {
        var slots = Build().SelectMany(g => g.Slots).ToList();

        var a03 = slots.Single(s => s.SlotId == 3);
        Assert.Equal(SlotStatus.Occupied, a03.EffectiveStatus);
        Assert.Equal("59T22222", a03.CurrentLicensePlate);
        Assert.Equal("TK-3", a03.TicketCode);

        var r = slots.Single(s => s.SlotId == 7);
        Assert.Equal(SlotStatus.Occupied, r.EffectiveStatus);
        Assert.Equal(CustomerType.Resident, r.SessionCustomerType);
        Assert.True(r.IsMonthlyPass);
        Assert.Equal("Cư dân A", r.CustomerName);

        Assert.Equal(SlotStatus.Occupied, slots.Single(s => s.SlotId == 4).EffectiveStatus);
        Assert.Equal(SlotStatus.Available, slots.Single(s => s.SlotId == 1).EffectiveStatus);
    }

    [Fact]
    public void Unzoned_slots_form_a_mixed_group_named_after_their_zone_name()
    {
        var unzoned = Build().Single(g => g.ZoneId is null);

        Assert.Equal(ZoneAudience.Mixed, unzoned.Audience);
        Assert.Equal("Khu cũ", unzoned.ZoneName);
        Assert.Equal(new[] { 9 }, unzoned.Slots.Select(s => s.SlotId));
        Assert.Equal(1, unzoned.AvailableCount);
    }

    [Fact]
    public void Every_slot_appears_exactly_once()
    {
        var ids = Build().SelectMany(g => g.Slots).Select(s => s.SlotId).OrderBy(x => x).ToList();

        Assert.Equal(Enumerable.Range(1, 9), ids);
        Assert.Equal(9, Build().Sum(g => g.TotalCount));
        Assert.All(Build(), g => Assert.Equal(g.TotalCount, g.AvailableCount + g.OccupiedCount + g.MaintenanceCount));
    }

    [Fact]
    public void Vehicle_type_name_is_carried()
    {
        var x01 = Build().SelectMany(g => g.Slots).Single(s => s.SlotId == 5);

        Assert.Equal(2, x01.VehicleTypeId);
        Assert.Equal("Xe ô tô", x01.VehicleTypeName);
    }
}
