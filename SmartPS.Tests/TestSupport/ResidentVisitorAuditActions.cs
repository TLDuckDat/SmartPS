namespace SmartPS.Tests.TestSupport;

/// <summary>
/// The audit actions added by the gate flow (Task 1), in declaration order after AUDIT_VERIFY.
/// Customer, ticket and blacklist management actions belong to the household/vehicle module and are not ours.
/// </summary>
public static class ResidentVisitorAuditActions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        AuditActions.GateBlacklistBlocked,
        AuditActions.ZoneUpdate
    };
}
