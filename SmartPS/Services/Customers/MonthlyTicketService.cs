using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.GateControl;

namespace SmartPS.Services.Customers;

public sealed class MonthlyTicketService : IMonthlyTicketService
{
    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;
    private readonly ICurrentUserContext _currentUser;
    private readonly TimeProvider _time;

    public MonthlyTicketService(
        IDbContextFactory<SmartPsDbContext> contextFactory,
        IAuthorizationGuard guard,
        IAuditService audit,
        ICurrentUserContext currentUser,
        TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _time = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<MonthlyTicketPlan>> GetActivePlansAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.MonthlyTicketPlans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.VehicleTypeId)
            .ThenBy(p => p.DurationMonths)
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<int>> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "MonthlyTicket", null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult<int>.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var userId = _currentUser.User?.UserId;
        if (userId is null)
        {
            return OperationResult<int>.Fail(OperationError.PermissionDenied, "Cần đăng nhập để tạo vé tháng.");
        }

        var plate = LicensePlateNormalizer.Normalize(request.LicensePlate);

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId, cancellationToken);
                if (customer is null)
                {
                    return OperationResult<int>.Fail(OperationError.NotFound, "Không tìm thấy khách hàng.");
                }

                if (!customer.IsActive)
                {
                    return OperationResult<int>.Fail(OperationError.CustomerInactive, "Khách hàng đang bị khoá.");
                }

                var plan = await db.MonthlyTicketPlans.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PlanId == request.PlanId && p.IsActive, cancellationToken);
                if (plan is null)
                {
                    return OperationResult<int>.Fail(OperationError.PlanNotFound, "Không tìm thấy gói vé tháng.");
                }

                var vehicle = await db.CustomerVehicles.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.CustomerId == request.CustomerId && v.LicensePlate == plate && v.IsActive, cancellationToken);
                if (vehicle is null)
                {
                    return OperationResult<int>.Fail(OperationError.PlateNotOwnedByCustomer, "Biển số không thuộc khách hàng này.");
                }

                if (plan.VehicleTypeId != vehicle.VehicleTypeId)
                {
                    return OperationResult<int>.Fail(OperationError.PlanVehicleTypeMismatch, "Gói vé không dành cho loại phương tiện này.");
                }

                var now = UtcNow;
                var startVn = request.StartDateVn ?? TicketDates.TodayVn(now);
                var (start, end) = TicketDates.ForNewTicket(startVn, plan.DurationMonths);
                if (await HasOverlapAsync(db, plate, start, end, null, cancellationToken))
                {
                    return OperationResult<int>.Fail(OperationError.TicketOverlap, "Biển số đã có vé tháng trùng thời gian.");
                }

                var ticket = new MonthlyTicket
                {
                    TicketCode = await NextTicketCodeAsync(db, now, cancellationToken),
                    CustomerId = customer.CustomerId,
                    RegisteredLicensePlate = plate,
                    PlanId = plan.PlanId,
                    VehicleTypeId = vehicle.VehicleTypeId,
                    StartDate = start,
                    EndDate = end,
                    MonthlyPrice = plan.TotalPrice,
                    Status = MonthlyTicketStatus.Active,
                    Notes = NullIfBlank(request.Notes),
                    CreatedAt = now
                };
                db.MonthlyTickets.Add(ticket);
                await db.SaveChangesAsync(cancellationToken);

                var purchase = NewPurchase(ticket, TicketPurchaseKind.Create, plan, start, end, userId.Value, now);
                db.MonthlyTicketPurchases.Add(purchase);
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.TicketCreate,
                    AuditOutcome.Success,
                    "MonthlyTicket",
                    ticket.TicketId.ToString(),
                    new
                    {
                        TicketCode = ticket.TicketCode,
                        CustomerId = ticket.CustomerId,
                        LicensePlate = plate,
                        PlanId = plan.PlanId,
                        StartDate = start,
                        EndDate = end,
                        Price = plan.TotalPrice,
                        PurchaseId = purchase.MonthlyTicketPurchaseId
                    }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult<int>.Ok(ticket.TicketId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> RenewTicketAsync(int ticketId, int planId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "MonthlyTicket", ticketId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var userId = _currentUser.User?.UserId;
        if (userId is null)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, "Cần đăng nhập để gia hạn vé tháng.");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken);
                if (ticket is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy vé tháng.");
                }

                if (ticket.Status == MonthlyTicketStatus.Suspended)
                {
                    return OperationResult.Fail(OperationError.TicketSuspended, "Vé đang tạm ngưng, hãy kích hoạt lại trước khi gia hạn.");
                }

                var plan = await db.MonthlyTicketPlans.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PlanId == planId && p.IsActive, cancellationToken);
                if (plan is null)
                {
                    return OperationResult.Fail(OperationError.PlanNotFound, "Không tìm thấy gói vé tháng.");
                }

                if (plan.VehicleTypeId != ticket.VehicleTypeId)
                {
                    return OperationResult.Fail(OperationError.PlanVehicleTypeMismatch, "Gói vé không dành cho loại phương tiện này.");
                }

                var failure = await CheckCustomerAndPlateAsync(db, ticket, cancellationToken);
                if (failure is not null)
                {
                    return failure;
                }

                var now = UtcNow;
                var wasExpired = ticket.Status == MonthlyTicketStatus.Expired || ticket.EndDate <= now;
                var (newStart, newEnd) = TicketDates.ForRenewal(ticket.StartDate, ticket.EndDate, ticket.Status, plan.DurationMonths, now);
                if (await HasOverlapAsync(db, ticket.RegisteredLicensePlate, newStart, newEnd, ticket.TicketId, cancellationToken))
                {
                    return OperationResult.Fail(OperationError.TicketOverlap, "Biển số đã có vé tháng trùng thời gian.");
                }

                var before = new { StartDate = ticket.StartDate, EndDate = ticket.EndDate, Status = ticket.Status.ToString() };
                var (periodStart, periodEnd) = TicketDates.RenewalPurchasePeriod(ticket.EndDate, wasExpired, newStart, newEnd);

                ticket.StartDate = newStart;
                ticket.EndDate = newEnd;
                ticket.Status = MonthlyTicketStatus.Active;
                ticket.PlanId = plan.PlanId;
                ticket.MonthlyPrice = plan.TotalPrice;

                var purchase = NewPurchase(ticket, TicketPurchaseKind.Renew, plan, periodStart, periodEnd, userId.Value, now);
                db.MonthlyTicketPurchases.Add(purchase);
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.TicketRenew,
                    AuditOutcome.Success,
                    "MonthlyTicket",
                    ticket.TicketId.ToString(),
                    new
                    {
                        TicketCode = ticket.TicketCode,
                        PlanId = plan.PlanId,
                        Price = plan.TotalPrice,
                        PurchaseId = purchase.MonthlyTicketPurchaseId,
                        Before = before,
                        After = new { StartDate = newStart, EndDate = newEnd }
                    }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> SuspendTicketAsync(int ticketId, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "MonthlyTicket", ticketId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken);
                if (ticket is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy vé tháng.");
                }

                if (ticket.Status != MonthlyTicketStatus.Active)
                {
                    return OperationResult.Fail(OperationError.InvalidTicketState, "Chỉ có thể tạm ngưng vé đang hoạt động.");
                }

                ticket.Status = MonthlyTicketStatus.Suspended;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.TicketSuspend,
                    AuditOutcome.Success,
                    "MonthlyTicket",
                    ticket.TicketId.ToString(),
                    new { TicketCode = ticket.TicketCode, Reason = AuditPii.MaskPhoneNumbersInText(NullIfBlank(reason)) }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> ResumeTicketAsync(int ticketId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "MonthlyTicket", ticketId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken);
                if (ticket is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy vé tháng.");
                }

                if (ticket.Status != MonthlyTicketStatus.Suspended)
                {
                    return OperationResult.Fail(OperationError.InvalidTicketState, "Chỉ có thể kích hoạt lại vé đang tạm ngưng.");
                }

                var failure = await CheckCustomerAndPlateAsync(db, ticket, cancellationToken);
                if (failure is not null)
                {
                    return failure;
                }

                if (await HasOverlapAsync(db, ticket.RegisteredLicensePlate, ticket.StartDate, ticket.EndDate, ticket.TicketId, cancellationToken))
                {
                    return OperationResult.Fail(OperationError.TicketOverlap, "Biển số đã có vé tháng trùng thời gian.");
                }

                ticket.Status = MonthlyTicketStatus.Active;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.TicketResume,
                    AuditOutcome.Success,
                    "MonthlyTicket",
                    ticket.TicketId.ToString(),
                    new { TicketCode = ticket.TicketCode }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>Khách phải đang hoạt động và biển số của vé phải là xe đang hoạt động của chính khách đó (M2).</summary>
    private static async Task<OperationResult?> CheckCustomerAndPlateAsync(SmartPsDbContext db, MonthlyTicket ticket, CancellationToken cancellationToken)
    {
        var active = await db.Customers.AsNoTracking()
            .Where(c => c.CustomerId == ticket.CustomerId)
            .Select(c => (bool?)c.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
        if (active != true)
        {
            return OperationResult.Fail(OperationError.CustomerInactive, "Khách hàng đang bị khoá.");
        }

        var owns = await db.CustomerVehicles.AsNoTracking()
            .AnyAsync(v => v.CustomerId == ticket.CustomerId && v.LicensePlate == ticket.RegisteredLicensePlate && v.IsActive, cancellationToken);
        if (!owns)
        {
            return OperationResult.Fail(OperationError.PlateNotOwnedByCustomer, "Biển số không thuộc khách hàng này.");
        }

        return null;
    }

    /// <summary>Hai vé Active của cùng biển số không được trùng thời gian; khoảng nửa mở nên hai vé liền kề không trùng.</summary>
    private static Task<bool> HasOverlapAsync(SmartPsDbContext db, string plate, DateTime start, DateTime end, int? excludeTicketId, CancellationToken cancellationToken)
        => db.MonthlyTickets.AsNoTracking().AnyAsync(t =>
            t.RegisteredLicensePlate == plate
            && t.Status == MonthlyTicketStatus.Active
            && (excludeTicketId == null || t.TicketId != excludeTicketId)
            && t.StartDate < end
            && start < t.EndDate, cancellationToken);

    /// <summary>MT-{yyyyMM giờ VN}-{số thứ tự 4 chữ số}; được gọi khi đang giữ khoá nhật ký nên không trùng mã.</summary>
    private static async Task<string> NextTicketCodeAsync(SmartPsDbContext db, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var today = TicketDates.TodayVn(nowUtc);
        var prefix = $"MT-{today.ToString("yyyyMM", CultureInfo.InvariantCulture)}-";
        // Lấy giá trị số lớn nhất của phần đuôi (không dùng thứ tự chuỗi: mã 5 chữ số xếp trước mã 4 chữ số lớn hơn)
        var codes = await db.MonthlyTickets.AsNoTracking()
            .Where(t => t.TicketCode.StartsWith(prefix))
            .Select(t => t.TicketCode)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var code in codes)
        {
            if (int.TryParse(code[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > max)
            {
                max = number;
            }
        }

        var next = max + 1;

        return prefix + next.ToString("D4", CultureInfo.InvariantCulture);
    }

    private static MonthlyTicketPurchase NewPurchase(
        MonthlyTicket ticket, TicketPurchaseKind kind, MonthlyTicketPlan plan, DateTime periodStart, DateTime periodEnd, int userId, DateTime nowUtc)
        => new()
        {
            TicketId = ticket.TicketId,
            Kind = kind,
            PlanId = plan.PlanId,
            Price = plan.TotalPrice,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = userId
        };

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
