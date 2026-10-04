using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Models.Payment;
using SmartPS.Services.GateControl;
using SmartPS.Services.Payment.PayOS;
using SmartPS.Services.Shifts;
using SmartPS.Models.Shifts;

namespace SmartPS.Services.Payment;

public class PaymentService : IPaymentService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbContextFactory;
    private readonly IPaymentGateway _gateway;
    private readonly IParkingFeeCalculator _feeCalculator;
    private readonly IGateControlService? _gateControlService;
    private readonly SemaphoreSlim _webhookLock = new(1, 1);

    public PaymentService(
        IDbContextFactory<SmartPsDbContext> dbContextFactory,
        IPaymentGateway gateway,
        IParkingFeeCalculator feeCalculator,
        IGateControlService? gateControlService = null)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _feeCalculator = feeCalculator ?? throw new ArgumentNullException(nameof(feeCalculator));
        _gateControlService = gateControlService;
    }

    public async Task<PaymentCreationResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var shiftReservation = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var shift = await ShiftAccounting.FindActiveShiftAsync(db, request.ActorUserId, cancellationToken);
        if (shift == null) return FailCreate("Bạn cần mở ca trực trước khi tạo thanh toán VietQR.");
        var session = await db.ParkingSessions
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.SessionId == request.SessionId, cancellationToken);

        if (session == null)
        {
            return FailCreate("Không tìm thấy phiên gửi xe.");
        }

        if (session.Status != SessionStatus.Active)
        {
            return FailCreate("Phiên gửi xe không còn hoạt động.");
        }

        var now = DateTime.UtcNow;
        var pricingRule = await db.PricingRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.VehicleTypeId == session.VehicleTypeId, cancellationToken);
        var fee = _feeCalculator.CalculateFee(session, pricingRule, now);

        if (fee.IsMonthlyTicket || fee.TotalFee <= 0)
        {
            return FailCreate("Vé tháng hoặc cước phí 0đ không tạo thanh toán VietQR.");
        }

        session.TotalFee = fee.TotalFee;
        if (session.TotalFee <= 0 || session.TotalFee != fee.TotalFee)
        {
            return FailCreate("Số tiền thanh toán không hợp lệ.");
        }

        var active = await db.Payments
            .Include(p => p.Transactions)
            .Where(p => p.SessionId == session.SessionId &&
                        (p.Status == PaymentStatus.Created || p.Status == PaymentStatus.Pending))
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (active != null)
        {
            if (active.ShiftId != shift.ShiftId || active.CheckoutUserId != request.ActorUserId)
                return FailCreate("Thanh toán VietQR đang chờ được tạo bởi ca trực khác.");
            var activeExpired = active.ExpiredAt.HasValue && active.ExpiredAt.Value <= now;
            if (activeExpired)
            {
                EnsureCanTransition(active.Status, PaymentStatus.Expired);
                active.Status = PaymentStatus.Expired;
                active.UpdatedAt = now;
                PaymentLog.Event("PaymentExpired", $"Payment #{active.PaymentId} đã hết hạn trước khi tạo lại QR.");
            }
            else if (active.Amount == fee.TotalFee)
            {
                var existingTx = active.Transactions.OrderByDescending(t => t.PaymentTransactionId).FirstOrDefault();
                if (existingTx != null && !string.IsNullOrWhiteSpace(existingTx.QrCodePayload))
                {
                    PaymentLog.Event("PaymentPending", $"Tái sử dụng payment #{active.PaymentId} cho session {session.SessionId}.");
                    return new PaymentCreationResult
                    {
                        Success = true,
                        Message = "Đang chờ thanh toán VietQR.",
                        PaymentId = active.PaymentId,
                        PaymentTransactionId = existingTx.PaymentTransactionId,
                        TransactionReference = existingTx.TransactionReference,
                        Amount = active.Amount,
                        Currency = active.Currency,
                        Status = active.Status,
                        QrCodePayload = existingTx.QrCodePayload,
                        QrImagePng = TransactionReferenceGenerator.CreateQrPng(existingTx.QrCodePayload),
                        CheckoutUrl = existingTx.CheckoutUrl,
                        Description = existingTx.TransactionReference
                    };
                }
                else
                {
                    EnsureCanTransition(active.Status, PaymentStatus.Cancelled);
                    active.Status = PaymentStatus.Cancelled;
                    active.UpdatedAt = now;
                }
            }
            else
            {
                EnsureCanTransition(active.Status, PaymentStatus.Cancelled);
                active.Status = PaymentStatus.Cancelled;
                active.UpdatedAt = now;
            }
        }

        var reference = TransactionReferenceGenerator.Create(session.SessionId);
        var orderCode = TransactionReferenceGenerator.CreateOrderCode();

        var payment = new Models.Payment.Payment
        {
            SessionId = session.SessionId,
            ShiftId = shift.ShiftId,
            CheckoutUserId = request.ActorUserId,
            Amount = fee.TotalFee,
            Currency = "VND",
            Status = PaymentStatus.Created,
            PaymentMethod = PaymentMethod.VietQR,
            Description = $"Thu phí gửi xe {session.LicensePlate} {reference}",
            CheckoutImagePath = request.CheckoutImagePath,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiredAt = now.AddMinutes(15)
        };

        var transaction = new PaymentTransaction
        {
            TransactionReference = reference,
            GatewayOrderCode = orderCode.ToString(),
            Amount = payment.Amount,
            Currency = "VND",
            Status = PaymentTransactionStatus.Created,
            CreatedAt = now,
            UpdatedAt = now
        };
        payment.Transactions.Add(transaction);

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        PaymentLog.Event("PaymentCreated", $"Payment #{payment.PaymentId} session {session.SessionId} amount {payment.Amount} ref {reference}");

        var attemptNumber = 1;
        var attempt = new PaymentAttempt
        {
            PaymentTransactionId = transaction.PaymentTransactionId,
            AttemptNumber = attemptNumber,
            Gateway = _gateway.ProviderName,
            Status = PaymentAttemptStatus.Started,
            CreatedAt = now
        };
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        await shiftReservation.CommitAsync(cancellationToken);

        PaymentGatewayCreateResult gatewayResult;
        try
        {
            gatewayResult = await _gateway.CreatePaymentAsync(new PaymentGatewayCreateRequest
            {
                TransactionReference = reference,
                OrderCode = orderCode,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Description = reference,
                BuyerName = session.LicensePlate
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            gatewayResult = new PaymentGatewayCreateResult
            {
                Success = false,
                ErrorCode = "GATEWAY_EXCEPTION",
                ErrorMessage = "Không thể kết nối cổng thanh toán PayOS.",
                SanitizedResponsePayload = PaymentPayloadSanitizer.Redact($"{{\"error\":\"{ex.Message}\"}}")
            };
            PaymentLog.Event("PaymentGatewayException", ex.Message);
        }

        attempt.RequestPayload = PaymentPayloadSanitizer.Redact(gatewayResult.SanitizedRequestPayload);
        attempt.ResponsePayload = PaymentPayloadSanitizer.Redact(gatewayResult.SanitizedResponsePayload);
        attempt.CompletedAt = DateTime.UtcNow;

        if (!gatewayResult.Success)
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.ErrorCode = gatewayResult.ErrorCode;
            attempt.ErrorMessage = gatewayResult.ErrorMessage;
            EnsureCanTransition(payment.Status, PaymentStatus.Failed);
            payment.Status = PaymentStatus.Failed;
            payment.UpdatedAt = DateTime.UtcNow;
            transaction.Status = PaymentTransactionStatus.Failed;
            transaction.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            PaymentLog.Event("PaymentFailed", $"Payment #{payment.PaymentId} {gatewayResult.ErrorMessage}");
            return FailCreate(gatewayResult.ErrorMessage ?? "Không tạo được yêu cầu thanh toán.");
        }

        attempt.Status = PaymentAttemptStatus.Succeeded;
        transaction.GatewayTransactionId = gatewayResult.GatewayTransactionId;
        transaction.GatewayOrderCode = gatewayResult.GatewayOrderCode ?? transaction.GatewayOrderCode;
        transaction.QrCodePayload = gatewayResult.QrCodePayload;
        transaction.CheckoutUrl = gatewayResult.CheckoutUrl;
        transaction.AccountNumber = gatewayResult.AccountNumber;
        transaction.AccountName = gatewayResult.AccountName;
        transaction.BankCode = gatewayResult.BankCode;
        transaction.Status = PaymentTransactionStatus.Pending;
        transaction.UpdatedAt = DateTime.UtcNow;
        EnsureCanTransition(payment.Status, PaymentStatus.Pending);
        payment.Status = PaymentStatus.Pending;
        payment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        PaymentLog.Event("PaymentPending", $"Payment #{payment.PaymentId} ref {reference} amount {payment.Amount}");

        return new PaymentCreationResult
        {
            Success = true,
            Message = "Đã tạo mã VietQR. Chờ xác nhận thanh toán từ cổng.",
            PaymentId = payment.PaymentId,
            PaymentTransactionId = transaction.PaymentTransactionId,
            TransactionReference = reference,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status,
            QrCodePayload = transaction.QrCodePayload,
            QrImagePng = TransactionReferenceGenerator.CreateQrPng(transaction.QrCodePayload),
            CheckoutUrl = transaction.CheckoutUrl,
            Description = reference
        };
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var payment = await db.Payments
            .Include(p => p.Session)
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        if (payment == null)
        {
            return new PaymentStatusResult { Success = false, Message = "Không tìm thấy thanh toán." };
        }

        if ((payment.Status == PaymentStatus.Created || payment.Status == PaymentStatus.Pending) &&
            payment.ExpiredAt.HasValue && payment.ExpiredAt.Value <= DateTime.UtcNow)
        {
            EnsureCanTransition(payment.Status, PaymentStatus.Expired);
            payment.Status = PaymentStatus.Expired;
            payment.UpdatedAt = DateTime.UtcNow;
            foreach (var pendingTx in payment.Transactions.Where(t =>
                         t.Status == PaymentTransactionStatus.Created || t.Status == PaymentTransactionStatus.Pending))
            {
                pendingTx.Status = PaymentTransactionStatus.Cancelled;
                pendingTx.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            PaymentLog.Event("PaymentExpired", $"Payment #{payment.PaymentId} đã hết hạn.");
        }

        var tx = payment.Transactions.OrderByDescending(t => t.PaymentTransactionId).FirstOrDefault();
        return new PaymentStatusResult
        {
            Success = true,
            PaymentId = payment.PaymentId,
            Status = payment.Status,
            TransactionReference = tx?.TransactionReference ?? string.Empty,
            Amount = payment.Amount,
            PaidAt = payment.PaidAt,
            CheckoutCompleted = payment.Session?.Status == SessionStatus.Completed,
            Message = payment.Status.ToString()
        };
    }

    public async Task<PaymentStatusResult> RefreshFromGatewayAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var payment = await db.Payments
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        if (payment == null)
        {
            return new PaymentStatusResult { Success = false, Message = "Không tìm thấy thanh toán." };
        }

        if ((payment.Status == PaymentStatus.Created || payment.Status == PaymentStatus.Pending) &&
            payment.ExpiredAt.HasValue && payment.ExpiredAt.Value <= DateTime.UtcNow)
        {
            EnsureCanTransition(payment.Status, PaymentStatus.Expired);
            payment.Status = PaymentStatus.Expired;
            payment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return await GetPaymentStatusAsync(paymentId, cancellationToken);
        }

        var tx = payment.Transactions.OrderByDescending(t => t.PaymentTransactionId).FirstOrDefault();
        if (tx == null)
        {
            return await GetPaymentStatusAsync(paymentId, cancellationToken);
        }

        var verify = await _gateway.VerifyPaymentAsync(new PaymentGatewayVerifyRequest
        {
            TransactionReference = tx.TransactionReference,
            ExpectedAmount = payment.Amount,
            GatewayTransactionId = tx.GatewayTransactionId,
            GatewayOrderCode = tx.GatewayOrderCode
        }, cancellationToken);

        if (verify.Success && verify.IsPaid)
        {
            var verifiedAmount = verify.Amount ?? tx.Amount;
            if (Math.Round(verifiedAmount, 0) != Math.Round(payment.Amount, 0))
            {
                PaymentLog.Event("PaymentAmountMismatch", $"Gateway verify amount {verifiedAmount} != {payment.Amount}");
                return await GetPaymentStatusAsync(paymentId, cancellationToken);
            }

            await using var dbTx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            try
            {
                var paid = await TryMarkPaidAndCheckoutAsync(db, payment, tx, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                if (paid)
                {
                    var session = await db.ParkingSessions.Include(s => s.Slot)
                        .FirstOrDefaultAsync(s => s.SessionId == payment.SessionId, cancellationToken);
                    if (session != null)
                    {
                        _gateControlService?.NotifySessionCompleted(session);
                    }
                    PaymentLog.Event("PaymentPaid", $"Payment #{payment.PaymentId} confirmed by gateway verify");
                }
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync(cancellationToken);
                PaymentLog.Event("PaymentProcessingError", ex.Message);
            }
        }

        return await GetPaymentStatusAsync(paymentId, cancellationToken);
    }

    public async Task<PaymentStatusResult> CancelPaymentAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var payment = await db.Payments
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        if (payment == null)
        {
            return new PaymentStatusResult { Success = false, Message = "Không tìm thấy thanh toán." };
        }

        try
        {
            EnsureCanTransition(payment.Status, PaymentStatus.Cancelled);
        }
        catch (InvalidOperationException ex)
        {
            return new PaymentStatusResult { Success = false, Message = ex.Message, Status = payment.Status, PaymentId = payment.PaymentId };
        }

        payment.Status = PaymentStatus.Cancelled;
        payment.UpdatedAt = DateTime.UtcNow;
        foreach (var tx in payment.Transactions.Where(t => t.Status == PaymentTransactionStatus.Pending || t.Status == PaymentTransactionStatus.Created))
        {
            tx.Status = PaymentTransactionStatus.Cancelled;
            tx.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetPaymentStatusAsync(paymentId, cancellationToken);
    }

    public async Task<PaymentStatusResult> ConfirmManualRefundAsync(
        int paymentId,
        string? reason = null,
        int actorUserId = 0,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var dbTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var activeShift = await ShiftAccounting.FindActiveShiftAsync(db, actorUserId, cancellationToken);
        if (activeShift == null)
            return new PaymentStatusResult { Success = false, PaymentId = paymentId, Message = "Bạn cần mở ca trực trước khi hoàn tiền." };
        var payment = await db.Payments
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        if (payment == null)
        {
            return new PaymentStatusResult
            {
                Success = false,
                PaymentId = paymentId,
                Message = "Không tìm thấy giao dịch thanh toán."
            };
        }

        if (payment.Status != PaymentStatus.Paid)
        {
            return new PaymentStatusResult
            {
                Success = false,
                PaymentId = payment.PaymentId,
                Status = payment.Status,
                Message = $"Chỉ giao dịch Paid mới được hoàn tiền. Trạng thái hiện tại: {payment.Status}."
            };
        }

        try
        {
            EnsureCanTransition(payment.Status, PaymentStatus.Refunded);
            payment.Status = PaymentStatus.Refunded;
            payment.UpdatedAt = DateTime.UtcNow;

            foreach (var tx in payment.Transactions.Where(t => t.Status == PaymentTransactionStatus.Completed))
            {
                tx.UpdatedAt = DateTime.UtcNow;
            }

            var txRef = payment.Transactions
                .OrderByDescending(t => t.PaymentTransactionId)
                .Select(t => t.TransactionReference)
                .FirstOrDefault() ?? $"PAYMENT-{payment.PaymentId}";

            if (await db.FinancialTransactions.AnyAsync(t =>
                t.Type == FinancialTransactionType.Refund && t.ReferenceCode == $"PAYMENT-{payment.PaymentId}", cancellationToken))
                throw new InvalidOperationException("Giao dịch này đã được ghi nhận hoàn tiền.");

            db.FinancialTransactions.Add(new FinancialTransaction
            {
                TransactionCode = ShiftAccounting.NewCode(),
                ShiftId = activeShift.ShiftId,
                ParkingSessionId = payment.SessionId,
                CreatedByUserId = actorUserId,
                Type = FinancialTransactionType.Refund,
                PaymentMethod = payment.PaymentMethod,
                Amount = -payment.Amount,
                ReferenceCode = $"PAYMENT-{payment.PaymentId}",
                Note = reason,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            PaymentLog.Event(
                "PaymentRefunded",
                $"Payment #{payment.PaymentId} ref {txRef} amount {payment.Amount}. Reason={reason ?? "Không ghi rõ lý do"}");

            return new PaymentStatusResult
            {
                Success = true,
                PaymentId = payment.PaymentId,
                Status = PaymentStatus.Refunded,
                Amount = payment.Amount,
                PaidAt = payment.PaidAt,
                TransactionReference = txRef,
                Message = "Đã ghi nhận hoàn tiền thủ công."
            };
        }
        catch (InvalidOperationException ex)
        {
            return new PaymentStatusResult
            {
                Success = false,
                PaymentId = payment.PaymentId,
                Status = PaymentStatus.Paid,
                Message = ex.Message
            };
        }
    }

    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string rawPayload,
        string? signature,
        string provider,
        CancellationToken cancellationToken = default)
    {
        await _webhookLock.WaitAsync(cancellationToken);
        try
        {
            return await ProcessWebhookInternalAsync(rawPayload, signature, provider, cancellationToken);
        }
        finally
        {
            _webhookLock.Release();
        }
    }

    private async Task<WebhookProcessResult> ProcessWebhookInternalAsync(
        string rawPayload,
        string? signature,
        string provider,
        CancellationToken cancellationToken = default)
    {
        PaymentLog.Event("PaymentWebhookReceived", $"Provider={provider} bytes={(rawPayload?.Length ?? 0)}");

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return new WebhookProcessResult { Accepted = false, HttpStatusCode = 400, Message = "Payload rỗng." };
        }

        if (!string.Equals(provider, _gateway.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookProcessResult { Accepted = false, HttpStatusCode = 400, Message = $"Provider không được hỗ trợ: {provider}." };
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var parsed = _gateway.ParseWebhook(rawPayload);
        var eventId = string.IsNullOrWhiteSpace(parsed.EventId)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawPayload)))
            : parsed.EventId;
        var webhookSignature = signature ?? parsed.Signature;

        var existing = await db.PaymentWebhooks
            .FirstOrDefaultAsync(w => w.Provider == provider && w.EventId == eventId, cancellationToken);
        if (existing?.IsProcessed == true)
        {
            existing.IsDuplicate = true;
            existing.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            PaymentLog.Event("WebhookDuplicate", $"EventId={eventId}");
            return new WebhookProcessResult
            {
                Accepted = true,
                IsDuplicate = true,
                HttpStatusCode = 200,
                Message = "Webhook trùng, bỏ qua.",
                PaymentPaid = false
            };
        }

        var webhook = existing ?? new PaymentWebhook
        {
            Provider = provider,
            EventId = eventId,
            TransactionReference = parsed.TransactionReference,
            Payload = rawPayload,
            Signature = webhookSignature,
            ReceivedAt = DateTime.UtcNow,
            IsValid = false,
            IsProcessed = false,
            IsDuplicate = false
        };

        if (existing == null)
        {
            db.PaymentWebhooks.Add(webhook);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                PaymentLog.Event("WebhookDuplicate", $"Unique constraint EventId={eventId}");
                return new WebhookProcessResult
                {
                    Accepted = true,
                    IsDuplicate = true,
                    HttpStatusCode = 200,
                    Message = "Webhook trùng (ràng buộc CSDL)."
                };
            }
        }

        var signatureOk = await _gateway.VerifyWebhookSignatureAsync(rawPayload, webhookSignature ?? string.Empty, cancellationToken);
        if (!signatureOk)
        {
            webhook.IsValid = false;
            webhook.ErrorMessage = "Chữ ký webhook không hợp lệ.";
            webhook.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            PaymentLog.Event("WebhookSignatureInvalid", $"EventId={eventId}");
            return new WebhookProcessResult
            {
                Accepted = false,
                HttpStatusCode = 400,
                Message = "Chữ ký webhook không hợp lệ."
            };
        }

        webhook.IsValid = true;

        if (!parsed.Success)
        {
            webhook.ErrorMessage = parsed.ErrorMessage ?? "Không phân tích được webhook.";
            webhook.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new WebhookProcessResult { Accepted = false, HttpStatusCode = 400, Message = webhook.ErrorMessage };
        }

        await using var dbTx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var paymentTx = await FindTransactionAsync(db, parsed, cancellationToken);
            if (paymentTx == null)
            {
                webhook.ErrorMessage = "Không tìm thấy giao dịch theo mã tham chiếu.";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("PaymentTransactionNotFound", $"ref={parsed.TransactionReference} order={parsed.GatewayOrderCode}");
                return new WebhookProcessResult
                {
                    Accepted = true,
                    HttpStatusCode = 200,
                    Message = webhook.ErrorMessage
                };
            }

            webhook.PaymentTransactionId = paymentTx.PaymentTransactionId;
            webhook.TransactionReference = paymentTx.TransactionReference;

            await db.Entry(paymentTx).Reference(t => t.Payment).LoadAsync(cancellationToken);
            var payment = paymentTx.Payment;
            if (payment == null)
            {
                webhook.ErrorMessage = "Giao dịch không gắn với payment.";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                return new WebhookProcessResult { Accepted = true, HttpStatusCode = 200, Message = webhook.ErrorMessage };
            }

            if (!string.Equals(parsed.TransactionReference?.Trim(), paymentTx.TransactionReference, StringComparison.OrdinalIgnoreCase))
            {
                webhook.ErrorMessage = $"Sai payment reference. Expected={paymentTx.TransactionReference}, Actual={parsed.TransactionReference ?? "<trống>"}";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("PaymentReferenceMismatch", webhook.ErrorMessage);
                return new WebhookProcessResult { Accepted = true, HttpStatusCode = 200, Message = webhook.ErrorMessage };
            }

            if ((payment.Status == PaymentStatus.Created || payment.Status == PaymentStatus.Pending) &&
                payment.ExpiredAt.HasValue && payment.ExpiredAt.Value <= DateTime.UtcNow)
            {
                EnsureCanTransition(payment.Status, PaymentStatus.Expired);
                payment.Status = PaymentStatus.Expired;
                payment.UpdatedAt = DateTime.UtcNow;
                paymentTx.Status = PaymentTransactionStatus.Cancelled;
                paymentTx.UpdatedAt = DateTime.UtcNow;
                webhook.ErrorMessage = "Payment đã hết hạn, không thể xác nhận thanh toán.";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("PaymentExpired", $"Webhook đến sau thời hạn payment #{payment.PaymentId}.");
                return new WebhookProcessResult { Accepted = true, HttpStatusCode = 200, Message = webhook.ErrorMessage };
            }

            if (payment.Status == PaymentStatus.Paid)
            {
                webhook.IsProcessed = true;
                webhook.IsDuplicate = true;
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("WebhookDuplicate", $"Payment #{payment.PaymentId} đã Paid.");
                return new WebhookProcessResult
                {
                    Accepted = true,
                    IsDuplicate = true,
                    PaymentPaid = true,
                    HttpStatusCode = 200,
                    Message = "Thanh toán đã được xử lý trước đó."
                };
            }

            if (!string.IsNullOrWhiteSpace(parsed.GatewayOrderCode) &&
                !string.Equals(parsed.GatewayOrderCode, paymentTx.GatewayOrderCode, StringComparison.Ordinal))
            {
                webhook.ErrorMessage = $"Sai GatewayOrderCode. Expected={paymentTx.GatewayOrderCode}, Actual={parsed.GatewayOrderCode}.";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("PaymentOrderCodeMismatch", webhook.ErrorMessage);
                return new WebhookProcessResult { Accepted = true, HttpStatusCode = 200, Message = webhook.ErrorMessage };
            }

            if (Math.Round(parsed.Amount, 0) != Math.Round(payment.Amount, 0) ||
                Math.Round(parsed.Amount, 0) != Math.Round(paymentTx.Amount, 0))
            {
                webhook.ErrorMessage = $"Số tiền không khớp. Expected={payment.Amount}, Actual={parsed.Amount}";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("PaymentAmountMismatch", webhook.ErrorMessage);
                return new WebhookProcessResult
                {
                    Accepted = true,
                    HttpStatusCode = 200,
                    Message = webhook.ErrorMessage
                };
            }

            if (!parsed.ProviderReportsSuccess)
            {
                webhook.ErrorMessage = "Nhà cung cấp báo thanh toán không thành công.";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                return new WebhookProcessResult { Accepted = true, HttpStatusCode = 200, Message = webhook.ErrorMessage };
            }

            var verifyResult = await _gateway.VerifyPaymentAsync(new PaymentGatewayVerifyRequest
            {
                TransactionReference = paymentTx.TransactionReference,
                ExpectedAmount = payment.Amount,
                GatewayTransactionId = parsed.GatewayTransactionId,
                GatewayOrderCode = parsed.GatewayOrderCode
            }, cancellationToken);

            if (!verifyResult.Success || !verifyResult.IsPaid ||
                !verifyResult.Amount.HasValue ||
                Math.Round(verifyResult.Amount.Value, 0) != Math.Round(payment.Amount, 0))
            {
                webhook.ErrorMessage = verifyResult.ErrorMessage ?? "Đối soát với cổng thanh toán không xác nhận giao dịch Paid hoặc sai số tiền.";
                webhook.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await dbTx.CommitAsync(cancellationToken);
                PaymentLog.Event("PaymentProviderVerificationFailed", webhook.ErrorMessage);
                return new WebhookProcessResult { Accepted = true, HttpStatusCode = 200, Message = webhook.ErrorMessage };
            }

            var checkoutCompleted = await TryMarkPaidAndCheckoutAsync(db, payment, paymentTx, cancellationToken);
            if (!string.IsNullOrWhiteSpace(parsed.GatewayTransactionId))
            {
                paymentTx.GatewayTransactionId = parsed.GatewayTransactionId;
            }

            webhook.IsProcessed = true;
            webhook.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await dbTx.CommitAsync(cancellationToken);

            var session = await db.ParkingSessions
                .Include(s => s.Slot)
                .FirstOrDefaultAsync(s => s.SessionId == payment.SessionId, cancellationToken);
            if (session != null)
            {
                _gateControlService?.NotifySessionCompleted(session);
            }

            PaymentLog.Event("PaymentPaid", $"Payment #{payment.PaymentId} ref {paymentTx.TransactionReference}");
            if (checkoutCompleted)
            {
                PaymentLog.Event("PaymentCheckoutCompleted", $"Session {payment.SessionId} completed after payment #{payment.PaymentId}");
            }

            return new WebhookProcessResult
            {
                Accepted = true,
                PaymentPaid = true,
                CheckoutCompleted = checkoutCompleted,
                HttpStatusCode = 200,
                Message = "Thanh toán hợp lệ, đã hoàn tất phiên gửi xe."
            };
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            try
            {
                var savedWebhook = await db.PaymentWebhooks.FindAsync(new object[] { webhook.PaymentWebhookId }, cancellationToken);
                if (savedWebhook != null)
                {
                    savedWebhook.ErrorMessage = "Lỗi xử lý webhook.";
                    savedWebhook.ProcessedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch
            {
                // ignore secondary save errors
            }

            PaymentLog.Event("PaymentProcessingError", ex.Message);
            return new WebhookProcessResult
            {
                Accepted = false,
                HttpStatusCode = 500,
                Message = "Lỗi xử lý webhook."
            };
        }
    }

    public async Task<List<PaymentHistoryItem>> GetPaymentHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Payments
            .AsNoTracking()
            .Include(p => p.Session)
            .Include(p => p.Transactions)
                .ThenInclude(t => t.Attempts)
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return rows.Select(ToHistoryItem).ToList();
    }

    public async Task<PaymentDetailsResult?> GetPaymentDetailsAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var payment = await db.Payments
            .AsNoTracking()
            .Include(p => p.Session)
            .Include(p => p.Transactions)
                .ThenInclude(t => t.Attempts)
            .Include(p => p.Transactions)
                .ThenInclude(t => t.Webhooks)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);

        if (payment == null)
        {
            return null;
        }

        var tx = payment.Transactions.OrderByDescending(t => t.PaymentTransactionId).FirstOrDefault();
        return new PaymentDetailsResult
        {
            Payment = ToHistoryItem(payment),
            Attempts = tx?.Attempts.OrderBy(a => a.AttemptNumber).ToList() ?? new List<PaymentAttempt>(),
            Webhooks = tx?.Webhooks.OrderByDescending(w => w.ReceivedAt).ToList() ?? new List<PaymentWebhook>(),
            QrCodePayload = tx?.QrCodePayload,
            GatewayTransactionId = tx?.GatewayTransactionId
        };
    }

    private static async Task<PaymentTransaction?> FindTransactionAsync(
        SmartPsDbContext db,
        PaymentGatewayWebhookParseResult parsed,
        CancellationToken cancellationToken)
    {
        PaymentTransaction? tx = null;

        if (!string.IsNullOrWhiteSpace(parsed.GatewayOrderCode))
        {
            tx = await db.PaymentTransactions
                .FirstOrDefaultAsync(t => t.GatewayOrderCode == parsed.GatewayOrderCode, cancellationToken);
        }

        if (tx == null && !string.IsNullOrWhiteSpace(parsed.GatewayTransactionId))
        {
            tx = await db.PaymentTransactions
                .FirstOrDefaultAsync(t => t.GatewayTransactionId == parsed.GatewayTransactionId, cancellationToken);
        }

        if (tx == null && !string.IsNullOrWhiteSpace(parsed.TransactionReference))
        {
            var refText = parsed.TransactionReference.Trim();
            tx = await db.PaymentTransactions
                .FirstOrDefaultAsync(t => t.TransactionReference == refText, cancellationToken);

            if (tx == null)
            {
                tx = await db.PaymentTransactions
                    .Where(t => t.Status == PaymentTransactionStatus.Pending || t.Status == PaymentTransactionStatus.Created)
                    .OrderByDescending(t => t.PaymentTransactionId)
                    .FirstOrDefaultAsync(t => refText.Contains(t.TransactionReference), cancellationToken);
            }
        }

        return tx;
    }

    private static async Task<bool> TryMarkPaidAndCheckoutAsync(
        SmartPsDbContext db,
        Models.Payment.Payment payment,
        PaymentTransaction paymentTx,
        CancellationToken cancellationToken)
    {
        EnsureCanTransition(payment.Status, PaymentStatus.Paid);

        var paidAt = DateTime.UtcNow;
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = paidAt;
        payment.UpdatedAt = paidAt;
        paymentTx.Status = PaymentTransactionStatus.Completed;
        paymentTx.CompletedAt = paidAt;
        paymentTx.UpdatedAt = paidAt;

        return await CompleteParkingSessionAsync(db, payment, cancellationToken);
    }

    private static async Task<bool> CompleteParkingSessionAsync(
        SmartPsDbContext db,
        Models.Payment.Payment payment,
        CancellationToken cancellationToken)
    {
        var session = await db.ParkingSessions
            .Include(s => s.Slot)
            .FirstOrDefaultAsync(s => s.SessionId == payment.SessionId, cancellationToken);

        if (session == null)
        {
            throw new InvalidOperationException("Không tìm thấy phiên gửi xe cho payment.");
        }

        if (session.Status == SessionStatus.Completed)
        {
            throw new InvalidOperationException("Phiên gửi xe đã được checkout trước đó.");
        }

        var shift = payment.ShiftId.HasValue && payment.CheckoutUserId.HasValue
            ? await ShiftAccounting.FindActiveShiftAsync(db, payment.CheckoutUserId.Value, cancellationToken)
            : null;
        if (shift == null || shift.ShiftId != payment.ShiftId)
            throw new InvalidOperationException("Ca trực của người tạo VietQR không còn Active.");
        var reference = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.PaymentId == payment.PaymentId)
            .OrderByDescending(t => t.PaymentTransactionId)
            .Select(t => t.TransactionReference)
            .FirstOrDefaultAsync(cancellationToken);
        await ShiftAccounting.AddParkingFeeAsync(db, shift, payment.CheckoutUserId!.Value,
            session.SessionId, PaymentMethod.VietQR, payment.Amount, reference, cancellationToken);

        session.CheckOutTime = DateTime.UtcNow;
        session.TotalFee = payment.Amount;
        session.PaymentMethod = PaymentMethod.VietQR;
        session.Status = SessionStatus.Completed;
        if (!string.IsNullOrWhiteSpace(payment.CheckoutImagePath))
        {
            session.CheckOutImagePath = payment.CheckoutImagePath;
        }

        if (session.Slot != null)
        {
            session.Slot.Status = SlotStatus.Available;
            session.Slot.CurrentLicensePlate = null;
        }
        else if (session.SlotId.HasValue)
        {
            var slot = await db.ParkingSlots.FindAsync(new object[] { session.SlotId.Value }, cancellationToken);
            if (slot != null)
            {
                slot.Status = SlotStatus.Available;
                slot.CurrentLicensePlate = null;
            }
        }

        return true;
    }

    private static PaymentHistoryItem ToHistoryItem(Models.Payment.Payment payment)
    {
        var tx = payment.Transactions?.OrderByDescending(t => t.PaymentTransactionId).FirstOrDefault();
        return new PaymentHistoryItem
        {
            PaymentId = payment.PaymentId,
            SessionId = payment.SessionId,
            TransactionReference = tx?.TransactionReference ?? string.Empty,
            LicensePlate = payment.Session?.LicensePlate ?? string.Empty,
            TicketCode = payment.Session?.TicketCode ?? string.Empty,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            Status = payment.Status,
            CreatedAt = payment.CreatedAt,
            PaidAt = payment.PaidAt,
            Gateway = tx?.Attempts?.LastOrDefault()?.Gateway ?? PayOSPaymentGateway.Provider
        };
    }

    private static void EnsureCanTransition(PaymentStatus from, PaymentStatus to)
    {
        if (from == to) return;

        var allowed = (from, to) switch
        {
            (PaymentStatus.Created, PaymentStatus.Pending) => true,
            (PaymentStatus.Created, PaymentStatus.Cancelled) => true,
            (PaymentStatus.Created, PaymentStatus.Failed) => true,
            (PaymentStatus.Created, PaymentStatus.Expired) => true,
            (PaymentStatus.Pending, PaymentStatus.Paid) => true,
            (PaymentStatus.Pending, PaymentStatus.Failed) => true,
            (PaymentStatus.Pending, PaymentStatus.Cancelled) => true,
            (PaymentStatus.Pending, PaymentStatus.Expired) => true,
            (PaymentStatus.Paid, PaymentStatus.Refunded) => true,
            _ => false
        };

        if (!allowed)
            throw new InvalidOperationException($"Không cho phép chuyển trạng thái thanh toán từ {from} sang {to}.");
    }

    private static PaymentCreationResult FailCreate(string message)
        => new() { Success = false, Message = message };
}
