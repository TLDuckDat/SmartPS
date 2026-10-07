using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Audit;

public sealed class AuditIntegrityVerifier : IAuditIntegrityVerifier
{
    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;

    public AuditIntegrityVerifier(
        IDbContextFactory<SmartPsDbContext> contextFactory,
        IAuthorizationGuard guard,
        IAuditService audit)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<AuditVerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await _guard.DemandAsync(Permissions.AuditVerify, "AuditLog", null, cancellationToken);

        AuditVerificationResult result;
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entries = db.AuditLogs
                .AsNoTracking()
                .OrderBy(a => a.AuditLogId)
                .AsAsyncEnumerable();
            result = await AuditChainVerifier.VerifyAsync(entries, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Vẫn ghi lại việc kiểm tra thất bại rồi báo lỗi cho người gọi
            await _audit.LogAsync(new AuditEntry(
                AuditActions.AuditVerify, AuditOutcome.Failed, "AuditLog", null,
                new { IsValid = false, Error = ex.GetType().Name }), CancellationToken.None);
            throw;
        }

        // Ghi lại chính thao tác kiểm tra sau khi đã đọc xong (không nằm trong phạm vi chuỗi vừa kiểm tra)
        await _audit.LogAsync(new AuditEntry(
            AuditActions.AuditVerify,
            result.IsValid ? AuditOutcome.Success : AuditOutcome.Failed,
            "AuditLog",
            null,
            new
            {
                IsValid = result.IsValid,
                CheckedCount = result.CheckedCount,
                FirstInvalidAuditLogId = result.FirstInvalidAuditLogId,
                FailureReason = result.FailureReason?.ToString()
            }), cancellationToken);

        return result;
    }
}
