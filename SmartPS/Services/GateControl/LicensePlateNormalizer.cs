namespace SmartPS.Services.GateControl;

/// <summary>Chuẩn hoá biển số về dạng chữ hoa + số để so khớp (bỏ dấu gạch, chấm, khoảng trắng).</summary>
public static class LicensePlateNormalizer
{
    public const int MinLength = 5;
    public const int MaxLength = 12;

    public static string Normalize(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return string.Empty;
        }

        var buffer = new char[plate.Length];
        var count = 0;
        foreach (var ch in plate)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                buffer[count++] = char.ToUpperInvariant(ch);
            }
        }

        return new string(buffer, 0, count);
    }

    public static bool IsValid(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            return false;
        }

        // Chữ hoặc số ngoài ASCII (toàn chiều rộng, Cyrillic...) làm biển số không hợp lệ thay vì âm thầm biến mất khi chuẩn hoá.
        // Ký tự cách/điều khiển/vô hình vẫn là dấu phân cách.
        foreach (var ch in plate)
        {
            if (!char.IsAscii(ch) && char.IsLetterOrDigit(ch))
            {
                return false;
            }
        }

        var length = Normalize(plate).Length;
        return length >= MinLength && length <= MaxLength;
    }
}
