using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>R12 + decision 2 (resident overflow ResidentOnly → Mixed → VisitorOnly; visitors never ResidentOnly), R13 requested-slot validation order.</summary>
public class SlotAllocationPolicyTests
{
    private const int Moto = 1, Car = 2;

    private static SlotCandidate Slot(int id, string code, ZoneAudience audience, int vt = Moto, SlotStatus status = SlotStatus.Available, bool hasSession = false)
        => new(id, code, vt, status, audience, 100 + (int)audience, $"Z{(int)audience}", hasSession);

    [Fact]
    public void Resident_preference_is_ResidentOnly_then_Mixed_then_VisitorOnly()
    {
        Assert.Equal(new[] { ZoneAudience.ResidentOnly, ZoneAudience.Mixed, ZoneAudience.VisitorOnly },
            SlotAllocationPolicy.GetAudiencePreference(VehicleCategory.Resident));
    }

    [Theory]
    [InlineData(VehicleCategory.Visitor)]
    [InlineData(VehicleCategory.MonthlyPass)]
    public void Visitor_and_monthly_preference_is_VisitorOnly_then_Mixed(VehicleCategory category)
    {
        Assert.Equal(new[] { ZoneAudience.VisitorOnly, ZoneAudience.Mixed }, SlotAllocationPolicy.GetAudiencePreference(category));
    }

    [Fact]
    public void Blacklisted_gets_no_audience()
    {
        Assert.Empty(SlotAllocationPolicy.GetAudiencePreference(VehicleCategory.Blacklisted));
        foreach (var audience in Enum.GetValues<ZoneAudience>())
        {
            Assert.False(SlotAllocationPolicy.IsAudienceAllowed(VehicleCategory.Blacklisted, audience));
        }
    }

    [Theory]
    [InlineData(VehicleCategory.Resident, ZoneAudience.ResidentOnly, true)]
    [InlineData(VehicleCategory.Resident, ZoneAudience.Mixed, true)]
    [InlineData(VehicleCategory.Resident, ZoneAudience.VisitorOnly, true)]
    [InlineData(VehicleCategory.Visitor, ZoneAudience.ResidentOnly, false)]
    [InlineData(VehicleCategory.Visitor, ZoneAudience.Mixed, true)]
    [InlineData(VehicleCategory.Visitor, ZoneAudience.VisitorOnly, true)]
    [InlineData(VehicleCategory.MonthlyPass, ZoneAudience.ResidentOnly, false)]
    [InlineData(VehicleCategory.MonthlyPass, ZoneAudience.Mixed, true)]
    [InlineData(VehicleCategory.MonthlyPass, ZoneAudience.VisitorOnly, true)]
    public void IsAudienceAllowed_matrix(VehicleCategory category, ZoneAudience audience, bool expected)
    {
        Assert.Equal(expected, SlotAllocationPolicy.IsAudienceAllowed(category, audience));
    }

    private static readonly SlotCandidate[] AllAudiences =
    {
        Slot(1, "V-01", ZoneAudience.VisitorOnly),
        Slot(2, "M-01", ZoneAudience.Mixed),
        Slot(3, "R-01", ZoneAudience.ResidentOnly),
    };

    [Fact]
    public void Resident_picks_ResidentOnly_first()
    {
        Assert.Equal(3, SlotAllocationPolicy.PickSlot(AllAudiences, VehicleCategory.Resident, Moto)!.SlotId);
    }

    [Fact]
    public void Resident_overflows_to_Mixed_then_VisitorOnly()
    {
        Assert.Equal(2, SlotAllocationPolicy.PickSlot(AllAudiences.Where(s => s.SlotId != 3), VehicleCategory.Resident, Moto)!.SlotId);
        Assert.Equal(1, SlotAllocationPolicy.PickSlot(AllAudiences.Where(s => s.SlotId == 1), VehicleCategory.Resident, Moto)!.SlotId);
    }

    [Theory]
    [InlineData(VehicleCategory.Visitor)]
    [InlineData(VehicleCategory.MonthlyPass)]
    public void Visitor_picks_VisitorOnly_then_Mixed_and_never_ResidentOnly(VehicleCategory category)
    {
        Assert.Equal(1, SlotAllocationPolicy.PickSlot(AllAudiences, category, Moto)!.SlotId);
        Assert.Equal(2, SlotAllocationPolicy.PickSlot(AllAudiences.Where(s => s.SlotId != 1), category, Moto)!.SlotId);
        Assert.Null(SlotAllocationPolicy.PickSlot(AllAudiences.Where(s => s.SlotId == 3), category, Moto));
    }

    [Fact]
    public void Blacklisted_never_gets_a_slot()
    {
        Assert.Null(SlotAllocationPolicy.PickSlot(AllAudiences, VehicleCategory.Blacklisted, Moto));
    }

