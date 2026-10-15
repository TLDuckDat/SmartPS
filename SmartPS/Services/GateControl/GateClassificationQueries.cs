using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.GateControl;

namespace SmartPS.Services.GateControl;

/// <summary>Truy vấn chỉ-đọc của luồng phân loại xe ở cổng. Mọi lỗi được ném ra để người gọi quyết định (fail-closed).</summary>
public static class GateClassificationQueries
{
    /// <summary>Mục danh sách đen đang hiệu lực khớp biển số đã chuẩn hoá (biển số lưu thô được chuẩn hoá trong SQL).</summary>
    public static async Task<BlacklistMatch?> FindActiveBlacklistAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        CancellationToken cancellationToken = default)
    {
        var norm = normalizedPlate;
        var reason = await db.BlacklistedVehicles.FromSql($"""
                SELECT * FROM "BlacklistedVehicles"
                WHERE "IsActive" AND upper(regexp_replace("LicensePlate", '[^a-zA-Z0-9]', '', 'g')) = {norm}
                """)
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BlacklistId)
            .Select(b => new { b.Reason })
            .FirstOrDefaultAsync(cancellationToken);

        return reason is null ? null : new BlacklistMatch(normalizedPlate, reason.Reason);
    }

    public static async Task<bool> HasActiveSessionAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        CancellationToken cancellationToken = default)
    {
        var norm = normalizedPlate;
        var count = await db.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM "ParkingSessions" WHERE "Status" = 0 AND upper(regexp_replace("LicensePlate", '[^a-zA-Z0-9]', '', 'g')) = {norm}""")
            .SingleAsync(cancellationToken);
        return count > 0;
    }

    public static async Task<VehicleClassification> ClassifyAsync(
        SmartPsDbContext db,
        IPlateOwnershipReader owners,
        BlacklistMatch? blacklist,
        string normalizedPlate,
        int requestedVehicleTypeId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var ownership = await owners.ReadAsync(db, normalizedPlate, nowUtc, cancellationToken);
        return VehicleClassifier.Classify(normalizedPlate, blacklist, ownership, requestedVehicleTypeId, nowUtc);
    }
}
