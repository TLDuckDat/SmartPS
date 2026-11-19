using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SmartPS.Services.Common;

/// <summary>
/// Mã chứng từ dễ đọc, cùng kiểu với mã biên bản sự cố (SC-2026-001):
/// <list type="bullet">
/// <item>Vé lượt:          VL-yyMMdd-0001  (số thứ tự trong ngày, giờ Việt Nam)</item>
/// <item>Giao dịch ca:     GD-yyMMdd-0001</item>
/// <item>Ca trực:          CA-yyMMdd-01</item>
/// <item>Vé tháng:         VT-yyyy-0001    (số thứ tự trong năm)</item>
/// </list>
/// Số thứ tự = số lớn nhất đã cấp với cùng tiền tố + 1. Phải gọi bên trong giao dịch có khoá nhật ký
/// (<c>BeginAuditedTransactionAsync</c> giữ khoá ứng dụng của SQL Server suốt giao dịch) để hai máy trạm
/// không cấp trùng số.
/// </summary>
public static class DocumentCodes
{
    public const string WalkInTicket = "VL";
    public const string FinancialTransaction = "GD";
    public const string Shift = "CA";
    public const string MonthlyTicket = "VT";

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static DateTime ToVietnam(DateTime utc)
    {
        var u = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
        return u + VietnamOffset;
    }

    /// <summary>Tiền tố theo ngày, ví dụ "VL-261119-".</summary>
    public static string DailyPrefix(string kind, DateTime utc)
        => $"{kind}-{ToVietnam(utc).ToString("yyMMdd", CultureInfo.InvariantCulture)}-";

    /// <summary>Tiền tố theo năm, ví dụ "VT-2026-".</summary>
    public static string YearlyPrefix(string kind, DateTime utc)
        => $"{kind}-{ToVietnam(utc).Year.ToString(CultureInfo.InvariantCulture)}-";

    public static string Format(string prefix, int number, int digits)
        => prefix + number.ToString(new string('0', digits), CultureInfo.InvariantCulture);

    /// <summary>Số tiếp theo từ các mã đã có (bỏ qua mã không đúng dạng số, ví dụ mã ngoại tuyến).</summary>
    public static int NextNumber(string prefix, IEnumerable<string> existingCodes)
    {
        var max = 0;
        foreach (var code in existingCodes)
        {
            if (code is null || !code.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max)
            {
                max = n;
            }
        }

        return max + 1;
    }

    /// <summary>Đọc các mã cùng tiền tố từ CSDL và trả về mã tiếp theo.</summary>
    public static async Task<string> NextAsync(IQueryable<string> codes, string prefix, int digits, CancellationToken cancellationToken)
    {
        var existing = await codes.Where(c => c.StartsWith(prefix)).ToListAsync(cancellationToken);
        return Format(prefix, NextNumber(prefix, existing), digits);
    }

    /// <summary>Mã vé lượt khi mất kết nối CSDL: không có số thứ tự nên dùng chữ N + đoạn ngẫu nhiên.</summary>
    public static string OfflineWalkInTicket(DateTime utc)
        => DailyPrefix(WalkInTicket, utc) + "N" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
}
