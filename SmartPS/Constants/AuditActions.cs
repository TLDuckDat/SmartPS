using System.Reflection;

namespace SmartPS.Constants;

public static class AuditActions
{
    public const string AuthLoginSuccess = "AUTH_LOGIN_SUCCESS";
    public const string AuthLoginFailed = "AUTH_LOGIN_FAILED";
    public const string AuthLogout = "AUTH_LOGOUT";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string UserCreate = "USER_CREATE";
    public const string UserUpdate = "USER_UPDATE";
    public const string UserDelete = "USER_DELETE";
    public const string RolePermissionsUpdate = "ROLE_PERMISSIONS_UPDATE";
    public const string ParkingCheckIn = "PARKING_CHECKIN";
    public const string ParkingCheckOut = "PARKING_CHECKOUT";
    public const string PaymentRefund = "PAYMENT_REFUND";
    public const string PaymentCancel = "PAYMENT_CANCEL";
    public const string ShiftOpen = "SHIFT_OPEN";
    public const string ShiftClose = "SHIFT_CLOSE";
    public const string ShiftReview = "SHIFT_REVIEW";
    public const string ShiftAdjustment = "SHIFT_ADJUSTMENT";
    public const string AuditVerify = "AUDIT_VERIFY";
    public const string CustomerCreate = "CUSTOMER_CREATE";
    public const string CustomerUpdate = "CUSTOMER_UPDATE";
    public const string CustomerVehicleAdd = "CUSTOMER_VEHICLE_ADD";
    public const string CustomerVehicleRemove = "CUSTOMER_VEHICLE_REMOVE";
    public const string TicketCreate = "TICKET_CREATE";
    public const string TicketRenew = "TICKET_RENEW";
    public const string TicketSuspend = "TICKET_SUSPEND";
    public const string TicketResume = "TICKET_RESUME";
    public const string BlacklistAdd = "BLACKLIST_ADD";
    public const string BlacklistRemove = "BLACKLIST_REMOVE";
    public const string GateBlacklistBlocked = "GATE_BLACKLIST_BLOCKED";
    public const string GateBlacklistExitWarning = "GATE_BLACKLIST_EXIT_WARNING";
    public const string ZoneUpdate = "ZONE_UPDATE";
    public const string ReportExport = "REPORT_EXPORT";

    public static IReadOnlyList<string> All { get; } = typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
        .OrderBy(fi => fi.MetadataToken)
        .Select(fi => (string)fi.GetRawConstantValue()!)
        .ToList();
}
