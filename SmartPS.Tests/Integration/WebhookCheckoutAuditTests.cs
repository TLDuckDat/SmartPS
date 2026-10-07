using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Payment;
using SmartPS.Services.Payment;
using SmartPS.Services.Payment.Mock;

namespace SmartPS.Tests.Integration;

/// <summary>
/// T-WEBHOOK (A3): VietQR completion by webhook / gateway verify writes PARKING_CHECKOUT with the checkout user as actor.
/// T-IO (M1 / rule R-IO): no gateway HTTP call happens while the audit lock is held.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class WebhookCheckoutAuditTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public WebhookCheckoutAuditTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    /// <summary>Delegates to the mock gateway and records any call made while the audit lock is held in the calling flow.</summary>
    private sealed class LockAssertingGateway : IPaymentGateway
    {
        private readonly MockPaymentGateway _inner;

        public LockAssertingGateway(MockPaymentGateway inner) => _inner = inner;

        public ConcurrentQueue<string> Violations { get; } = new();

        public int VerifyCalls;

        public string ProviderName => _inner.ProviderName;

        private void Check(string call)
        {
            if (AuditService.IsLockHeldInCurrentFlow)
            {
                Violations.Enqueue(call);
            }
        }

        public Task<PaymentGatewayCreateResult> CreatePaymentAsync(PaymentGatewayCreateRequest request, CancellationToken cancellationToken = default)
        {
            Check(nameof(CreatePaymentAsync));
            return _inner.CreatePaymentAsync(request, cancellationToken);
        }

        public Task<PaymentGatewayVerifyResult> VerifyPaymentAsync(PaymentGatewayVerifyRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref VerifyCalls);
            Check(nameof(VerifyPaymentAsync));
            return _inner.VerifyPaymentAsync(request, cancellationToken);
        }

        public Task<bool> VerifyWebhookSignatureAsync(string payload, string signature, CancellationToken cancellationToken = default)
        {
            Check(nameof(VerifyWebhookSignatureAsync));
            return _inner.VerifyWebhookSignatureAsync(payload, signature, cancellationToken);
        }

        public PaymentGatewayWebhookParseResult ParseWebhook(string payload) => _inner.ParseWebhook(payload);
    }

    private ServiceProvider CreateWithLockAssertingGateway(out LockAssertingGateway gateway)
    {
        LockAssertingGateway? created = null;
        var sp = IntegrationServices.Create(_db, services =>
        {
            services.AddSingleton<LockAssertingGateway>(p => created = new LockAssertingGateway(p.GetRequiredService<MockPaymentGateway>()));
            services.AddSingleton<IPaymentGateway>(p => p.GetRequiredService<LockAssertingGateway>());
        });
        gateway = sp.GetRequiredService<LockAssertingGateway>();
        Assert.Same(created, gateway);
        return sp;
    }

    [Fact]
    public async Task TWEBHOOK_paid_webhook_writes_one_PARKING_CHECKOUT_attributed_to_the_checkout_user()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = CreateWithLockAssertingGateway(out var gateway);
        await sp.LoginAsync(op.Username);
        var (sessionId, paymentId, plate) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
        await sp.Auth().LogoutAsync(); // webhook arrives without an interactive user (system actor, A3)
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var mock = sp.GetRequiredService<MockPaymentGateway>();
        var payload = await ParkingFlows.PaidWebhookPayloadAsync(sp, paymentId, mock);

        var result = await sp.GetRequiredService<IPaymentService>().ProcessWebhookAsync(payload, null, MockPaymentGateway.MockProvider);

        Assert.True(result.Accepted, result.Message);
        Assert.True(result.PaymentPaid);
        Assert.True(result.CheckoutCompleted);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("ParkingSession", row.EntityType);
        Assert.Equal(sessionId.ToString(), row.EntityId);
        Assert.Equal(op.UserId, row.UserId);
        Assert.Equal(op.Username, row.Username);
        Assert.Equal("Operator", row.RoleName);
        AuditDb.HasKeys(row, "sessionId", "licensePlate", "ticketCode", "fee", "paymentMethod", "paymentId", "source");
        var details = AuditDb.Details(row);
        Assert.Equal("VietQR", AuditDb.String(details, "paymentMethod"));
        Assert.Equal("webhook", AuditDb.String(details, "source"));
        Assert.Equal(plate, AuditDb.String(details, "licensePlate"));
        Assert.Equal(paymentId, details.GetProperty("paymentId").GetInt32());

        // A duplicate delivery does not produce a second checkout row.
        var duplicate = await sp.GetRequiredService<IPaymentService>().ProcessWebhookAsync(payload, null, MockPaymentGateway.MockProvider);
        Assert.True(duplicate.IsDuplicate);
        Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));

        // T-IO: the gateway verify ran, and never while the audit lock was held.
        Assert.True(gateway.VerifyCalls > 0);
        Assert.Empty(gateway.Violations);
    }

    [Fact]
    public async Task Gateway_verify_completion_writes_PARKING_CHECKOUT_with_source_gatewayVerify()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = CreateWithLockAssertingGateway(out var gateway);
        await sp.LoginAsync(op.Username);
        var (sessionId, paymentId, _) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var status = await sp.GetRequiredService<IPaymentService>().RefreshFromGatewayAsync(paymentId);

        Assert.Equal(PaymentStatus.Paid, status.Status);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Equal(sessionId.ToString(), row.EntityId);
        Assert.Equal(op.UserId, row.UserId);
        Assert.Equal("gatewayVerify", AuditDb.String(AuditDb.Details(row), "source"));
        Assert.True(gateway.VerifyCalls > 0);
        Assert.Empty(gateway.Violations);
    }

    [Fact]
    public async Task Webhook_with_wrong_amount_does_not_complete_checkout_or_audit_it()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = CreateWithLockAssertingGateway(out var gateway);
        await sp.LoginAsync(op.Username);
        var (_, paymentId, _) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var mock = sp.GetRequiredService<MockPaymentGateway>();
        await using var ctx = _db.CreateContext();
        var tx = ctx.PaymentTransactions.Where(t => t.PaymentId == paymentId).OrderByDescending(t => t.PaymentTransactionId).First();
        var payload = mock.CreateSignedWebhookPayload(long.Parse(tx.GatewayOrderCode!), tx.Amount + 1000m, tx.TransactionReference);

        var result = await sp.GetRequiredService<IPaymentService>().ProcessWebhookAsync(payload, null, MockPaymentGateway.MockProvider);

        Assert.False(result.PaymentPaid);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Empty(gateway.Violations);
    }
}
