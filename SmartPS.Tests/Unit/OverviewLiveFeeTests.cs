using SmartPS.Models.Parking;
using SmartPS.Services.Pricing;
using SmartPS.ViewModels.Overview;

namespace SmartPS.Tests.Unit;

/// <summary>Màn Tổng quan: xe đang trong bãi hiển thị phí tạm tính; xe đã ra giữ nguyên phí đã thu.</summary>
public class OverviewLiveFeeTests
{
    private sealed class FakeEstimator : IParkingFeeEstimator
    {
        public decimal PerMinute { get; set; } = 100m;
        public int ResolveCalls { get; private set; }

        public Task<IReadOnlyDictionary<int, PricingContext>> ResolveContextsAsync(
            IEnumerable<ParkingSession> sessions, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            IReadOnlyDictionary<int, PricingContext> map = sessions
                .Where(s => s.Status == SessionStatus.Active)
                .ToDictionary(s => s.SessionId, _ => new PricingContext(PricingPolicy.Default(1), null, false));
            return Task.FromResult(map);
        }

        public decimal Estimate(ParkingSession session, PricingContext context, DateTime nowUtc)
            => Math.Floor((decimal)(nowUtc - session.CheckInTime).TotalMinutes) * PerMinute;
    }

    private static ParkingSession Active(int id, int minutesAgo) => new()
    {
        SessionId = id, TicketCode = "TK-" + id, LicensePlate = "59X1-000.0" + id, VehicleTypeId = 1,
        Status = SessionStatus.Active, CheckInTime = DateTime.UtcNow.AddMinutes(-minutesAgo), TotalFee = 0
    };

    [Fact]
    public async Task Active_sessions_show_estimated_fee_and_completed_keep_charged_fee()
    {
        var completed = new ParkingSession
        {
            SessionId = 9, TicketCode = "TK-9", LicensePlate = "51F-123.45", VehicleTypeId = 2,
            Status = SessionStatus.Completed, CheckInTime = DateTime.UtcNow.AddHours(-5),
            CheckOutTime = DateTime.UtcNow.AddHours(-1), TotalFee = 25000
        };
        var reports = new FakeReportService { RecentSessionsResult = new[] { Active(1, 60), completed } };
        var vm = new OverviewViewModel(reports, new FakeLocalizationService(), new FakeEstimator());

        await vm.LoadDataAsync();

        var active = vm.RecentSessions.Single(i => i.Session.SessionId == 1);
        var done = vm.RecentSessions.Single(i => i.Session.SessionId == 9);
        Assert.True(active.IsEstimated);
        Assert.InRange(active.Fee, 5900m, 6100m);
        Assert.False(done.IsEstimated);
        Assert.Equal(25000m, done.Fee);
    }

    [Fact]
    public async Task Refresh_recomputes_without_reloading_contexts()
    {
        var estimator = new FakeEstimator();
        var reports = new FakeReportService { RecentSessionsResult = new[] { Active(1, 30) } };
        var vm = new OverviewViewModel(reports, new FakeLocalizationService(), estimator);
        await vm.LoadDataAsync();
        var item = vm.RecentSessions.Single();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        estimator.PerMinute = 200m;
        vm.RefreshEstimatedFees();

        Assert.Equal(1, estimator.ResolveCalls);
        Assert.InRange(item.Fee, 5800m, 6200m);
        Assert.Contains(nameof(RecentSessionItem.Fee), changed);
    }

    [Fact]
    public async Task Without_estimator_fee_column_falls_back_to_stored_fee()
    {
        var reports = new FakeReportService { RecentSessionsResult = new[] { Active(1, 90) } };
        var vm = new OverviewViewModel(reports, new FakeLocalizationService());

        await vm.LoadDataAsync();

        var item = vm.RecentSessions.Single();
        Assert.False(item.IsEstimated);
        Assert.Equal(0m, item.Fee);
    }
}
