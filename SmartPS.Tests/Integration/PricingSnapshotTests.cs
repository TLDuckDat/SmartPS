using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// KAN-29b: khi xe ra, cổng lưu bản chụp bảng giá và chi tiết cước vào phiên; đổi bảng giá sau đó không đổi giao dịch cũ;
/// vé tháng hết hạn giữa lượt và xe vượt định mức được tính theo engine.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class PricingSnapshotTests : IClassFixture<SqlServerDatabaseFixture>
{
    private const decimal Block4h = 7_000m;
    private const decimal Daily = 31_000m;

    private readonly SqlServerDatabaseFixture _db;
    private readonly GateTestData _data;

    public PricingSnapshotTests(SqlServerDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> OperatorWithShiftAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        return (sp, op.UserId);
    }

    private static IGateControlService Gate(IServiceProvider sp) => sp.GetRequiredService<IGateControlService>();

    /// <summary>Loại xe riêng có bảng giá 7.000 / 31.000 và bản chụp phiên bản 1 trùng bảng giá.</summary>
    private async Task<(int VehicleTypeId, int VersionId)> VehicleTypeWithVersionAsync()
    {
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: Block4h, dailyPrice: Daily);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 4);
        var versionId = await AddVersionAsync(vt, 1, Block4h, Daily, overQuota: 0m);
        return (vt, versionId);
    }

    private async Task<int> AddVersionAsync(int vehicleTypeId, int number, decimal block, decimal daily, decimal overQuota)
    {
        await using var ctx = _db.CreateContext();
        var version = new PricingPolicyVersion
        {
            VehicleTypeId = vehicleTypeId,
            VersionNumber = number,
            Block4hPrice = block,
            DailyPrice = daily,
            Monthly1Price = 100_000m,
            Monthly3Price = 285_000m,
            Monthly6Price = 540_000m,
            OverQuotaDailyPrice = overQuota
        };
        ctx.PricingPolicyVersions.Add(version);
        await ctx.SaveChangesAsync();
        return version.VersionId;
    }

    private async Task<int> CheckInBackdatedAsync(IServiceProvider sp, string plate, int vt, TimeSpan ago)
    {
        var result = await Gate(sp).ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
        Assert.True(result.Success, result.Message);
        await _data.SetSessionCheckInAsync(result.Session!.SessionId, DateTime.UtcNow - ago);
        return result.Session.SessionId;
    }

    private async Task<ParkingSession> DbSessionAsync(int sessionId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
    }

    private static Task<GateCheckOutResult> CompleteAsync(IServiceProvider sp, int sessionId, int userId)
        => Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest { SessionId = sessionId, ActorUserId = userId, PaymentMethod = PaymentMethod.Cash });

    [Fact]
    public async Task Checkout_stores_policy_version_and_fee_breakdown_matching_the_total()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var (vt, versionId) = await VehicleTypeWithVersionAsync();
        var plate = GateTestData.UniqueNormalizedPlate();
        var sessionId = await CheckInBackdatedAsync(sp, plate, vt, TimeSpan.FromHours(5));

        var done = await CompleteAsync(sp, sessionId, userId);

        Assert.True(done.Success, done.Message);
        var stored = await DbSessionAsync(sessionId);
        Assert.Equal(2 * Block4h, stored.TotalFee);
        Assert.Equal(versionId, stored.PricingPolicyVersionId);
        Assert.False(string.IsNullOrWhiteSpace(stored.FeeBreakdownJson));
        using var doc = JsonDocument.Parse(stored.FeeBreakdownJson!);
        Assert.Equal(stored.TotalFee, doc.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(versionId, doc.RootElement.GetProperty("policyVersionId").GetInt32());
        var lines = doc.RootElement.GetProperty("lines");
        Assert.Equal(stored.TotalFee, lines.EnumerateArray().Sum(l => l.GetProperty("amount").GetDecimal()));
        Assert.Contains(lines.EnumerateArray(), l => l.GetProperty("code").GetString() == "CYCLE");
    }

    [Fact]
    public async Task Changing_the_pricing_afterwards_does_not_change_a_completed_session()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var (vt, versionId) = await VehicleTypeWithVersionAsync();
        var first = await CheckInBackdatedAsync(sp, GateTestData.UniqueNormalizedPlate(), vt, TimeSpan.FromHours(3));
        Assert.True((await CompleteAsync(sp, first, userId)).Success);
        var before = await DbSessionAsync(first);

        // Bảng giá mới: 10.000 / 40.000, phiên bản 2
        await _db.ExecuteAsync("UPDATE \"PricingRules\" SET \"Block4hPrice\" = 10000, \"DailyPrice\" = 40000 WHERE \"VehicleTypeId\" = @vt", ("vt", vt));
        var version2 = await AddVersionAsync(vt, 2, 10_000m, 40_000m, 0m);

        var after = await DbSessionAsync(first);
        Assert.Equal(before.TotalFee, after.TotalFee);
        Assert.Equal(before.FeeBreakdownJson, after.FeeBreakdownJson);
        Assert.Equal(versionId, after.PricingPolicyVersionId);

        // Phiên mới dùng bảng giá mới và gắn phiên bản 2
        var second = await CheckInBackdatedAsync(sp, GateTestData.UniqueNormalizedPlate(), vt, TimeSpan.FromHours(3));
        Assert.True((await CompleteAsync(sp, second, userId)).Success);
        var stored = await DbSessionAsync(second);
        Assert.Equal(10_000m, stored.TotalFee);
        Assert.Equal(version2, stored.PricingPolicyVersionId);
    }

    [Fact]
    public async Task Rule_without_a_matching_version_still_charges_correctly_with_a_null_version()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: Block4h, dailyPrice: Daily);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var sessionId = await CheckInBackdatedAsync(sp, GateTestData.UniqueNormalizedPlate(), vt, TimeSpan.FromHours(2));

        Assert.True((await CompleteAsync(sp, sessionId, userId)).Success);

        var stored = await DbSessionAsync(sessionId);
        Assert.Equal(Block4h, stored.TotalFee);
        Assert.Null(stored.PricingPolicyVersionId);
        Assert.False(string.IsNullOrWhiteSpace(stored.FeeBreakdownJson));
    }

    [Fact]
    public async Task Ticket_expired_during_the_stay_is_snapshotted_with_a_monthly_free_line()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var (vt, _) = await VehicleTypeWithVersionAsync();
        var external = await _data.CreateExternalAsync(vt, withTicket: true);
        var sessionId = await CheckInBackdatedAsync(sp, external.NormalizedPlate, vt, TimeSpan.FromHours(6));
        await _data.SetTicketEndAsync(external.TicketId!.Value, DateTime.UtcNow.AddHours(-1));

        Assert.True((await CompleteAsync(sp, sessionId, userId)).Success);

        var stored = await DbSessionAsync(sessionId);
        Assert.Equal(Block4h, stored.TotalFee);
        using var doc = JsonDocument.Parse(stored.FeeBreakdownJson!);
        Assert.Contains(doc.RootElement.GetProperty("lines").EnumerateArray(), l => l.GetProperty("code").GetString() == "MONTHLY_FREE");
    }

    [Fact]
    public async Task Vehicle_beyond_the_household_limit_pays_the_over_quota_price()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: Block4h, dailyPrice: Daily);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 6);
        await _db.ExecuteAsync("UPDATE \"PricingRules\" SET \"OverQuotaDailyPrice\" = 50000 WHERE \"VehicleTypeId\" = @vt", ("vt", vt));
        var limit = await _db.ScalarAsync<int>("SELECT TOP 1 \"DefaultMaxVehiclesPerHousehold\" FROM \"ParkingSettings\" ORDER BY \"SettingsId\"");

        // Một hộ có limit + 1 xe: các xe tạo trước (VehicleId nhỏ) trong hạn mức, xe cuối vượt
        var resident = await _data.CreateResidentAsync(vt);
        var plates = new List<string> { resident.NormalizedPlate };
        for (var i = 0; i < limit; i++)
        {
            var plate = GateTestData.UniqueNormalizedPlate();
            await _data.AddVehicleAsync(resident.CustomerId, GateTestData.Raw(plate), vt);
            plates.Add(plate);
        }

        var inQuota = await CheckInBackdatedAsync(sp, plates[0], vt, TimeSpan.FromHours(3));
        var overQuota = await CheckInBackdatedAsync(sp, plates[^1], vt, TimeSpan.FromHours(3));

        Assert.True((await CompleteAsync(sp, inQuota, userId)).Success);
        Assert.True((await CompleteAsync(sp, overQuota, userId)).Success);

        Assert.Equal(Block4h, (await DbSessionAsync(inQuota)).TotalFee);
        var over = await DbSessionAsync(overQuota);
        Assert.Equal(50_000m, over.TotalFee);
        using var doc = JsonDocument.Parse(over.FeeBreakdownJson!);
        Assert.Contains(doc.RootElement.GetProperty("lines").EnumerateArray(), l => l.GetProperty("code").GetString() == "OVER_QUOTA");
    }
}
