namespace SmartPS.Data;

/// <summary>
/// Chuỗi kết nối tới cơ sở dữ liệu của ứng dụng, đăng ký một lần trong DI. Dùng cho code truy cập ADO.NET thuần
/// (ví dụ AuditQueryService) để không phải tạo DbContext của EF Core chỉ để lấy chuỗi kết nối.
/// </summary>
public sealed record DatabaseConnectionInfo(string ConnectionString)
{
    public string ConnectionString { get; } = string.IsNullOrWhiteSpace(ConnectionString)
        ? throw new ArgumentException("The database connection string must not be empty.", nameof(ConnectionString))
        : ConnectionString;
}
