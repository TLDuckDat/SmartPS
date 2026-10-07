using SmartPS.Constants;
using SmartPS.Models.Navigation;

namespace SmartPS.Services.Authorization;

/// <summary>Maps each sidebar item to the permissions that unlock it (any-of).</summary>
public static class NavigationAccessPolicy
{
    public static IReadOnlyList<string> GetRequiredPermissions(NavigationItemType item)
    {
        return item switch
        {
            NavigationItemType.Overview => new[] { Permissions.ParkingView },
            NavigationItemType.ParkingMap => new[] { Permissions.ParkingView },
            NavigationItemType.Incidents => new[] { Permissions.ParkingView },
            NavigationItemType.GateControl => new[] { Permissions.ParkingCheckIn, Permissions.ParkingCheckOut },
            NavigationItemType.Shifts => new[] { Permissions.ShiftView },
            NavigationItemType.Customers => new[] { Permissions.CustomerView },
            NavigationItemType.Reports => new[] { Permissions.ReportView },
            NavigationItemType.Transactions => new[] { Permissions.ReportView },
            NavigationItemType.Pricing => new[] { Permissions.PricingManage },
            NavigationItemType.UserManagement => new[] { Permissions.UserView },
            NavigationItemType.RolePermissions => new[] { Permissions.RoleView },
            NavigationItemType.AuditLog => new[] { Permissions.AuditView },
            NavigationItemType.Settings => new[] { Permissions.SettingsManage },
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, null)
        };
    }

    public static bool CanAccess(NavigationItemType item, IPermissionService permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return GetRequiredPermissions(item).Any(permissions.HasPermission);
    }
}
