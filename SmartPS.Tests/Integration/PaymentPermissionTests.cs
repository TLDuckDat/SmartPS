using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Payment;
using SmartPS.Services.Payment;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Spec §6.4 (M4) VietQR cancel rule, R5 Payment.Refund, m8 result contracts (IsPermissionDenied), A2, T-ACTOR, R14 payment events.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class PaymentPermissionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public PaymentPermissionTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static IPaymentService Payments(IServiceProvider sp) => sp.GetRequiredService<IPaymentService>();

    private async Task<PaymentStatus> StatusAsync(int paymentId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.Payments.Where(p => p.PaymentId == paymentId).Select(p => p.Status).SingleAsync();
    }

    /// <summary>Operator creates a VietQR payment and the (mock) gateway confirms it by webhook.</summary>
    private async Task<(int PaymentId, int SessionId)> PaidPaymentAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var (sessionId, paymentId, _) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
        var webhook = await ParkingFlows.SendPaidWebhookAsync(sp, paymentId);
        Assert.True(webhook.PaymentPaid, webhook.Message);
        Assert.Equal(PaymentStatus.Paid, await StatusAsync(paymentId));
        return (paymentId, sessionId);
    }

    [Fact]
    public async Task M4_operator_cancels_own_pending_vietqr_with_PAYMENT_CANCEL()
    {
        // Spec §6.4: cancelling one's own Pending VietQR needs only Parking.CheckOut.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var (_, paymentId, _) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
        Assert.Equal(PaymentStatus.Pending, await StatusAsync(paymentId));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Payments(sp).CancelPaymentAsync(paymentId);

        Assert.True(result.Success, result.Message);
        Assert.False(result.IsPermissionDenied);
        Assert.Equal(PaymentStatus.Cancelled, await StatusAsync(paymentId));
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PaymentCancel));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal(op.UserId, row.UserId);
        AuditDb.HasKeys(row, "paymentId", "amount", "previousStatus", "byOwner");
        var details = AuditDb.Details(row);
        Assert.Equal(paymentId, details.GetProperty("paymentId").GetInt32());
        Assert.Equal("Pending", AuditDb.String(details, "previousStatus"));
        Assert.True(details.GetProperty("byOwner").GetBoolean());
    }

    [Fact]
    public async Task M4_operator_cannot_cancel_another_users_payment()
    {
        _db.RequireAvailable();
        var owner = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var intruder = await TestUsers.CreateAsync(_db.Factory, "Operator");
        int paymentId;
        using (var ownerSession = IntegrationServices.Create(_db))
        {
            await ownerSession.LoginAsync(owner.Username);
            (_, paymentId, _) = await ParkingFlows.PendingVietQrAsync(ownerSession, owner.UserId);
        }

        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(intruder.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Payments(sp).CancelPaymentAsync(paymentId);

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal(PaymentStatus.Pending, await StatusAsync(paymentId));
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(intruder.UserId, denied.UserId);
        Assert.Equal(new[] { Permissions.PaymentRefund }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task M4_user_with_Payment_Refund_can_cancel_someone_elses_pending_payment()
    {
        _db.RequireAvailable();
        var owner = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        int paymentId;
        using (var ownerSession = IntegrationServices.Create(_db))
        {
            await ownerSession.LoginAsync(owner.Username);
            (_, paymentId, _) = await ParkingFlows.PendingVietQrAsync(ownerSession, owner.UserId);
        }

        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Payments(sp).CancelPaymentAsync(paymentId);

        Assert.True(result.Success, result.Message);
        Assert.Equal(PaymentStatus.Cancelled, await StatusAsync(paymentId));
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PaymentCancel));
        Assert.Equal(manager.UserId, row.UserId);
        Assert.False(AuditDb.Details(row).GetProperty("byOwner").GetBoolean());
    }

    [Fact]
    public async Task Operator_refund_is_denied_and_payment_stays_paid()
    {
        // R5: hoàn tiền requires Payment.Refund; result contract returns IsPermissionDenied (m8).
        _db.RequireAvailable();
        var (paymentId, _) = await PaidPaymentAsync();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Payments(sp).ConfirmManualRefundAsync(paymentId, "test", op.UserId);

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal(PaymentStatus.Paid, await StatusAsync(paymentId));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PaymentRefund));
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
        Assert.Equal(new[] { Permissions.PaymentRefund }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task Manager_refund_succeeds_with_PAYMENT_REFUND_row()
    {
        _db.RequireAvailable();
        var (paymentId, sessionId) = await PaidPaymentAsync();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        await ParkingFlows.OpenShiftAsync(sp, manager.UserId);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Payments(sp).ConfirmManualRefundAsync(paymentId, "khách yêu cầu", manager.UserId);

        Assert.True(result.Success, result.Message);
        Assert.Equal(PaymentStatus.Refunded, await StatusAsync(paymentId));
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PaymentRefund));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal(manager.UserId, row.UserId);
        AuditDb.HasKeys(row, "paymentId", "sessionId", "amount", "paymentMethod", "reason", "transactionReference");
        var details = AuditDb.Details(row);
        Assert.Equal(paymentId, details.GetProperty("paymentId").GetInt32());
        Assert.Equal(sessionId, details.GetProperty("sessionId").GetInt32());
        Assert.Equal("khách yêu cầu", AuditDb.String(details, "reason"));
    }

    [Fact]
    public async Task Refund_with_mismatched_actor_is_denied()
    {
        _db.RequireAvailable();
        var (paymentId, _) = await PaidPaymentAsync();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var other = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Payments(sp).ConfirmManualRefundAsync(paymentId, "x", other.UserId);

        Assert.True(result.IsPermissionDenied);
        Assert.Equal(PaymentStatus.Paid, await StatusAsync(paymentId));
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal("ActorMismatch", AuditDb.String(AuditDb.Details(denied), "reason"));
    }

    [Fact]
    public async Task A2_create_payment_is_guarded_by_actor_and_writes_no_audit_on_success()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var other = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        var checkIn = await ParkingFlows.CheckInAsync(sp, ParkingFlows.UniquePlate());

        var idBeforeDenied = await AuditDb.MaxIdAsync(_db.Factory);
        var denied = await ParkingFlows.CreateVietQrAsync(sp, checkIn.Session!.SessionId, other.UserId);
        Assert.False(denied.Success);
        Assert.True(denied.IsPermissionDenied);
        var deniedRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBeforeDenied));
        Assert.Equal(AuditActions.AccessDenied, deniedRow.Action);

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var created = await ParkingFlows.CreateVietQrAsync(sp, checkIn.Session.SessionId, op.UserId);
        Assert.True(created.Success, created.Message);
        Assert.False(created.IsPermissionDenied);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
    }

    [Fact]
    public async Task FX8_unexpected_audit_failure_during_refund_is_not_reported_as_a_business_rejection()
    {
        // FX8 (F6): the refund catch only covers EnsureCanTransition and the duplicate-refund check;
        // an audit append failure (an InvalidOperationException too) must propagate, and nothing is persisted.
        _db.RequireAvailable();
        var (paymentId, _) = await PaidPaymentAsync();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db, IntegrationServices.FailAppendFor(AuditActions.PaymentRefund));
        await sp.LoginAsync(manager.Username);
        await ParkingFlows.OpenShiftAsync(sp, manager.UserId);

        var ex = await Record.ExceptionAsync(() => Payments(sp).ConfirmManualRefundAsync(paymentId, "x", manager.UserId));

        Assert.NotNull(ex);
        var chain = ex;
        var found = false;
        while (chain is not null)
        {
            found |= chain.Message.Contains("Simulated audit append failure", StringComparison.Ordinal);
            chain = chain.InnerException;
        }

        Assert.True(found, $"expected the audit failure to surface, got {ex}");
        Assert.Equal(1, sp.GetRequiredService<ThrowingAuditServiceDecorator>().FailuresThrown);
        Assert.Equal(PaymentStatus.Paid, await StatusAsync(paymentId));
        await using var ctx = _db.CreateContext();
        Assert.False(await ctx.FinancialTransactions.AnyAsync(t =>
            t.Type == SmartPS.Models.Shifts.FinancialTransactionType.Refund && t.ReferenceCode == $"PAYMENT-{paymentId}"));
    }
}
