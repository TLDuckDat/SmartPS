using System.Globalization;

namespace SmartPS.Services.ParkingZones;

/// <summary>
/// Sinh mã ô đỗ hàng loạt: tiền tố + số thứ tự (ít nhất 2 chữ số), ví dụ "C-" → C-31, C-32...; "R-M" → R-M05.
/// Dùng chung cho phần xem trước trên giao diện và cho dịch vụ khi lưu.
/// </summary>
public static class SlotCodeGenerator
{
    public const int MaxSlotsPerBatch = 100;
    public const int MaxPrefixLength = 10;
    public const int MaxCodeLength = 20;

    /// <summary>
    /// Tiền tố gợi ý cho khu: lấy phần chữ đầu của một mã ô đã có (ưu tiên ô cùng loại xe),
    /// nếu khu chưa có ô thì lấy mã khu bỏ "ZONE_" và thêm dấu gạch, ví dụ ZONE_C → "C-".
    /// </summary>
    public static string SuggestPrefix(string? zoneCode, IEnumerable<string> existingCodesInZone)
    {
        foreach (var code in existingCodesInZone)
        {
            var prefix = SplitPrefix(code);
            if (prefix.Length > 0 && prefix.Length < code.Length)
            {
                return prefix;
            }
        }

        var core = (zoneCode ?? string.Empty).Trim().ToUpperInvariant();
        if (core.StartsWith("ZONE_", StringComparison.Ordinal))
        {
            core = core["ZONE_".Length..];
        }

        core = new string(core.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (core.Length == 0)
        {
            core = "S";
        }

        return (core.Length > MaxPrefixLength - 1 ? core[..(MaxPrefixLength - 1)] : core) + "-";
    }

    /// <summary>Chuẩn hoá tiền tố người dùng nhập: bỏ khoảng trắng, viết hoa.</summary>
    public static string NormalizePrefix(string? prefix)
        => new string((prefix ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    /// <summary>Tiền tố hợp lệ: 1-10 ký tự chữ/số ASCII hoặc dấu gạch, không kết thúc bằng chữ số.</summary>
    public static bool IsValidPrefix(string prefix)
        => prefix.Length is > 0 and <= MaxPrefixLength
           && prefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
           && !char.IsDigit(prefix[^1]);

    /// <summary>Phần đứng trước dãy chữ số cuối cùng, ví dụ "R-M05" → "R-M", "A-01" → "A-".</summary>
    public static string SplitPrefix(string code)
    {
        var i = code.Length;
        while (i > 0 && char.IsDigit(code[i - 1]))
        {
            i--;
        }

        return code[..i];
    }

    /// <summary>Các mã kế tiếp với tiền tố, nối tiếp số lớn nhất đã có; số chữ số theo mã đã có (ít nhất 2).</summary>
    public static IReadOnlyList<string> NextCodes(string prefix, IEnumerable<string> existingCodes, int count)
    {
        var max = 0;
        var width = 2;
        foreach (var code in existingCodes)
        {
            if (!code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var digits = code[prefix.Length..];
            if (digits.Length > 0 && digits.All(char.IsDigit)
                && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                max = Math.Max(max, n);
                width = Math.Max(width, digits.Length);
            }
        }

        var format = new string('0', width);
        return Enumerable.Range(max + 1, Math.Max(0, count))
            .Select(n => prefix + n.ToString(format, CultureInfo.InvariantCulture))
            .ToList();
    }
}
