using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.DTOs.Shifts;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Models.Payment;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Shifts;

public class ShiftService : IShiftService
{
    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;

    public ShiftService(IDbContextFactory<SmartPsDbContext> contextFactory, IAuthorizationGuard guard, IAuditService audit)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<Shift?> GetActiveShiftAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return null;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Shifts
            .AsNoTracking()
            .Include(x => x.OpenedByUser)
            .FirstOrDefaultAsync(x => x.OpenedByUserId == userId && x.Status == ShiftStatus.Active, cancellationToken);
    }

    public async Task<Shift> OpenShiftAsync(int userId, decimal beginningCash, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ArgumentException("Tài khoản mở ca không hợp lệ.", nameof(userId));
        if (beginningCash < 0)
            throw new ArgumentOutOfRangeException(nameof(beginningCash), "Beginning Cash không được âm.");

        await _guard.DemandActorAsync(userId, Permissions.ShiftOpen, "Shift", null, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy tài khoản mở ca.");

        if (!user.IsActive)
            throw new InvalidOperationException("Tài khoản đã bị khóa nên không thể mở ca.");

        var hasActiveShift = await db.Shifts
            .AnyAsync(x => x.OpenedByUserId == userId && x.Status == ShiftStatus.Active, cancellationToken);

        if (hasActiveShift)
            throw new InvalidOperationException("Tài khoản hiện đã có một ca đang hoạt động.");

        var shift = new Shift
        {
            OpenedByUserId = userId,
            OpenedAt = DateTime.UtcNow,
            BeginningCash = beginningCash,
            Status = ShiftStatus.Active
        };

        db.Shifts.Add(shift);

        try
        {
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                await db.SaveChangesAsync(cancellationToken);
                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.ShiftOpen,
                    AuditOutcome.Success,
                    "Shift",
                    shift.ShiftId.ToString(),
                    new { BeginningCash = beginningCash }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("Không thể mở ca. Tài khoản có thể đã có một ca Active khác.", ex);
        }

        return shift;
    }

    public async Task<ShiftDashboardData> GetShiftDashboardAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var shift = await db.Shifts
            .AsNoTracking()
            .Include(x => x.OpenedByUser)
            .Include(x => x.ReviewedByUser)
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy ca trực.");

        var transactions = await db.FinancialTransactions
            .AsNoTracking()
            .Include(x => x.ParkingSession)
            .Include(x => x.CreatedByUser)
            .Where(x => x.ShiftId == shiftId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return ShiftFinancialCalculator.BuildDashboard(shift, transactions);
    }

    public async Task<Shift> CloseShiftAsync(int shiftId, int actorUserId, decimal actualCash, CancellationToken cancellationToken = default)
    {
        if (actualCash < 0)
            throw new ArgumentOutOfRangeException(nameof(actualCash), "Actual Cash không được âm.");

        await _guard.DemandActorAsync(actorUserId, Permissions.ShiftClose, "Shift", shiftId.ToString(), cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using (var dbTransaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            var shift = await db.Shifts
                .FirstOrDefaultAsync(x => x.ShiftId == shiftId, cancellationToken)
                ?? throw new KeyNotFoundException("Không tìm thấy ca trực.");

            if (shift.Status != ShiftStatus.Active)
                throw new InvalidOperationException("Chỉ ca Active mới có thể đóng.");
            if (shift.OpenedByUserId != actorUserId)
                throw new UnauthorizedAccessException("Chỉ người mở ca mới được đóng ca này.");
            if (await db.Payments.AnyAsync(p => p.ShiftId == shiftId &&
                (p.Status == PaymentStatus.Created || p.Status == PaymentStatus.Pending), cancellationToken))
                throw new InvalidOperationException("Ca còn thanh toán VietQR đang chờ. Hãy hoàn tất hoặc hủy thanh toán trước khi đóng ca.");

            var transactions = await db.FinancialTransactions
                .AsNoTracking()
                .Where(x => x.ShiftId == shiftId)
                .ToListAsync(cancellationToken);

            var dashboard = ShiftFinancialCalculator.BuildDashboard(shift, transactions);

            shift.ExpectedCash = dashboard.ExpectedCash;
            shift.ActualCash = actualCash;
            shift.Difference = actualCash - dashboard.ExpectedCash;
            shift.ClosedAt = DateTime.UtcNow;
            shift.Status = ShiftStatus.Locked;

            await _audit.AppendAsync(db, new AuditEntry(
                AuditActions.ShiftClose,
                AuditOutcome.Success,
                "Shift",
                shiftId.ToString(),
                new
                {
                    ExpectedCash = shift.ExpectedCash,
                    ActualCash = actualCash,
                    Difference = shift.Difference
                }), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            return shift;
        }
    }

    public async Task<List<Shift>> GetShiftHistoryAsync(ShiftFilterRequest filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Shifts
            .AsNoTracking()
            .Include(x => x.OpenedByUser)
            .Include(x => x.ReviewedByUser)
            .AsQueryable();

        if (filter.UserId.HasValue)
            query = query.Where(x => x.OpenedByUserId == filter.UserId.Value);
        if (filter.OpenedFromUtc.HasValue)
            query = query.Where(x => x.OpenedAt >= filter.OpenedFromUtc.Value);
        if (filter.OpenedToUtc.HasValue)
            query = query.Where(x => x.OpenedAt < filter.OpenedToUtc.Value);
        if (filter.Status.HasValue)
            query = query.Where(x => x.Status == filter.Status.Value);

        var limit = Math.Clamp(filter.Limit, 1, 1000);
        return await query
            .OrderByDescending(x => x.OpenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Shift?> GetShiftDetailAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Shifts
            .AsNoTracking()
            .Include(x => x.OpenedByUser)
            .Include(x => x.ReviewedByUser)
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId, cancellationToken);
    }

    public async Task<List<FinancialTransaction>> GetTransactionsAsync(FinancialTransactionFilterRequest? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new FinancialTransactionFilterRequest();

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.FinancialTransactions
            .AsNoTracking()
            .Include(x => x.Shift)
                .ThenInclude(x => x!.OpenedByUser)
            .Include(x => x.ParkingSession)
                .ThenInclude(x => x!.VehicleType)
            .Include(x => x.CreatedByUser)
            .AsQueryable();

        if (filter.ShiftId.HasValue)
            query = query.Where(x => x.ShiftId == filter.ShiftId.Value);
        if (filter.CreatedByUserId.HasValue)
            query = query.Where(x => x.CreatedByUserId == filter.CreatedByUserId.Value);
        if (filter.PaymentMethod.HasValue)
            query = query.Where(x => x.PaymentMethod == filter.PaymentMethod.Value);
        if (filter.Type.HasValue)
            query = query.Where(x => x.Type == filter.Type.Value);
        if (filter.FromUtc.HasValue)
            query = query.Where(x => x.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)
            query = query.Where(x => x.CreatedAt < filter.ToUtc.Value);

        var limit = Math.Clamp(filter.Limit, 1, 2000);
        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancialTransaction> CreateManualTransactionAsync(
        int shiftId,
        int createdByUserId,
        FinancialTransactionType type,
        PaymentMethod paymentMethod,
        decimal amount,
        string? referenceCode,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (type == FinancialTransactionType.ParkingFee)
            throw new InvalidOperationException("ParkingFee phải được tạo tự động từ luồng checkout.");
        if (createdByUserId <= 0)
            throw new ArgumentException("Người tạo giao dịch không hợp lệ.", nameof(createdByUserId));

        await _guard.DemandActorAsync(createdByUserId, Permissions.ShiftAdjust, "FinancialTransaction", null, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using (var dbTransaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
        {
            var shift = await db.Shifts.FirstOrDefaultAsync(x => x.ShiftId == shiftId, cancellationToken)
                ?? throw new KeyNotFoundException("Không tìm thấy ca trực.");

            if (shift.Status == ShiftStatus.Reviewed)
                throw new InvalidOperationException("Ca đã được xác nhận nên không thể phát sinh giao dịch mới.");

            if (shift.Status == ShiftStatus.Locked && type != FinancialTransactionType.Adjustment)
                throw new InvalidOperationException("Ca đã khóa chỉ cho phép tạo Adjustment, không sửa giao dịch cũ.");

            if (type == FinancialTransactionType.Refund)
                amount = -Math.Abs(amount);
            else if (type == FinancialTransactionType.Adjustment && amount == 0)
                throw new ArgumentException("Adjustment phải có số tiền khác 0.", nameof(amount));
            else if (type == FinancialTransactionType.Incident && amount == 0)
                throw new ArgumentException("Incident transaction phải có số tiền khác 0.", nameof(amount));

            if (paymentMethod == PaymentMethod.Free)
                amount = 0;

            var transaction = new FinancialTransaction
            {
                TransactionCode = CreateTransactionCode(),
                ShiftId = shiftId,
                CreatedByUserId = createdByUserId,
                Type = type,
                PaymentMethod = paymentMethod,
                Amount = amount,
                CreatedAt = DateTime.UtcNow,
                ReferenceCode = string.IsNullOrWhiteSpace(referenceCode) ? null : referenceCode.Trim(),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };

            db.FinancialTransactions.Add(transaction);
            await db.SaveChangesAsync(cancellationToken);

            if (shift.Status == ShiftStatus.Locked)
            {
                var transactions = await db.FinancialTransactions
                    .AsNoTracking()
                    .Where(x => x.ShiftId == shiftId)
                    .ToListAsync(cancellationToken);

                var dashboard = ShiftFinancialCalculator.BuildDashboard(shift, transactions);
                shift.ExpectedCash = dashboard.ExpectedCash;
                shift.Difference = shift.ActualCash.HasValue
                    ? shift.ActualCash.Value - dashboard.ExpectedCash
                    : null;

                await db.SaveChangesAsync(cancellationToken);
            }

            await _audit.AppendAsync(db, new AuditEntry(
                AuditActions.ShiftAdjustment,
                AuditOutcome.Success,
                "FinancialTransaction",
                transaction.TransactionId.ToString(),
                new
                {
                    ShiftId = shiftId,
                    Type = type.ToString(),
                    PaymentMethod = paymentMethod.ToString(),
                    Amount = amount,
                    TransactionCode = transaction.TransactionCode
                }), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return transaction;
        }
    }

    public async Task<Shift> ReviewShiftAsync(int shiftId, int managerUserId, string? note, CancellationToken cancellationToken = default)
    {
        await _guard.DemandActorAsync(managerUserId, Permissions.ShiftReview, "Shift", shiftId.ToString(), cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
        {
            var shift = await db.Shifts.FirstOrDefaultAsync(x => x.ShiftId == shiftId, cancellationToken)
                ?? throw new KeyNotFoundException("Không tìm thấy ca trực.");

            if (shift.Status != ShiftStatus.Locked)
                throw new InvalidOperationException("Chỉ ca Locked mới có thể được Manager Review.");

            shift.ReviewedByUserId = managerUserId;
            shift.ReviewedAt = DateTime.UtcNow;
            shift.ManagerNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            shift.Status = ShiftStatus.Reviewed;

            await _audit.AppendAsync(db, new AuditEntry(
                AuditActions.ShiftReview,
                AuditOutcome.Success,
                "Shift",
                shiftId.ToString(),
                new { Note = shift.ManagerNote }), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return shift;
        }
    }

    private static string CreateTransactionCode()
        => $"TX-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..45].ToUpperInvariant();
}
