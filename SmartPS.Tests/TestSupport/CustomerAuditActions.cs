namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Audit actions of the customer / household / vehicle / ticket / blacklist module (KAN-33b), declared after SETTINGS_UPDATE.
/// They are covered by CustomersAuthzAuditTests, not by the R14 catalogue.
/// </summary>
public static class CustomerAuditActions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        AuditActions.HouseholdCreate,
        AuditActions.HouseholdDelete,
        AuditActions.CustomerCreate,
        AuditActions.CustomerDelete,
        AuditActions.VehicleCreate,
        AuditActions.VehicleDelete,
        AuditActions.TicketIssue,
        AuditActions.TicketRenew,
        AuditActions.TicketChangePlate,
        AuditActions.TicketLockToggle,
        AuditActions.TicketCancel,
        AuditActions.BlacklistAdd,
        AuditActions.BlacklistToggle,
        AuditActions.BlacklistDelete
    };
}
