using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Constants;
using SmartPS.Models.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Pricing;

namespace SmartPS.Tests.Integration;

/// <summary>
/// KAN-29b / KAN-33b (phần Bảng giá): PricingService kiểm quyền Pricing.Manage ở service, kiểm tra dữ liệu,
/// tạo phiên bản bảng giá và ghi PRICING_UPDATED (giá trị cũ/mới) trong cùng giao dịch.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class PricingServiceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public PricingServiceTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    private async Task<(ServiceProvider Sp, IPricingService Pricing)> ServiceAsync(string role)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, role);
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        return (sp, sp.GetRequiredService<IPricingService>());
    }

    private async Task<long> VersionCountAsync()
    {
        await using var ctx = _db.CreateContext();
        return await ctx.PricingPolicyVersions.LongCountAsync();
    }

    private static PricingConfiguration WithRule(PricingConfiguration config, int ruleId, Func<PricingRuleData, PricingRuleData> change)
        => config with { Rules = config.Rules.Select(r => r.RuleId == ruleId ? change(r) : r).ToList() };

    [Fact]
    public async Task Operator_cannot_save_pricing_and_ACCESS_DENIED_is_audited_without_changes()
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Operator");
        using var scope = sp;
        await using var seed = _db.CreateContext();
        var rule = await seed.PricingRules.AsNoTracking().OrderBy(r => r.RuleId).FirstAsync();
        var versionsBefore = await VersionCountAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var config = new PricingConfiguration(new[]
        {
            new PricingRuleData
            {
                RuleId = rule.RuleId, VehicleTypeId = rule.VehicleTypeId, VehicleTypeName = "Xe máy",
                Block4hPrice = rule.Block4hPrice + 1000, DailyPrice = rule.DailyPrice, Monthly1Price = rule.Monthly1Price,
                Monthly3Price = rule.Monthly3Price, Monthly6Price = rule.Monthly6Price, GracePeriodMinutes = 15
            }
        }, 2);

        var result = await pricing.SavePoliciesAsync(config);

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Contains(Permissions.PricingManage, denied.Details, StringComparison.Ordinal);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PricingUpdated));
        Assert.Equal(versionsBefore, await VersionCountAsync());
        await using var after = _db.CreateContext();
        Assert.Equal(rule.Block4hPrice, (await after.PricingRules.AsNoTracking().SingleAsync(r => r.RuleId == rule.RuleId)).Block4hPrice);
    }

    [Fact]
    public async Task Operator_cannot_load_pricing()
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Operator");
        using var scope = sp;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => pricing.LoadPoliciesAsync());

        Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
    }

    [Fact]
    public async Task Manager_save_updates_rule_creates_version_and_audits_old_and_new()
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Manager");
        using var scope = sp;
        var config = await pricing.LoadPoliciesAsync();
        var first = config.Rules[0];
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var edited = WithRule(config, first.RuleId, r => r with
        {
            Block4hPrice = r.Block4hPrice + 1000m,
            GracePeriodMinutes = 20,
            OvernightSurcharge = 3000m,
            OverQuotaDailyPrice = r.DailyPrice + 5000m
        });

        var result = await pricing.SavePoliciesAsync(edited);

        Assert.True(result.Success, result.Message);

        await using var ctx = _db.CreateContext();
        var rule = await ctx.PricingRules.AsNoTracking().SingleAsync(r => r.RuleId == first.RuleId);
        Assert.Equal(first.Block4hPrice + 1000m, rule.Block4hPrice);
        Assert.Equal(20, rule.GracePeriodMinutes);
        Assert.Equal(3000m, rule.OvernightSurcharge);

        var versions = await ctx.PricingPolicyVersions.AsNoTracking()
            .Where(v => v.VehicleTypeId == first.VehicleTypeId).OrderBy(v => v.VersionNumber).ToListAsync();
        var latest = versions[^1];
        Assert.True(latest.VersionNumber >= 2, "version 1 exists from the seed; the save adds the next one");
        Assert.Equal(rule.Block4hPrice, latest.Block4hPrice);
        Assert.Equal(20, latest.GracePeriodMinutes);
        Assert.NotNull(latest.CreatedByUserId);
        Assert.Equal(first.Block4hPrice, versions[^2].Block4hPrice); // phiên bản cũ giữ nguyên giá cũ

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PricingUpdated));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("PricingPolicy", row.EntityType);
        AuditDb.HasKeys(row, "old", "new", "versions");
        var details = AuditDb.Details(row);
        var oldRule = details.GetProperty("old").GetProperty("rules")[0];
        var newRule = details.GetProperty("new").GetProperty("rules")[0];
        Assert.Equal(first.VehicleTypeId, oldRule.GetProperty("vehicleTypeId").GetInt32());
        Assert.Equal(first.Block4hPrice, oldRule.GetProperty("block4hPrice").GetDecimal());
        Assert.Equal(first.GracePeriodMinutes, oldRule.GetProperty("gracePeriodMinutes").GetInt32());
        Assert.Equal(first.Block4hPrice + 1000m, newRule.GetProperty("block4hPrice").GetDecimal());
        Assert.Equal(20, newRule.GetProperty("gracePeriodMinutes").GetInt32());
        Assert.Equal(3000m, newRule.GetProperty("overnightSurcharge").GetDecimal());
        Assert.Equal(latest.VersionId, details.GetProperty("versions")[0].GetProperty("versionId").GetInt32());
    }

    [Fact]
    public async Task Save_without_changes_creates_no_version_and_no_audit()
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Manager");
        using var scope = sp;
        var config = await pricing.LoadPoliciesAsync();
        var versionsBefore = await VersionCountAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await pricing.SavePoliciesAsync(config);

        Assert.True(result.Success, result.Message);
        Assert.Equal(versionsBefore, await VersionCountAsync());
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PricingUpdated));
    }

    [Fact]
    public async Task Changing_only_max_vehicles_is_audited_without_a_new_version()
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Manager");
        using var scope = sp;
        var config = await pricing.LoadPoliciesAsync();
        var versionsBefore = await VersionCountAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await pricing.SavePoliciesAsync(config with { DefaultMaxVehiclesPerHousehold = config.DefaultMaxVehiclesPerHousehold + 1 });

        Assert.True(result.Success, result.Message);
        Assert.Equal(versionsBefore, await VersionCountAsync());
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PricingUpdated));
        var details = AuditDb.Details(row);
        Assert.Equal(config.DefaultMaxVehiclesPerHousehold, details.GetProperty("old").GetProperty("defaultMaxVehiclesPerHousehold").GetInt32());
        Assert.Equal(config.DefaultMaxVehiclesPerHousehold + 1, details.GetProperty("new").GetProperty("defaultMaxVehiclesPerHousehold").GetInt32());
        var again = await pricing.LoadPoliciesAsync();
        Assert.Equal(config.DefaultMaxVehiclesPerHousehold + 1, again.DefaultMaxVehiclesPerHousehold);
    }

    public static TheoryData<string> InvalidCases() => new() { "negative", "grace-high", "grace-negative", "zero-motorbike", "max-vehicles", "negative-overquota" };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task Invalid_input_is_rejected_without_version_audit_or_change(string kind)
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Manager");
        using var scope = sp;
        var config = await pricing.LoadPoliciesAsync();
        var motorbike = config.Rules.First(r => r.VehicleTypeName.Contains("máy", StringComparison.OrdinalIgnoreCase));
        var versionsBefore = await VersionCountAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var bad = kind switch
        {
            "negative" => WithRule(config, motorbike.RuleId, r => r with { Monthly3Price = -1m }),
            "grace-high" => WithRule(config, motorbike.RuleId, r => r with { GracePeriodMinutes = 121 }),
            "grace-negative" => WithRule(config, motorbike.RuleId, r => r with { GracePeriodMinutes = -1 }),
            "zero-motorbike" => WithRule(config, motorbike.RuleId, r => r with { DailyPrice = 0m }),
            "negative-overquota" => WithRule(config, motorbike.RuleId, r => r with { OverQuotaDailyPrice = -5m }),
            _ => config with { DefaultMaxVehiclesPerHousehold = 0 }
        };

        var result = await pricing.SavePoliciesAsync(bad);

        Assert.False(result.Success);
        Assert.Equal(OperationError.Validation, result.Error);
        Assert.Equal(versionsBefore, await VersionCountAsync());
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.PricingUpdated));
        var reloaded = await pricing.LoadPoliciesAsync();
        Assert.Equal(config.Rules, reloaded.Rules);
        Assert.Equal(config.DefaultMaxVehiclesPerHousehold, reloaded.DefaultMaxVehiclesPerHousehold);
    }

    [Fact]
    public async Task Grace_boundaries_0_and_120_are_accepted()
    {
        _db.RequireAvailable();
        var (sp, pricing) = await ServiceAsync("Manager");
        using var scope = sp;
        var config = await pricing.LoadPoliciesAsync();
        var first = config.Rules[0];

        Assert.True((await pricing.SavePoliciesAsync(WithRule(config, first.RuleId, r => r with { GracePeriodMinutes = 0 }))).Success);
        Assert.True((await pricing.SavePoliciesAsync(WithRule(config, first.RuleId, r => r with { GracePeriodMinutes = 120 }))).Success);
        Assert.Equal(120, (await pricing.LoadPoliciesAsync()).Rules.First(r => r.RuleId == first.RuleId).GracePeriodMinutes);
    }
}
