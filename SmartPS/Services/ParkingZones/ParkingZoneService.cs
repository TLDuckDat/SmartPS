using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;

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

        if (!Enum.IsDefined(audience))
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_InvalidAudience"));
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var zone = await db.ParkingZones.FirstOrDefaultAsync(z => z.ZoneId == zoneId, cancellationToken);
                if (zone is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Zone_NotFound"));
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

    // ------------------------------------------------------------------ Tạo khu, thêm / bảo trì / xoá ô đỗ

    public async Task<OperationResult> CreateZoneAsync(CreateZoneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var denied = await DemandConfigureAsync("ParkingZone", null, cancellationToken);
        if (denied is not null) return denied;

        var code = new string((request.ZoneCode ?? string.Empty).Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        var name = (request.ZoneName ?? string.Empty).Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (code.Length == 0)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_CodeRequired"));
        if (code.Length > 20 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_CodeInvalid"));
        if (name.Length == 0)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_NameRequired"));
        if (name.Length > 100 || description?.Length > 255)
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_TextTooLong"));
        if (!Enum.IsDefined(request.Audience))
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_InvalidAudience"));

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                if (request.VehicleTypeId.HasValue
                    && !await db.VehicleTypes.AnyAsync(v => v.VehicleTypeId == request.VehicleTypeId.Value, cancellationToken))
                {
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_VehicleTypeNotFound"));
                }

                if (await db.ParkingZones.AnyAsync(z => z.ZoneCode == code, cancellationToken))
                {
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Zone_CodeExists", code));
                }

                var zone = new ParkingZone
                {
                    ZoneCode = code,
                    ZoneName = name,
                    VehicleTypeId = request.VehicleTypeId,
                    Audience = request.Audience,
                    Description = description,
                    TotalCapacity = 0
                };
                db.ParkingZones.Add(zone);
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.ZoneCreate,
                    AuditOutcome.Success,
                    "ParkingZone",
                    zone.ZoneId.ToString(),
                    new
                    {
                        ZoneCode = zone.ZoneCode,
                        ZoneName = zone.ZoneName,
                        VehicleTypeId = zone.VehicleTypeId,
                        Audience = zone.Audience.ToString()
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

    public async Task<AddSlotsResult> AddSlotsAsync(AddSlotsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var denied = await DemandConfigureAsync("ParkingZone", request.ZoneId.ToString(), cancellationToken);
        if (denied is not null) return new AddSlotsResult(denied, Array.Empty<string>());

        static AddSlotsResult Invalid(string message) => new(OperationResult.Fail(OperationError.Validation, message), Array.Empty<string>());

        if (request.Count < 1 || request.Count > SlotCodeGenerator.MaxSlotsPerBatch)
            return Invalid(AppText.Get("Msg_Slot_CountRange", SlotCodeGenerator.MaxSlotsPerBatch));

        var prefix = SlotCodeGenerator.NormalizePrefix(request.Prefix);
        if (!SlotCodeGenerator.IsValidPrefix(prefix))
            return Invalid(AppText.Get("Msg_Slot_PrefixInvalid"));

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.Serializable, cancellationToken))
            {
                var zone = await db.ParkingZones.FirstOrDefaultAsync(z => z.ZoneId == request.ZoneId, cancellationToken);
                if (zone is null)
                    return new AddSlotsResult(OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Zone_NotFound")), Array.Empty<string>());

                if (!await db.VehicleTypes.AnyAsync(v => v.VehicleTypeId == request.VehicleTypeId, cancellationToken))
                    return Invalid(AppText.Get("Msg_Zone_VehicleTypeNotFound"));

                // Khu dành cho một loại xe thì ô mới phải cùng loại
                if (zone.VehicleTypeId.HasValue && zone.VehicleTypeId.Value != request.VehicleTypeId)
                    return Invalid(AppText.Get("Msg_Slot_VehicleTypeMismatch"));

                // Mã ô là duy nhất toàn bãi: nối tiếp số lớn nhất của tiền tố trên mọi khu
                var samePrefix = await db.ParkingSlots.AsNoTracking()
                    .Where(x => x.SlotCode.StartsWith(prefix))
                    .Select(x => x.SlotCode)
                    .ToListAsync(cancellationToken);
                var codes = SlotCodeGenerator.NextCodes(prefix, samePrefix, request.Count);
                if (codes[^1].Length > SlotCodeGenerator.MaxCodeLength)
                    return Invalid(AppText.Get("Msg_Slot_PrefixInvalid"));

                var clash = await db.ParkingSlots.AsNoTracking()
                    .Where(x => codes.Contains(x.SlotCode))
                    .Select(x => x.SlotCode)
                    .FirstOrDefaultAsync(cancellationToken);
                if (clash is not null)
                    return Invalid(AppText.Get("Msg_Slot_CodeExists", clash));

                // Toạ độ trên sơ đồ: xếp tiếp theo lưới sau các ô đã có của khu
                var existingInZone = await db.ParkingSlots.CountAsync(x => x.ZoneId == zone.ZoneId, cancellationToken);
                const int perRow = 10;
                for (var i = 0; i < codes.Count; i++)
                {
                    var index = existingInZone + i;
                    db.ParkingSlots.Add(new ParkingSlot
                    {
                        SlotCode = codes[i],
                        ZoneId = zone.ZoneId,
                        ZoneName = zone.ZoneName,
                        VehicleTypeId = request.VehicleTypeId,
                        Status = SlotStatus.Available,
                        CoordX = 40 + index % perRow * 90,
                        CoordY = 40 + index / perRow * 140,
                        Width = 70,
                        Height = 110
                    });
                }

                zone.TotalCapacity = existingInZone + codes.Count;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.SlotCreate,
                    AuditOutcome.Success,
                    "ParkingZone",
                    zone.ZoneId.ToString(),
                    new
                    {
                        ZoneCode = zone.ZoneCode,
                        VehicleTypeId = request.VehicleTypeId,
                        Count = codes.Count,
                        FirstCode = codes[0],
                        LastCode = codes[^1]
                    }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AddSlotsResult(OperationResult.Ok(), codes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AddSlotsResult(OperationResult.Fail(OperationError.DatabaseError, ex.Message), Array.Empty<string>());
        }
    }

    public async Task<OperationResult> SetSlotMaintenanceAsync(int slotId, bool maintenance, string? reason, CancellationToken cancellationToken = default)
    {
        var denied = await DemandConfigureAsync("ParkingSlot", slotId.ToString(), cancellationToken);
        if (denied is not null) return denied;

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.Serializable, cancellationToken))
            {
                var slot = await db.ParkingSlots.Include(x => x.Zone).FirstOrDefaultAsync(x => x.SlotId == slotId, cancellationToken);
                if (slot is null)
                    return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Slot_NotFound"));

                var target = maintenance ? SlotStatus.Maintenance : SlotStatus.Available;
                if (slot.Status == target)
                    return OperationResult.Ok();

                // Không khoá ô đang có xe
                var hasActiveSession = await db.ParkingSessions.AnyAsync(s => s.SlotId == slotId && s.Status == SessionStatus.Active, cancellationToken);
                if (hasActiveSession || slot.Status == SlotStatus.Occupied)
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Slot_Occupied", slot.SlotCode));

                var before = slot.Status;
                slot.Status = target;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.SlotStatusUpdate,
                    AuditOutcome.Success,
                    "ParkingSlot",
                    slot.SlotId.ToString(),
                    new
                    {
                        SlotCode = slot.SlotCode,
                        ZoneCode = slot.Zone?.ZoneCode,
                        Before = before.ToString(),
                        After = target.ToString(),
                        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
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

    public async Task<OperationResult> DeleteSlotAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var denied = await DemandConfigureAsync("ParkingSlot", slotId.ToString(), cancellationToken);
        if (denied is not null) return denied;

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.Serializable, cancellationToken))
            {
                var slot = await db.ParkingSlots.Include(x => x.Zone).FirstOrDefaultAsync(x => x.SlotId == slotId, cancellationToken);
                if (slot is null)
                    return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Slot_NotFound"));

                if (slot.Status == SlotStatus.Occupied
                    || await db.ParkingSessions.AnyAsync(s => s.SlotId == slotId && s.Status == SessionStatus.Active, cancellationToken))
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Slot_Occupied", slot.SlotCode));

                // Ô đã có lịch sử gửi xe thì giữ lại cho báo cáo và đối soát; chỉ chuyển sang bảo trì
                if (await db.ParkingSessions.AnyAsync(s => s.SlotId == slotId, cancellationToken))
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Slot_HasHistory", slot.SlotCode));

                var zone = slot.Zone;
                var details = new { SlotCode = slot.SlotCode, ZoneCode = zone?.ZoneCode, VehicleTypeId = slot.VehicleTypeId };
                var remaining = zone is null ? 0 : await db.ParkingSlots.CountAsync(x => x.ZoneId == zone.ZoneId, cancellationToken) - 1;
                db.ParkingSlots.Remove(slot);
                if (zone is not null)
                {
                    zone.TotalCapacity = Math.Max(0, remaining);
                }

                await db.SaveChangesAsync(cancellationToken);
                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.SlotDelete,
                    AuditOutcome.Success,
                    "ParkingSlot",
                    slotId.ToString(),
                    details), cancellationToken);
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

    private async Task<OperationResult?> DemandConfigureAsync(string entityType, string? entityId, CancellationToken cancellationToken)
    {
        try
        {
            await _guard.DemandAsync(Permissions.ParkingConfigure, entityType, entityId, cancellationToken);
            return null;
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }
    }
}
