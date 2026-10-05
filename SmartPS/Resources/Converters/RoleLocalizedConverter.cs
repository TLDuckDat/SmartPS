using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SmartPS.Models.Auth;

namespace SmartPS.Resources.Converters;

/// <summary>
/// Converter chuyển đổi Role hoặc RoleName thành tên vai trò địa phương hóa theo ngôn ngữ đang chọn
/// </summary>
public class RoleLocalizedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? roleName = null;

        if (value is Role role)
        {
            roleName = role.RoleName;
        }
        else if (value is string str)
        {
            roleName = str;
        }

        if (string.IsNullOrWhiteSpace(roleName)) return string.Empty;

        string resourceKey = roleName.Trim().ToLowerInvariant() switch
        {
            "admin" or "administrator" or "quản trị viên" => "Str_Role_Admin",
            "manager" or "quản lý" => "Str_Role_Manager",
            "operator" or "staff" or "nhân viên" or "nhân viên vận hành" => "Str_Role_Operator",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(resourceKey))
        {
            if (Application.Current?.TryFindResource(resourceKey) is string localized)
            {
                return localized;
            }
        }

        return roleName;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
