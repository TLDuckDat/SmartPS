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
        var length = Normalize(plate).Length;
        return length >= MinLength && length <= MaxLength;
    }
}
