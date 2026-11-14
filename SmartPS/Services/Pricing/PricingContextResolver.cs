using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Services.Pricing;

/// <summary>Đầu vào ngoài thời gian của engine cho một phiên gửi xe: bảng giá chụp, vé tháng, xe vượt định mức.</summary>
public sealed record PricingContext(PricingPolicy Policy, MonthlyPassInfo? MonthlyPass, bool IsOverQuota);

/// <summary>
/// Tập hợp dữ liệu để tính cước một phiên (chỉ đọc). Bảng giá lấy từ PricingRules hiện hành; chỉ gắn mã phiên bản
/// khi bản chụp mới nhất của loại xe trùng giá trị với bảng giá hiện hành. Dữ liệu của nhóm Khách hàng (vé tháng,
/// xe, hộ) chỉ được đọc, không ghi.
/// </summary>
public static class PricingContextResolver
{
    /// <summary>Số xe tối đa mỗi hộ khi chưa có bản ghi cấu hình (mặc định của ParkingSettings).</summary>
    private static int DefaultMaxVehicles => new ParkingSettings().DefaultMaxVehiclesPerHousehold;

    public static async Task<PricingContext> ResolveAsync(
        SmartPsDbContext db,
        ParkingSession session,
        DateTime nowUtc,
        IEnumerable<PricingRule>? fallbackRules = null,
        CancellationToken cancellationToken = default)
    {
        var policy = await ResolvePolicyAsync(db, session.VehicleTypeId, fallbackRules, cancellationToken);
        var pass = await ResolvePassAsync(db, session, cancellationToken);
        var overQuota = await IsOverQuotaAsync(db, session, cancellationToken);
        return new PricingContext(policy, pass, overQuota);
    }

    public static async Task<PricingPolicy> ResolvePolicyAsync(
        SmartPsDbContext db,
        int vehicleTypeId,
        IEnumerable<PricingRule>? fallbackRules = null,
        CancellationToken cancellationToken = default)
    {
        var rule = await db.PricingRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.VehicleTypeId == vehicleTypeId, cancellationToken);
        var version = await db.PricingPolicyVersions.AsNoTracking()
            .Where(v => v.VehicleTypeId == vehicleTypeId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (rule is null)
        {
            if (version is not null)
            {
                return FromVersion(version);
            }

            var fallback = fallbackRules?.FirstOrDefault(r => r.VehicleTypeId == vehicleTypeId);
            return fallback is null ? PricingPolicy.Default(vehicleTypeId) : PricingPolicy.FromRule(fallback);
        }

        var policy = PricingPolicy.FromRule(rule);
        return version is not null && Matches(version, rule)
            ? policy with { PolicyVersionId = version.VersionId, PolicyVersion = version.VersionNumber }
            : policy;
    }

    public static PricingPolicy FromVersion(PricingPolicyVersion version) => new()
    {
        VehicleTypeId = version.VehicleTypeId,
        Block4hPrice = version.Block4hPrice,
        DailyPrice = version.DailyPrice,
        GracePeriodMinutes = version.GracePeriodMinutes,
        OvernightSurcharge = version.OvernightSurcharge,
        OverQuotaDailyPrice = version.OverQuotaDailyPrice,
        BlockHours = version.BlockHours,
        PolicyVersionId = version.VersionId,
        PolicyVersion = version.VersionNumber
    };

    private static bool Matches(PricingPolicyVersion v, PricingRule r)
        => v.Block4hPrice == r.Block4hPrice
           && v.DailyPrice == r.DailyPrice
           && v.GracePeriodMinutes == r.GracePeriodMinutes
           && v.OvernightSurcharge == r.OvernightSurcharge
           && v.OverQuotaDailyPrice == r.OverQuotaDailyPrice;

    /// <summary>
    /// Vé tháng của phiên đã vào bằng vé (IsMonthlyPass): tìm vé còn hiệu lực lúc vào theo xe/biển số. Không tìm thấy
    /// (dữ liệu cũ chỉ có cờ) thì coi như vé miễn phí cả lượt như trước.
    /// </summary>
    private static async Task<MonthlyPassInfo?> ResolvePassAsync(SmartPsDbContext db, ParkingSession session, CancellationToken cancellationToken)
    {
        if (!session.IsMonthlyPass)
        {
            return null;
        }

        var norm = LicensePlateNormalizer.Normalize(session.LicensePlate);
        var entry = session.CheckInTime;
        var ticket = await db.MonthlyTickets.FromSql($"""
                SELECT * FROM "MonthlyTickets"
                WHERE "Status" = 0 AND "StartDate" <= {entry} AND {entry} <= "EndDate"
                  AND (dbo.fn_NormalizePlate("RegisteredLicensePlate") = {norm}
                       OR "VehicleId" IN (SELECT "VehicleId" FROM "Vehicles"
                                          WHERE "IsActive" = 1 AND dbo.fn_NormalizePlate("LicensePlate") = {norm}))
                """)
            .AsNoTracking()
            .OrderByDescending(t => t.EndDate).ThenByDescending(t => t.TicketId)
            .FirstOrDefaultAsync(cancellationToken);

        return ticket is null
            ? new MonthlyPassInfo(DateTime.MinValue, DateTime.MaxValue, MonthlyTicketStatus.Active)
            : new MonthlyPassInfo(
                DateTime.SpecifyKind(ticket.StartDate, DateTimeKind.Utc),
                DateTime.SpecifyKind(ticket.EndDate, DateTimeKind.Utc),
                ticket.Status);
    }

    /// <summary>
    /// Xe vượt định mức: chủ xe thuộc hộ đang hoạt động và số xe đang hoạt động của hộ vượt
    /// ParkingSettings.DefaultMaxVehiclesPerHousehold; các xe xếp sau N xe đầu (VehicleId tăng dần) là vượt.
    /// </summary>
    private static async Task<bool> IsOverQuotaAsync(SmartPsDbContext db, ParkingSession session, CancellationToken cancellationToken)
    {
        var norm = LicensePlateNormalizer.Normalize(session.LicensePlate);
        if (string.IsNullOrEmpty(norm))
        {
            return false;
        }

        var vehicle = await db.Vehicles.FromSql($"""
                SELECT * FROM "Vehicles"
                WHERE "IsActive" = 1 AND dbo.fn_NormalizePlate("LicensePlate") = {norm}
                """)
            .AsNoTracking()
            .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.VehicleId)
            .Select(v => new
            {
                v.VehicleId,
                OwnerActive = v.OwnerCustomer!.IsActive,
                v.OwnerCustomer.HouseholdId,
                HouseholdActive = v.OwnerCustomer.Household != null && v.OwnerCustomer.Household.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (vehicle is null || !vehicle.OwnerActive || vehicle.HouseholdId is null || !vehicle.HouseholdActive)
        {
            return false;
        }

        var maxVehicles = await db.ParkingSettings.AsNoTracking()
            .OrderBy(s => s.SettingsId)
            .Select(s => (int?)s.DefaultMaxVehiclesPerHousehold)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultMaxVehicles;

        var householdId = vehicle.HouseholdId.Value;
        var allowed = await db.Vehicles.AsNoTracking()
            .Where(v => v.IsActive && v.OwnerCustomer!.HouseholdId == householdId)
            .OrderBy(v => v.VehicleId)
            .Select(v => v.VehicleId)
            .Take(Math.Max(maxVehicles, 0))
            .ToListAsync(cancellationToken);

        return !allowed.Contains(vehicle.VehicleId);
    }
}
