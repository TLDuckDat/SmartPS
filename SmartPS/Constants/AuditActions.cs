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

    public static IReadOnlyList<string> All { get; } = typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
        .OrderBy(fi => fi.MetadataToken)
        .Select(fi => (string)fi.GetRawConstantValue()!)
        .ToList();
}
