using SmartPS.Services.Common;

namespace SmartPS.Tests.Unit;

/// <summary>Mã chứng từ dễ đọc: VL/GD/CA theo ngày giờ Việt Nam, VT theo năm; số thứ tự nối tiếp.</summary>
public class DocumentCodesTests
{
    [Fact]
    public void Daily_prefix_uses_vietnam_date()
    {
        // 18/11 lúc 23:30 UTC đã là 19/11 06:30 giờ Việt Nam
        var utc = new DateTime(2026, 11, 18, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal("VL-261119-", DocumentCodes.DailyPrefix(DocumentCodes.WalkInTicket, utc));
        Assert.Equal("GD-261119-", DocumentCodes.DailyPrefix(DocumentCodes.FinancialTransaction, utc));
        Assert.Equal("CA-261119-", DocumentCodes.DailyPrefix(DocumentCodes.Shift, utc));
    }

    [Fact]
    public void Yearly_prefix_uses_vietnam_year()
    {
        var utc = new DateTime(2026, 12, 31, 18, 0, 0, DateTimeKind.Utc); // 01/01/2027 01:00 giờ VN

        Assert.Equal("VT-2027-", DocumentCodes.YearlyPrefix(DocumentCodes.MonthlyTicket, utc));
    }

    [Fact]
    public void Next_number_continues_after_the_largest_and_ignores_other_shapes()
    {
        var existing = new[] { "VL-261119-0007", "VL-261119-0012", "VL-261119-N1A2B", "VL-261118-0099", "TK-20261119-1001-ABCD" };

        Assert.Equal(13, DocumentCodes.NextNumber("VL-261119-", existing));
        Assert.Equal(1, DocumentCodes.NextNumber("VL-261120-", existing));
    }

    [Fact]
    public void Format_pads_and_still_grows_past_the_padding()
    {
        Assert.Equal("CA-261119-03", DocumentCodes.Format("CA-261119-", 3, 2));
        Assert.Equal("CA-261119-104", DocumentCodes.Format("CA-261119-", 104, 2));
        Assert.Equal("GD-261119-0042", DocumentCodes.Format("GD-261119-", 42, 4));
    }

    [Fact]
    public void Offline_ticket_is_marked_and_not_counted_as_a_sequence_number()
    {
        var utc = new DateTime(2026, 11, 19, 3, 0, 0, DateTimeKind.Utc);
        var code = DocumentCodes.OfflineWalkInTicket(utc);

        Assert.Matches(@"^VL-261119-N[0-9A-F]{4}$", code);
        Assert.Equal(1, DocumentCodes.NextNumber("VL-261119-", new[] { code }));
    }
}
