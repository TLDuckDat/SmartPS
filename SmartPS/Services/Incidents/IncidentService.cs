using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Data.Configurations.Incidents;
using SmartPS.Models.Audit;
using SmartPS.Models.Incidents;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.GateControl;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Incidents;

public sealed class IncidentService : IIncidentService
{
    private const string EntityType = "Incident";

    // Nhân viên soát vé được lập biên bản; chỉ người có quyền Incident.Manage được đóng biên bản
    private static readonly string[] ReportPermissions =
    {
        Permissions.IncidentManage, Permissions.ParkingCheckIn, Permissions.ParkingCheckOut
    };

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;
    private readonly ICurrentUserContext _currentUser;

    public IncidentService(
        IDbContextFactory<SmartPsDbContext> contextFactory,
        IAuthorizationGuard guard,
        IAuditService audit,
        ICurrentUserContext currentUser)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public async Task<IReadOnlyList<IncidentInfo>> GetIncidentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await db.Incidents
                .AsNoTracking()
                .OrderByDescending(x => x.ReportedAt)
                .ThenByDescending(x => x.IncidentId)
                .Select(x => new IncidentInfo(
                    x.IncidentId,
                    x.IncidentCode,
                    x.Type,
                    x.LicensePlate,
                    x.Description,
                    x.Status,
                    x.ReportedAt,
                    x.ReportedByUser != null ? x.ReportedByUser.FullName : string.Empty,
                    x.ResolvedAt,
                    x.ResolvedByUser != null ? x.ResolvedByUser.FullName : null,
                    x.ResolutionNotes))
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<IncidentInfo>();
        }
    }

    public async Task<OperationResult> ReportAsync(IncidentType type, string? licensePlate, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAnyAsync(ReportPermissions, EntityType, null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var plate = string.IsNullOrWhiteSpace(licensePlate) ? null : licensePlate.Trim().ToUpperInvariant();
        var text = description?.Trim() ?? string.Empty;

        if (!Enum.IsDefined(type))
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_InvalidType"));
        if (text.Length == 0)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_DescriptionRequired"));
        if (text.Length > IncidentConfiguration.TextMaxLength)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_DescriptionTooLong", IncidentConfiguration.TextMaxLength));
        if (plate != null && plate.Length > IncidentConfiguration.LicensePlateMaxLength)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_PlateTooLong", IncidentConfiguration.LicensePlateMaxLength));

        var userId = _currentUser.User?.UserId;
        if (userId is null or <= 0)
            return OperationResult.Fail(OperationError.PermissionDenied, AppText.Get("Msg_IncSvc_InvalidSession"));

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var incident = await CreateIncidentAsync(db, type, plate, text, userId.Value, cancellationToken);
            return OperationResult.Ok(incident.IncidentCode);
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

    public async Task<OperationResult> ResolveAsync(int incidentId, string resolutionNotes, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.IncidentManage, EntityType, incidentId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var notes = resolutionNotes?.Trim() ?? string.Empty;
        if (notes.Length == 0)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_ResolutionRequired"));
        if (notes.Length > IncidentConfiguration.TextMaxLength)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_ResolutionTooLong", IncidentConfiguration.TextMaxLength));

        var userId = _currentUser.User?.UserId;
        if (userId is null or <= 0)
            return OperationResult.Fail(OperationError.PermissionDenied, AppText.Get("Msg_IncSvc_InvalidSession"));

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var incident = await db.Incidents.FirstOrDefaultAsync(x => x.IncidentId == incidentId, cancellationToken);
                if (incident is null)
                    return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_IncSvc_NotFound"));
                if (incident.Status == IncidentStatus.Resolved)
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_IncSvc_AlreadyResolved"));

                incident.Status = IncidentStatus.Resolved;
                incident.ResolvedByUserId = userId.Value;
                incident.ResolvedAt = DateTime.UtcNow;
                incident.ResolutionNotes = notes;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.IncidentResolve,
                    AuditOutcome.Success,
                    EntityType,
                    incident.IncidentId.ToString(),
                    new { IncidentCode = incident.IncidentCode, Notes = notes }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok(incident.IncidentCode);
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

    public async Task<OperationResult> ScanOverstayAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAnyAsync(ReportPermissions, EntityType, null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var userId = _currentUser.User?.UserId;
        if (userId is null or <= 0)
            return OperationResult.Fail(OperationError.PermissionDenied, AppText.Get("Msg_IncSvc_InvalidSession"));

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var thresholdHours = await db.ParkingSettings
                .AsNoTracking()
                .Select(s => (int?)s.OverstayThresholdHours)
                .FirstOrDefaultAsync(cancellationToken) ?? new ParkingSettings().OverstayThresholdHours;
            var cutoff = DateTime.UtcNow.AddHours(-thresholdHours);

            var overstaying = await db.ParkingSessions
                .AsNoTracking()
                .Where(s => s.Status == SessionStatus.Active && s.CheckInTime < cutoff)
                .OrderBy(s => s.CheckInTime)
                .Select(s => new { s.LicensePlate, s.TicketCode, s.CheckInTime })
                .ToListAsync(cancellationToken);

            var openOverstayPlates = (await db.Incidents
                    .AsNoTracking()
                    .Where(i => i.Type == IncidentType.Overstay && i.Status == IncidentStatus.Open && i.LicensePlate != null)
                    .Select(i => i.LicensePlate!)
                    .ToListAsync(cancellationToken))
                .Select(LicensePlateNormalizer.Normalize)
                .ToHashSet();

            var created = 0;
            foreach (var session in overstaying)
            {
                var normalized = LicensePlateNormalizer.Normalize(session.LicensePlate);
                if (normalized.Length == 0 || !openOverstayPlates.Add(normalized))
                    continue;

                var checkInVn = AuditTime.ToVietnamTime(session.CheckInTime);
                var description = $"Xe gửi quá {thresholdHours} giờ chưa xuất bãi (vé {session.TicketCode}, vào lúc {checkInVn:dd/MM/yyyy HH:mm}).";
                await CreateIncidentAsync(db, IncidentType.Overstay, session.LicensePlate.Trim().ToUpperInvariant(), description, userId.Value, cancellationToken);
                created++;
            }

            return OperationResult.Ok(created.ToString());
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

    /// <summary>Mã biên bản kế tiếp trong năm (giờ Việt Nam): SC-2026-001, SC-2026-002...</summary>
    internal static string NextIncidentCode(int vietnamYear, IEnumerable<string> existingCodesOfYear)
    {
        var prefix = $"SC-{vietnamYear}-";
        var max = 0;
        foreach (var code in existingCodesOfYear)
        {
            if (code.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(code.AsSpan(prefix.Length), out var number) &&
                number > max)
            {
                max = number;
            }
        }

        return $"{prefix}{max + 1:000}";
    }

    private async Task<Incident> CreateIncidentAsync(
        SmartPsDbContext db,
        IncidentType type,
        string? licensePlate,
        string description,
        int reportedByUserId,
        CancellationToken cancellationToken)
    {
        // Serializable + chỉ mục duy nhất trên IncidentCode: hai biên bản lập cùng lúc không thể trùng mã
        await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            var now = DateTime.UtcNow;
            var year = AuditTime.ToVietnamTime(now).Year;
            var prefix = $"SC-{year}-";
            var codesOfYear = await db.Incidents
                .AsNoTracking()
                .Where(x => x.IncidentCode.StartsWith(prefix))
                .Select(x => x.IncidentCode)
                .ToListAsync(cancellationToken);

            var incident = new Incident
            {
                IncidentCode = NextIncidentCode(year, codesOfYear),
                Type = type,
                LicensePlate = licensePlate,
                Description = description,
                Status = IncidentStatus.Open,
                ReportedByUserId = reportedByUserId,
                ReportedAt = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(cancellationToken);

            await _audit.AppendAsync(db, new AuditEntry(
                AuditActions.IncidentReport,
                AuditOutcome.Success,
                EntityType,
                incident.IncidentId.ToString(),
                new { IncidentCode = incident.IncidentCode, Type = type.ToString(), LicensePlate = licensePlate }), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            db.ChangeTracker.Clear();
            return incident;
        }
    }
}
