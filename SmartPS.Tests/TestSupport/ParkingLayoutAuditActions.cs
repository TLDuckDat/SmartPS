namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Audit actions of the parking map layout (create zone, add / maintain / delete slots), declared after BLACKLIST_DELETE.
/// They are covered by ParkingLayoutServiceTests, not by the R14 catalogue.
/// </summary>
public static class ParkingLayoutAuditActions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        AuditActions.ZoneCreate,
        AuditActions.SlotCreate,
        AuditActions.SlotStatusUpdate,
        AuditActions.SlotDelete
    };
}
