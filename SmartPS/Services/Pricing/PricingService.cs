using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Pricing;

public sealed class PricingService : IPricingService
{
    private const string EntityType = "PricingPolicy";

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;

    public PricingService(IDbContextFactory<SmartPsDbContext> contextFactory, IAuthorizationGuard guard, IAuditService audit)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<PricingConfiguration> LoadPoliciesAsync(CancellationToken cancellationToken = default)
    {
        await _guard.DemandAsync(Permissions.PricingManage, EntityType, null, cancellationToken);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db.PricingRules.AsNoTracking()
            .Include(r => r.VehicleType)
            .OrderBy(r => r.RuleId)
            .ToListAsync(cancellationToken);
        var maxVehicles = await db.ParkingSettings.AsNoTracking()
            .OrderBy(s => s.SettingsId)
            .Select(s => (int?)s.DefaultMaxVehiclesPerHousehold)
            .FirstOrDefaultAsync(cancellationToken) ?? new ParkingSettings().DefaultMaxVehiclesPerHousehold;

        return new PricingConfiguration(rules.Select(ToData).ToList(), maxVehicles);
    }

    public async Task<OperationResult> SavePoliciesAsync(PricingConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Kiểm quyền trước khi mở giao dịch có khoá nhật ký (guard không được gọi khi đang giữ khoá).
        try
        {
            await _guard.DemandAsync(Permissions.PricingManage, EntityType, null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var types = await db.VehicleTypes.AsNoTracking().ToDictionaryAsync(v => v.VehicleTypeId, v => v.TypeName, cancellationToken);
            var invalid = Validate(configuration, types);
            if (invalid is not null)
            {
                return OperationResult.Fail(OperationError.Validation, invalid);
            }

            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {

                var rules = await db.PricingRules.Include(r => r.VehicleType).ToListAsync(cancellationToken);
                var settings = await db.ParkingSettings.OrderBy(s => s.SettingsId).FirstOrDefaultAsync(cancellationToken);
                var oldMax = settings?.DefaultMaxVehiclesPerHousehold ?? new ParkingSettings().DefaultMaxVehiclesPerHousehold;

                var oldRules = new List<PricingRuleData>();
                var newRules = new List<PricingRuleData>();
                var newVersions = new List<(PricingRule Rule, PricingPolicyVersion Version)>();
                var actorId = _guard.CurrentUserId;
                var now = DateTime.UtcNow;

                foreach (var input in configuration.Rules)
                {
                    var rule = rules.FirstOrDefault(r => r.RuleId == input.RuleId);
                    if (rule is null)
                    {
                        return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Price_RuleNotFound", input.VehicleTypeName));
                    }

                    var before = ToData(rule);
                    if (SameValues(before, input))
                    {
                        continue;
                    }

                    rule.Block4hPrice = input.Block4hPrice;
                    rule.DailyPrice = input.DailyPrice;
                    rule.Monthly1Price = input.Monthly1Price;
                    rule.Monthly3Price = input.Monthly3Price;
                    rule.Monthly6Price = input.Monthly6Price;
                    rule.GracePeriodMinutes = input.GracePeriodMinutes;
                    rule.OvernightSurcharge = input.OvernightSurcharge;
                    rule.OverQuotaDailyPrice = input.OverQuotaDailyPrice;

                    var lastNumber = await db.PricingPolicyVersions
                        .Where(v => v.VehicleTypeId == rule.VehicleTypeId)
                        .Select(v => (int?)v.VersionNumber)
                        .MaxAsync(cancellationToken) ?? 0;
                    var version = new PricingPolicyVersion
                    {
                        VehicleTypeId = rule.VehicleTypeId,
                        VersionNumber = lastNumber + 1,
                        Block4hPrice = rule.Block4hPrice,
                        DailyPrice = rule.DailyPrice,
                        Monthly1Price = rule.Monthly1Price,
                        Monthly3Price = rule.Monthly3Price,
                        Monthly6Price = rule.Monthly6Price,
                        GracePeriodMinutes = rule.GracePeriodMinutes,
                        OvernightSurcharge = rule.OvernightSurcharge,
                        OverQuotaDailyPrice = rule.OverQuotaDailyPrice,
                        BlockHours = PricingPolicy.DefaultBlockHours,
                        CreatedAtUtc = now,
                        CreatedByUserId = actorId
                    };
                    db.PricingPolicyVersions.Add(version);
                    newVersions.Add((rule, version));
                    oldRules.Add(before);
                    newRules.Add(ToData(rule));
                }

                var maxChanged = configuration.DefaultMaxVehiclesPerHousehold != oldMax;
                if (newVersions.Count == 0 && !maxChanged)
                {
                    return OperationResult.Ok();
                }

                if (maxChanged)
                {
                    if (settings is null)
                    {
                        settings = new ParkingSettings();
                        db.ParkingSettings.Add(settings);
                    }

                    settings.DefaultMaxVehiclesPerHousehold = configuration.DefaultMaxVehiclesPerHousehold;
                }

                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.PricingUpdated,
                    AuditOutcome.Success,
                    EntityType,
                    newVersions.Count == 0 ? "ParkingSettings" : string.Join(",", newVersions.Select(v => v.Rule.VehicleTypeId)),
                    new
                    {
                        Old = new { Rules = oldRules.Select(ToAudit).ToList(), DefaultMaxVehiclesPerHousehold = oldMax },
                        New = new { Rules = newRules.Select(ToAudit).ToList(), DefaultMaxVehiclesPerHousehold = configuration.DefaultMaxVehiclesPerHousehold },
                        Versions = newVersions.Select(v => new
                        {
                            v.Version.VehicleTypeId,
                            v.Version.VersionId,
                            v.Version.VersionNumber
                        }).ToList()
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

    /// <summary>Quy tắc kiểm tra dữ liệu bảng giá; trả về thông báo lỗi đầu tiên hoặc null khi hợp lệ.</summary>
    internal static string? Validate(PricingConfiguration configuration, IReadOnlyDictionary<int, string> vehicleTypeNames)
    {
        if (configuration.DefaultMaxVehiclesPerHousehold < 1)
        {
            return AppText.Get("Msg_Price_MaxVehiclesMin");
        }

        foreach (var rule in configuration.Rules)
        {
            var name = vehicleTypeNames.TryGetValue(rule.VehicleTypeId, out var typeName) ? typeName : rule.VehicleTypeName;

            if (rule.Block4hPrice < 0 || rule.DailyPrice < 0 || rule.Monthly1Price < 0 || rule.Monthly3Price < 0
                || rule.Monthly6Price < 0 || rule.OvernightSurcharge < 0 || rule.OverQuotaDailyPrice < 0)
            {
                return AppText.Get("Msg_Price_NegativeValue", name);
            }

            if (RequiresPositivePrices(name) && (rule.Block4hPrice <= 0 || rule.DailyPrice <= 0 || rule.Monthly1Price <= 0))
            {
                return AppText.Get("Msg_Price_MustBePositive", name);
            }

            if (rule.GracePeriodMinutes is < 0 or > IPricingService.MaxGracePeriodMinutes)
            {
                return AppText.Get("Msg_Price_GraceRange", name, IPricingService.MaxGracePeriodMinutes);
            }
        }

        return null;
    }

    /// <summary>Xe máy và ô tô: giá lượt (4 giờ, ngày) và vé tháng 1 tháng phải lớn hơn 0.</summary>
    internal static bool RequiresPositivePrices(string vehicleTypeName)
        => vehicleTypeName.Contains("máy", StringComparison.OrdinalIgnoreCase)
           || vehicleTypeName.Contains("ô tô", StringComparison.OrdinalIgnoreCase);

    private static bool SameValues(PricingRuleData a, PricingRuleData b)
        => a.Block4hPrice == b.Block4hPrice
           && a.DailyPrice == b.DailyPrice
           && a.Monthly1Price == b.Monthly1Price
           && a.Monthly3Price == b.Monthly3Price
           && a.Monthly6Price == b.Monthly6Price
           && a.GracePeriodMinutes == b.GracePeriodMinutes
           && a.OvernightSurcharge == b.OvernightSurcharge
           && a.OverQuotaDailyPrice == b.OverQuotaDailyPrice;

    private static PricingRuleData ToData(PricingRule rule) => new()
    {
        RuleId = rule.RuleId,
        VehicleTypeId = rule.VehicleTypeId,
        VehicleTypeName = rule.VehicleType?.TypeName ?? string.Empty,
        Block4hPrice = rule.Block4hPrice,
        DailyPrice = rule.DailyPrice,
        Monthly1Price = rule.Monthly1Price,
        Monthly3Price = rule.Monthly3Price,
        Monthly6Price = rule.Monthly6Price,
        GracePeriodMinutes = rule.GracePeriodMinutes,
        OvernightSurcharge = rule.OvernightSurcharge,
        OverQuotaDailyPrice = rule.OverQuotaDailyPrice,
        Description = rule.Description
    };

    private static object ToAudit(PricingRuleData r) => new
    {
        r.VehicleTypeId,
        r.VehicleTypeName,
        r.Block4hPrice,
        r.DailyPrice,
        r.Monthly1Price,
        r.Monthly3Price,
        r.Monthly6Price,
        r.GracePeriodMinutes,
        r.OvernightSurcharge,
        r.OverQuotaDailyPrice
    };
}
