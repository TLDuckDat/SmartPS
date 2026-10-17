using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Data;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.GateControl;
using SmartPS.Services.Payment;
using SmartPS.Services.Payment.Mock;
using SmartPS.Services.Shifts;

namespace SmartPS.Tests.TestSupport;

/// <summary>Business flows used to produce audit events in integration tests.</summary>
public static class ParkingFlows
{
    public static string UniquePlate() => "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static async Task<int> MotorbikeTypeIdAsync(IDbContextFactory<SmartPsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.VehicleTypes.Where(v => v.TypeName == "Xe máy").Select(v => v.VehicleTypeId).SingleAsync();
    }

    public static async Task<Shift> OpenShiftAsync(IServiceProvider sp, int userId, decimal beginningCash = 100_000m)
        => await sp.GetRequiredService<IShiftService>().OpenShiftAsync(userId, beginningCash);

    public static async Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate)
    {
        var typeId = await MotorbikeTypeIdAsync(sp.DbFactory());
        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest
        {
            LicensePlate = plate,
            VehicleTypeId = typeId
        });
        Assert.True(result.Success, $"check-in failed: {result.Message}");
        Assert.NotNull(result.Session);
        Assert.True(result.Session!.SessionId > 0);
        return result;
    }

    public static async Task<(GateCheckOutRequest Request, GateCheckOutResult Result)> CheckOutCashAsync(
        IServiceProvider sp, string plate, int actorUserId)
    {
        var gate = sp.GetRequiredService<IGateControlService>();
        var calc = await gate.CalculateCheckOutAsync(plate);
        Assert.True(calc.Success, $"checkout calculation failed: {calc.Message}");
        var request = new GateCheckOutRequest
        {
            SessionId = calc.ActiveSession!.SessionId,
            ActorUserId = actorUserId,
            PaymentMethod = PaymentMethod.Cash,
            TotalFee = calc.TotalFee
        };
        var result = await gate.CompleteCheckOutAsync(request);
        return (request, result);
    }

    /// <summary>
    /// Creates a VietQR payment, making sure its gateway order code cannot collide with another transaction of the
    /// same test database.
    /// <para>
    /// Root cause of the rare "Sai payment reference" failures: <c>TransactionReferenceGenerator.CreateOrderCode()</c>
    /// returns <c>unixSeconds * 1000 + random(100..999)</c>, i.e. only 900 distinct codes per second. Two payments created
    /// in the same second can get the same code, and the webhook looks the transaction up by GatewayOrderCode
    /// (FirstOrDefault), so it may pick the other payment. Production code is not changed here; the fixture waits until
    /// the wall-clock second is later than the second encoded in every existing order code of the database, so codes of
    /// this database never share a second, then asserts the new code is unique.
    /// </para>
    /// </summary>
    public static async Task<PaymentCreationResult> CreateVietQrAsync(IServiceProvider sp, int sessionId, int actorUserId)
    {
        var lastSecond = await LastOrderCodeSecondAsync(sp.DbFactory());
        for (var waited = 0; DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= lastSecond && waited < 3000; waited += 50)
        {
            await Task.Delay(50);
        }

        var result = await sp.GetRequiredService<IPaymentService>().CreatePaymentAsync(new CreatePaymentRequest
        {
            SessionId = sessionId,
            ActorUserId = actorUserId
        });

        if (result.Success)
        {
            await using var db = await sp.DbFactory().CreateDbContextAsync();
            var codes = await db.PaymentTransactions.AsNoTracking()
                .Where(t => t.PaymentId == result.PaymentId)
                .Select(t => t.GatewayOrderCode)
                .ToListAsync();
            foreach (var code in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var sameCode = await db.PaymentTransactions.AsNoTracking().CountAsync(t => t.GatewayOrderCode == code);
                Assert.True(sameCode == 1, $"gateway order code {code} is used by {sameCode} transactions");
            }
        }

        return result;
    }

    /// <summary>The unix second encoded in the newest gateway order code of the database (0 when there is none).</summary>
    private static async Task<long> LastOrderCodeSecondAsync(IDbContextFactory<SmartPsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var codes = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.GatewayOrderCode != null)
            .Select(t => t.GatewayOrderCode!)
            .ToListAsync();
        return codes
            .Select(c => long.TryParse(c, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v / 1000 : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>Builds a correctly signed "paid" webhook payload for the latest transaction of the payment.</summary>
    public static async Task<string> PaidWebhookPayloadAsync(IServiceProvider sp, int paymentId, MockPaymentGateway gateway)
    {
        await using var db = await sp.DbFactory().CreateDbContextAsync();
        var tx = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.PaymentId == paymentId)
            .OrderByDescending(t => t.PaymentTransactionId)
            .FirstAsync();
        Assert.False(string.IsNullOrWhiteSpace(tx.GatewayOrderCode));
        return gateway.CreateSignedWebhookPayload(
            long.Parse(tx.GatewayOrderCode!, System.Globalization.CultureInfo.InvariantCulture),
            tx.Amount,
            tx.TransactionReference);
    }

    public static async Task<WebhookProcessResult> SendPaidWebhookAsync(IServiceProvider sp, int paymentId, MockPaymentGateway? gateway = null)
    {
        gateway ??= sp.GetRequiredService<MockPaymentGateway>();
        var payload = await PaidWebhookPayloadAsync(sp, paymentId, gateway);
        return await sp.GetRequiredService<IPaymentService>().ProcessWebhookAsync(payload, null, MockPaymentGateway.MockProvider);
    }

    /// <summary>Operator flow: (open shift if needed) → check-in → VietQR payment (Pending).</summary>
    public static async Task<(int SessionId, int PaymentId, string Plate)> PendingVietQrAsync(IServiceProvider sp, int operatorUserId)
    {
        if (await sp.GetRequiredService<IShiftService>().GetActiveShiftAsync(operatorUserId) is null)
        {
            await OpenShiftAsync(sp, operatorUserId);
        }

        var plate = UniquePlate();
        var checkIn = await CheckInAsync(sp, plate);
        var payment = await CreateVietQrAsync(sp, checkIn.Session!.SessionId, operatorUserId);
        Assert.True(payment.Success, $"payment creation failed: {payment.Message}");
        return (checkIn.Session.SessionId, payment.PaymentId, plate);
    }
}
