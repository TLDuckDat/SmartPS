using System.Windows;
using Microsoft.Extensions.Configuration;

namespace SmartPS.Services.Common;

/// <summary>
/// Chế độ hiển thị của ứng dụng.
/// Bản giao cho khách hàng chỉ hiện thông tin dễ hiểu (ví dụ "Hoạt động tốt");
/// chi tiết kỹ thuật (máy chủ, mã lỗi, nhật ký gọi cổng thanh toán...) chỉ hiện khi bật
/// <c>"App": { "DeveloperMode": true }</c> trong appsettings.json / appsettings.local.json.
/// </summary>
public static class AppMode
{
    /// <summary>true: hiện thông tin kỹ thuật dành cho người phát triển.</summary>
    public static bool IsDeveloperMode { get; set; }

    /// <summary>Dùng trong XAML: Visibility="{x:Static common:AppMode.DeveloperVisibility}".</summary>
    public static Visibility DeveloperVisibility => IsDeveloperMode ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Ngược lại với <see cref="DeveloperVisibility"/>: chỉ hiện ở bản dành cho khách hàng.</summary>
    public static Visibility CustomerVisibility => IsDeveloperMode ? Visibility.Collapsed : Visibility.Visible;

    public static void Load(IConfiguration configuration)
    {
        IsDeveloperMode = configuration.GetValue("App:DeveloperMode", false);
    }
}
