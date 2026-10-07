namespace SmartPS.Services.GateControl;

/// <summary>Một kỳ vé tháng đã thanh toán, khoảng nửa mở [StartUtc, EndUtc).</summary>
public sealed record CoveragePeriod(DateTime StartUtc, DateTime EndUtc);

/// <summary>Kết quả tra cứu hiệu lực vé tháng của một xe; ValidUntilUtc null nghĩa là không có kỳ nào bao trùm giờ vào.</summary>
public sealed record MonthlyCoverage(DateTime? ValidUntilUtc);

public enum MonthlyChargeReason
{
    NotMonthly = 0,
    CoveredAtExit = 1,
    ExpiredDuringStay = 2,
    NotCovered = 3,
    CoverageUnknown = 4
}

public readonly record struct MonthlyChargeWindow(bool IsFree, DateTime? ChargeFromUtc, MonthlyChargeReason Reason, DateTime? TicketValidUntilUtc);

public static class MonthlyCoverageCalculator
{
    /// <summary>
    /// Tìm kỳ chứa giờ vào (Start &lt;= checkIn &lt; End) rồi nối tiếp khi kỳ kế (theo Start) có Start &lt;= End hiện tại.
    /// Khoảng trống chấm dứt chuỗi. Trả về null khi không có kỳ nào chứa giờ vào.
    /// </summary>
    public static DateTime? ContiguousValidUntil(IEnumerable<CoveragePeriod> periods, DateTime checkInUtc)
    {
        var ordered = periods.OrderBy(p => p.StartUtc).ThenBy(p => p.EndUtc).ToList();

        DateTime? chainEnd = null;
        foreach (var period in ordered)
        {
            if (chainEnd is null)
            {
                if (period.StartUtc <= checkInUtc && checkInUtc < period.EndUtc)
                {
                    chainEnd = period.EndUtc;
                }

                continue;
            }

            if (period.StartUtc > chainEnd.Value)
            {
                break;
            }

            if (period.EndUtc > chainEnd.Value)
            {
                chainEnd = period.EndUtc;
            }
        }

        return chainEnd;
    }
}

/// <summary>Quyết định phần nào của lượt gửi xe vé tháng được miễn phí và phần nào tính theo giá vãng lai (R15).</summary>
public static class MonthlyFeeSplitPolicy
{
    public static MonthlyChargeWindow Resolve(DateTime checkInUtc, DateTime checkOutUtc, bool isMonthlyPass, MonthlyCoverage? coverage)
    {
        if (!isMonthlyPass)
        {
            return new MonthlyChargeWindow(false, null, MonthlyChargeReason.NotMonthly, null);
        }

        if (coverage is null)
        {
            // Không tra cứu được (ngoại tuyến): giữ hành vi cũ, miễn phí.
            return new MonthlyChargeWindow(true, null, MonthlyChargeReason.CoverageUnknown, null);
        }

        var validUntil = coverage.ValidUntilUtc;
        if (validUntil is null)
        {
            return new MonthlyChargeWindow(false, checkInUtc, MonthlyChargeReason.NotCovered, null);
        }

        if (validUntil.Value > checkOutUtc)
        {
            return new MonthlyChargeWindow(true, null, MonthlyChargeReason.CoveredAtExit, validUntil);
        }

        if (validUntil.Value > checkInUtc)
        {
            return new MonthlyChargeWindow(false, validUntil, MonthlyChargeReason.ExpiredDuringStay, validUntil);
        }

        return new MonthlyChargeWindow(false, checkInUtc, MonthlyChargeReason.NotCovered, validUntil);
    }
}
