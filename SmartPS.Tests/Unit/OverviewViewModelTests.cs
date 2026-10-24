using SmartPS.ViewModels.Overview;

namespace SmartPS.Tests.Unit;

/// <summary>
/// R7 / AC-9 (UI part) and plan §1.8 / §2.5 / m3 / ADDENDUM N5: Overview reads the shared report service snapshot,
/// does not load in its constructor, formats revenue in the VM and shows a localized banner on failure.
/// </summary>
public class OverviewViewModelTests
{
    private sealed class FailingReportService : IReportService
    {
        public Task<ReportFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ReportResult> GetReportAsync(ReportFilter filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<SessionDetailPage> GetSessionDetailsAsync(ReportFilter filter, int maxRows = ReportLimits.MaxExportSessionRows, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();
        public Task<OverviewSnapshot> GetOverviewSnapshotAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("db down");
        public Task<IReadOnlyList<SmartPS.Models.Parking.ParkingSession>> GetRecentSessionsAsync(int count = 15, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("db down");
    }

    [Fact]
    public void M3_constructor_does_not_call_the_service()
    {
        var reports = new FakeReportService();

        _ = new OverviewViewModel(reports, new FakeLocalizationService());

        Assert.Equal(0, reports.OverviewCalls);
        Assert.Equal(0, reports.RecentSessionsCalls);
    }

    [Fact]
    public async Task Load_maps_the_snapshot()
    {
        var reports = new FakeReportService();
        var vm = new OverviewViewModel(reports, new FakeLocalizationService());

        await vm.LoadDataAsync();

        Assert.Equal(1, reports.OverviewCalls);
        Assert.Equal(1, reports.RecentSessionsCalls);
        Assert.Equal(30, vm.TotalSlots);
        Assert.Equal(20, vm.AvailableSlots);
        Assert.Equal(8, vm.TotalParkedVehicles);
        Assert.Equal(12, vm.TodayCheckIns);
        Assert.Equal(10, vm.TodayCheckOuts);
        Assert.Equal(150_000m, vm.TodayRevenue);
        Assert.Equal("150.000 ₫", vm.TodayRevenueFormatted);
        Assert.Equal(3, vm.ResidentParkedCount);
        Assert.Equal(1, vm.MonthlyParkedCount);
        // r1 m3: the visitor count is VisitorParkedCount (read by name so the suite compiles before the rename)
        Assert.Equal(4, VisitorParkedCount(vm));
        Assert.Equal(new[] { "Xe máy", "Xe ô tô" }, vm.ParkedByVehicleType.Select(v => v.Name).ToArray());
        Assert.Equal(26.7, vm.OccupancyRate, 1);
        Assert.False(vm.HasError);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Kpi_values_are_split_from_their_units_for_baseline_alignment()
    {
        var vm = new OverviewViewModel(new FakeReportService(), new FakeLocalizationService());

        await vm.LoadDataAsync();

        Assert.Equal("150.000", vm.TodayRevenueAmount);
        Assert.Equal("26,7", vm.OccupancyRateValue);
    }

    [Fact]
    public async Task Empty_state_flags_follow_the_loaded_lists()
    {
        var vm = new OverviewViewModel(new FakeReportService(), new FakeLocalizationService());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.False(vm.HasParkedByVehicleType);
        Assert.False(vm.HasRecentSessions);

        await vm.LoadDataAsync();

        // The fake snapshot has two vehicle types but no recent sessions
        Assert.True(vm.HasParkedByVehicleType);
        Assert.False(vm.HasRecentSessions);
        Assert.Contains(nameof(OverviewViewModel.HasParkedByVehicleType), changed);
    }

    [Fact]
    public async Task Database_error_shows_the_localized_banner()
    {
        var vm = new OverviewViewModel(new FailingReportService(), new FakeLocalizationService());

        await vm.LoadDataAsync();

        Assert.Equal("Msg_Ov_LoadError", vm.ErrorMessage);
        Assert.True(vm.HasError);
        Assert.False(vm.IsLoading);
    }

    private static int VisitorParkedCount(OverviewViewModel vm)
    {
        var property = typeof(OverviewViewModel).GetProperty("VisitorParkedCount");
        Assert.True(property is not null, "OverviewViewModel.VisitorParkedCount does not exist (review r1 m3)");
        return (int)property!.GetValue(vm)!;
    }
}
