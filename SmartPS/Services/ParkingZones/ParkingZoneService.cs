using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;

namespace SmartPS.Services.ParkingZones;

public sealed class ParkingZoneService : IParkingZoneService
{
    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;

    public ParkingZoneService(IDbContextFactory<SmartPsDbContext> contextFactory, IAuthorizationGuard guard, IAuditService audit)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<IReadOnlyList<ParkingZoneInfo>> GetZonesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await db.ParkingZones
                .AsNoTracking()
                .OrderBy(z => z.ZoneCode)
                .Select(z => new ParkingZoneInfo(z.ZoneId, z.ZoneCode, z.ZoneName, z.Audience, z.VehicleTypeId, z.TotalCapacity))
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Ngoại tuyến: sơ đồ bãi tự gom ô theo tên khu cũ
            return Array.Empty<ParkingZoneInfo>();
        }
    }

    public async Task<OperationResult> UpdateAudienceAsync(int zoneId, ZoneAudience audience, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.ParkingConfigure, "ParkingZone", zoneId.ToString(), cancellationToken);
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
                var zone = await db.ParkingZones.FirstOrDefaultAsync(z => z.ZoneId == zoneId, cancellationToken);
                if (zone is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy khu đỗ xe.");
                }

                if (zone.Audience == audience)
                {
                    return OperationResult.Ok();
                }

                var before = zone.Audience;

                // Chỉ đổi dòng của khu: xe đang đỗ trong khu không bị ảnh hưởng (E4)
                zone.Audience = audience;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.ZoneUpdate,
                    AuditOutcome.Success,
                    "ParkingZone",
                    zone.ZoneId.ToString(),
                    new
                    {
                        ZoneCode = zone.ZoneCode,
                        Before = new { Audience = before.ToString() },
                        After = new { Audience = audience.ToString() }
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
}
