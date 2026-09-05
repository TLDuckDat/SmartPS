using System.Reflection;

namespace SmartPS.Constants;

public static class Permissions
{
    // Quản lý người dùng
    public const string UserView = "User.View";
    public const string UserCreate = "User.Create";
    public const string UserEdit = "User.Edit";
    public const string UserDelete = "User.Delete";

    // Quản lý vai trò & quyền hạn
    public const string RoleView = "Role.View";
    public const string RoleManage = "Role.Manage";

    // Quản lý bãi đỗ xe & phương tiện ra vào
    public const string ParkingView = "Parking.View";
    public const string ParkingCheckIn = "Parking.CheckIn";
    public const string ParkingCheckOut = "Parking.CheckOut";
    public const string ParkingConfigure = "Parking.Configure";

    // Quản lý biểu phí & báo cáo doanh thu
    public const string PricingManage = "Pricing.Manage";
    public const string ReportView = "Report.View";
    public const string ReportExport = "Report.Export";

    public static IReadOnlyList<string> GetAll()
    {
        return typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
            .Select(fi => (string)fi.GetValue(null)!)
            .ToList();
    }
}