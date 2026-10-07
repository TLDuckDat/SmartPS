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

public sealed class BlacklistService : IBlacklistService
{
    private const int MaxReasonLength = 500;

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;
    private readonly ICurrentUserContext _currentUser;
    private readonly TimeProvider _time;

    public BlacklistService(
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

    public async Task<IReadOnlyList<BlacklistEntryDto>> GetEntriesAsync(BlacklistQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _guard.DemandAsync(Permissions.CustomerView, "BlacklistEntry", null, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<BlacklistEntry> entries = db.BlacklistEntries.AsNoTracking();
        if (!query.IncludeInactive)
        {
            entries = entries.Where(e => e.IsActive);
        }

        var fragment = LicensePlateNormalizer.Normalize(query.SearchText);
        if (fragment.Length > 0)
        {
            entries = entries.Where(e => e.LicensePlate.Contains(fragment));
        }

        return await entries
            .OrderByDescending(e => e.IsActive)
            .ThenByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.BlacklistEntryId)
            .Select(e => new BlacklistEntryDto(
                e.BlacklistEntryId,
                e.LicensePlate,
                e.Reason,
                e.CreatedAt,
                e.CreatedByUserId,
                e.CreatedByUser == null ? null : e.CreatedByUser.Username,
                e.IsActive,
                e.RemovedAt,
                e.RemovedByUserId,
                e.RemovedByUser == null ? null : e.RemovedByUser.Username,
                e.RemoveReason))
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<int>> AddAsync(string licensePlate, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.BlacklistManage, "BlacklistEntry", null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult<int>.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var cleanReason = reason?.Trim() ?? string.Empty;
        if (cleanReason.Length == 0 || cleanReason.Length > MaxReasonLength)
        {
            return OperationResult<int>.Fail(OperationError.ReasonRequired, "Cần nhập lý do (tối đa 500 ký tự).");
        }

        var plate = LicensePlateNormalizer.Normalize(licensePlate);
        if (!LicensePlateNormalizer.IsValid(plate))
        {
            return OperationResult<int>.Fail(OperationError.PlateInvalid, "Biển số không hợp lệ.");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                if (await db.BlacklistEntries.AnyAsync(e => e.IsActive && e.LicensePlate == plate, cancellationToken))
                {
                    return OperationResult<int>.Fail(OperationError.BlacklistAlreadyActive, "Biển số đã nằm trong danh sách đen.");
                }

                var entry = new BlacklistEntry
                {
                    LicensePlate = plate,
                    Reason = cleanReason,
                    CreatedAt = UtcNow,
                    CreatedByUserId = _currentUser.User?.UserId,
                    IsActive = true
                };
                db.BlacklistEntries.Add(entry);
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.BlacklistAdd,
                    AuditOutcome.Success,
                    "BlacklistEntry",
                    entry.BlacklistEntryId.ToString(),
                    new { LicensePlate = plate, Reason = cleanReason }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult<int>.Ok(entry.BlacklistEntryId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return OperationResult<int>.Fail(OperationError.BlacklistAlreadyActive, "Biển số đã nằm trong danh sách đen.");
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> RemoveAsync(int blacklistEntryId, string removeReason, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.BlacklistManage, "BlacklistEntry", blacklistEntryId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var cleanReason = removeReason?.Trim() ?? string.Empty;
        if (cleanReason.Length == 0 || cleanReason.Length > MaxReasonLength)
        {
            return OperationResult.Fail(OperationError.ReasonRequired, "Cần nhập lý do gỡ (tối đa 500 ký tự).");
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var entry = await db.BlacklistEntries.FirstOrDefaultAsync(e => e.BlacklistEntryId == blacklistEntryId, cancellationToken);
                if (entry is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy mục danh sách đen.");
                }

                if (!entry.IsActive)
                {
                    return OperationResult.Fail(OperationError.BlacklistNotActive, "Mục này đã được gỡ trước đó.");
                }

                entry.IsActive = false;
                entry.RemovedAt = UtcNow;
                entry.RemovedByUserId = _currentUser.User?.UserId;
                entry.RemoveReason = cleanReason;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.BlacklistRemove,
                    AuditOutcome.Success,
                    "BlacklistEntry",
                    entry.BlacklistEntryId.ToString(),
                    new { LicensePlate = entry.LicensePlate, RemoveReason = cleanReason }), cancellationToken);
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
}
