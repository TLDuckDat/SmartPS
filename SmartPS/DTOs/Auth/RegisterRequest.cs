namespace SmartPS.DTOs.Auth;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public int RoleId { get; set; }
}