    [Fact]
    public void PickSlot_filters_vehicle_type_status_and_active_sessions()
    {
        var candidates = new[]
        {
            Slot(10, "M-01", ZoneAudience.Mixed, vt: Car),
            Slot(11, "M-02", ZoneAudience.Mixed, status: SlotStatus.Occupied),
            Slot(12, "M-03", ZoneAudience.Mixed, status: SlotStatus.Maintenance),
            Slot(13, "M-04", ZoneAudience.Mixed, hasSession: true),
            Slot(14, "M-05", ZoneAudience.Mixed),
        };

        Assert.Equal(14, SlotAllocationPolicy.PickSlot(candidates, VehicleCategory.Visitor, Moto)!.SlotId);
        Assert.Equal(10, SlotAllocationPolicy.PickSlot(candidates, VehicleCategory.Visitor, Car)!.SlotId);
        Assert.Null(SlotAllocationPolicy.PickSlot(candidates.Take(4), VehicleCategory.Visitor, Moto));
    }

    [Fact]
    public void PickSlot_orders_by_audience_preference_then_slot_code_ordinal_then_id()
    {
        var candidates = new[]
        {
            Slot(30, "M-10", ZoneAudience.Mixed),
            Slot(31, "M-02", ZoneAudience.Mixed),
            Slot(32, "m-01", ZoneAudience.Mixed),   // ordinal ("C" collation): upper case sorts before lower case
            Slot(33, "V-99", ZoneAudience.VisitorOnly),
        };

        Assert.Equal(33, SlotAllocationPolicy.PickSlot(candidates, VehicleCategory.Visitor, Moto)!.SlotId);
        Assert.Equal(31, SlotAllocationPolicy.PickSlot(candidates.Where(s => s.SlotId != 33), VehicleCategory.Visitor, Moto)!.SlotId);

        var sameCode = new[] { Slot(41, "X-01", ZoneAudience.Mixed), Slot(40, "X-01", ZoneAudience.Mixed) };
        Assert.Equal(40, SlotAllocationPolicy.PickSlot(sameCode, VehicleCategory.Visitor, Moto)!.SlotId);
    }

    [Fact]
    public void PickSlot_on_empty_input_returns_null()
    {
        Assert.Null(SlotAllocationPolicy.PickSlot(Array.Empty<SlotCandidate>(), VehicleCategory.Resident, Moto));
    }

    [Fact]
    public void ValidateRequestedSlot_reason_order()
    {
        // SlotNotFound → SlotVehicleTypeMismatch → SlotAudienceNotAllowed → SlotNotAvailable
        Assert.Equal(CheckInRejectReason.SlotNotFound, SlotAllocationPolicy.ValidateRequestedSlot(null, Moto, VehicleCategory.Visitor));

        var wrongEverything = Slot(1, "R-01", ZoneAudience.ResidentOnly, vt: Car, status: SlotStatus.Occupied);
        Assert.Equal(CheckInRejectReason.SlotVehicleTypeMismatch, SlotAllocationPolicy.ValidateRequestedSlot(wrongEverything, Moto, VehicleCategory.Visitor));

        var residentOccupied = Slot(2, "R-02", ZoneAudience.ResidentOnly, status: SlotStatus.Occupied);
        Assert.Equal(CheckInRejectReason.SlotAudienceNotAllowed, SlotAllocationPolicy.ValidateRequestedSlot(residentOccupied, Moto, VehicleCategory.Visitor));
        Assert.Equal(CheckInRejectReason.SlotAudienceNotAllowed, SlotAllocationPolicy.ValidateRequestedSlot(residentOccupied, Moto, VehicleCategory.MonthlyPass));

        Assert.Equal(CheckInRejectReason.SlotNotAvailable, SlotAllocationPolicy.ValidateRequestedSlot(residentOccupied, Moto, VehicleCategory.Resident));
        Assert.Equal(CheckInRejectReason.SlotNotAvailable,
            SlotAllocationPolicy.ValidateRequestedSlot(Slot(3, "M-01", ZoneAudience.Mixed, status: SlotStatus.Maintenance), Moto, VehicleCategory.Visitor));
        Assert.Equal(CheckInRejectReason.SlotNotAvailable,
            SlotAllocationPolicy.ValidateRequestedSlot(Slot(4, "M-02", ZoneAudience.Mixed, hasSession: true), Moto, VehicleCategory.Visitor));
    }

    [Fact]
    public void ValidateRequestedSlot_accepts_allowed_free_slots()
    {
        Assert.Equal(CheckInRejectReason.None, SlotAllocationPolicy.ValidateRequestedSlot(Slot(1, "M-01", ZoneAudience.Mixed), Moto, VehicleCategory.Visitor));
        Assert.Equal(CheckInRejectReason.None, SlotAllocationPolicy.ValidateRequestedSlot(Slot(2, "V-01", ZoneAudience.VisitorOnly), Moto, VehicleCategory.Resident));
        Assert.Equal(CheckInRejectReason.None, SlotAllocationPolicy.ValidateRequestedSlot(Slot(3, "R-01", ZoneAudience.ResidentOnly), Moto, VehicleCategory.Resident));
        Assert.Equal(CheckInRejectReason.SlotAudienceNotAllowed,
            SlotAllocationPolicy.ValidateRequestedSlot(Slot(4, "M-09", ZoneAudience.Mixed), Moto, VehicleCategory.Blacklisted));
    }
}
