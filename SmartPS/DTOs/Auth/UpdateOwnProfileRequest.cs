// Dữ liệu đầu vào cho nhân viên tự cập nhật hồ sơ và mật khẩu của chính mình.

namespace SmartPS.DTOs.Auth;

public sealed class UpdateOwnProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}
