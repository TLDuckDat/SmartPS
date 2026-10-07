namespace SmartPS.Tests.TestSupport;

/// <summary>The 13 audit actions introduced by the resident/visitor flow, in declaration order (plan §2.1).</summary>
public static class ResidentVisitorAuditActions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        AuditActions.CustomerCreate,
        AuditActions.CustomerUpdate,
        AuditActions.CustomerVehicleAdd,
        AuditActions.CustomerVehicleRemove,
        AuditActions.TicketCreate,
        AuditActions.TicketRenew,
        AuditActions.TicketSuspend,
        AuditActions.TicketResume,
        AuditActions.BlacklistAdd,
        AuditActions.BlacklistRemove,
        AuditActions.GateBlacklistBlocked,
        AuditActions.GateBlacklistExitWarning,
        AuditActions.ZoneUpdate
    };
}
