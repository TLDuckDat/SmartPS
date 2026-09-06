namespace SmartPS.DTOs.Auth;

public class UpdateUserRequest
{
    public int UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Mật khẩu mới nếu muốn đổi (để trống hoặc null nếu giữ nguyên mật khẩu cũ)
    /// </summary>
    public string? NewPassword { get; set; }
}
