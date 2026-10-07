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

    public static async Task<PaymentCreationResult> CreateVietQrAsync(IServiceProvider sp, int sessionId, int actorUserId)
        => await sp.GetRequiredService<IPaymentService>().CreatePaymentAsync(new CreatePaymentRequest
        {
            SessionId = sessionId,
            ActorUserId = actorUserId
        });

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
