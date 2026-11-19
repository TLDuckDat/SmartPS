using Microsoft.EntityFrameworkCore;
using SmartPS.Services.Common;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Mã chứng từ cấp trong giao dịch có khoá nhật ký: CA-yyMMdd-NN khi mở ca, VL-yyMMdd-NNNN khi xe vào,
/// GD-yyMMdd-NNNN khi thu phí; số thứ tự tăng dần, không trùng.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class DocumentCodeFlowTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public DocumentCodeFlowTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    [Fact]
    public async Task Shift_ticket_and_transaction_codes_are_daily_sequences()
    {
        _db.RequireAvailable();
        var op1 = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var op2 = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);

        await sp.LoginAsync(op1.Username);
        var shift1 = await ParkingFlows.OpenShiftAsync(sp, op1.UserId);
        await sp.LoginAsync(op2.Username);
        var shift2 = await ParkingFlows.OpenShiftAsync(sp, op2.UserId);

        var shiftPrefix = DocumentCodes.DailyPrefix(DocumentCodes.Shift, shift1.OpenedAt);
        Assert.Matches(@"^CA-\d{6}-\d{2,}$", shift1.ShiftCode!);
        Assert.StartsWith(shiftPrefix, shift1.ShiftCode!, StringComparison.Ordinal);
        Assert.True(string.CompareOrdinal(shift2.ShiftCode, shift1.ShiftCode) > 0, $"{shift2.ShiftCode} <= {shift1.ShiftCode}");

        var plateA = ParkingFlows.UniquePlate();
        var plateB = ParkingFlows.UniquePlate();
        var inA = await ParkingFlows.CheckInAsync(sp, plateA);
        var inB = await ParkingFlows.CheckInAsync(sp, plateB);
        Assert.Matches(@"^VL-\d{6}-\d{4,}$", inA.Session!.TicketCode);
        Assert.Matches(@"^VL-\d{6}-\d{4,}$", inB.Session!.TicketCode);
        Assert.NotEqual(inA.Session.TicketCode, inB.Session.TicketCode);

        var (_, outA) = await ParkingFlows.CheckOutCashAsync(sp, plateA, op2.UserId);
        Assert.True(outA.Success, outA.Message);

        await using var ctx = _db.CreateContext();
        var fee = await ctx.FinancialTransactions.AsNoTracking()
            .SingleAsync(t => t.ParkingSessionId == inA.Session.SessionId);
        Assert.Matches(@"^GD-\d{6}-\d{4,}$", fee.TransactionCode);
    }
}
