namespace SmartPS.Services.Customers;

/// <summary>Che dữ liệu cá nhân trước khi ghi vào nhật ký kiểm toán bất biến.</summary>
public static class AuditPii
{
    private const int VisibleDigits = 3;

    /// <summary>"0988123456" thành "*******456"; không quá 3 ký tự thì che toàn bộ; rỗng thì trả về rỗng.</summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var value = phone.Trim();
        if (value.Length <= VisibleDigits)
        {
            return new string('*', value.Length);
        }

        return new string('*', value.Length - VisibleDigits) + value[^VisibleDigits..];
    }
}
