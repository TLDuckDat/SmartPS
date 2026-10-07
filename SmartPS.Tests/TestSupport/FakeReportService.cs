using SmartPS.Models.Parking;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Records every report request and returns canned data. <see cref="NextCallAsync"/> returns a task that completes when
/// the next <see cref="GetReportAsync"/> call arrives (the TaskCompletionSource is replaced on each call), so debounce
/// tests never depend on wall-clock time (plan m7).
/// </summary>
public sealed class FakeReportService : IReportService
{
    private readonly object _gate = new();
    private TaskCompletionSource<ReportFilter> _nextCall = NewSignal();

    public List<ReportFilter> ReportCalls { get; } = new();

    public List<(ReportFilter Filter, int MaxRows)> SessionDetailCalls { get; } = new();

    public int FilterOptionsCalls { get; private set; }

    public int OverviewCalls { get; private set; }

    public int RecentSessionsCalls { get; private set; }

    /// <summary>Builds the result for a requested filter. Default: <see cref="ReportResults.Sample"/> carrying that filter.</summary>
    public Func<ReportFilter, ReportResult> ResultFactory { get; set; } = f => ReportResults.Sample(filter: f);

    public SessionDetailPage SessionPage { get; set; } = ReportResults.Sessions();

    /// <summary>When set, <see cref="GetReportAsync"/> throws it (after recording the call).</summary>
    public Exception? ReportException { get; set; }

    /// <summary>When set, <see cref="GetReportAsync"/> waits for this task before returning (used to keep an export in flight).</summary>
    public Task? ReportGate { get; set; }

    public ReportFilterOptions Options { get; set; } = new(
        new[] { new LookupItem(1, "Xe máy"), new LookupItem(2, "Xe ô tô"), new LookupItem(3, "Xe đạp / Xe điện") },
        new[] { new LookupItem(1, "Khu A"), new LookupItem(2, "Khu B") });

    public Task<ReportFilter> NextCallAsync()
    {
        lock (_gate)
        {
            return _nextCall.Task;
        }
    }

    public Task<ReportFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        FilterOptionsCalls++;
        return Task.FromResult(Options);
    }

    public async Task<ReportResult> GetReportAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<ReportFilter> signal;
        lock (_gate)
        {
            ReportCalls.Add(filter);
            signal = _nextCall;
            _nextCall = NewSignal();
        }

        signal.TrySetResult(filter);

        if (ReportGate is not null)
        {
            await ReportGate;
        }

        if (ReportException is not null)
        {
            throw ReportException;
        }

        return ResultFactory(filter);
    }

    public Task<SessionDetailPage> GetSessionDetailsAsync(ReportFilter filter, int maxRows = ReportLimits.MaxExportSessionRows,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            SessionDetailCalls.Add((filter, maxRows));
        }

        return Task.FromResult(SessionPage);
    }

    public Task<OverviewSnapshot> GetOverviewSnapshotAsync(CancellationToken cancellationToken = default)
    {
        OverviewCalls++;
        return Task.FromResult(new OverviewSnapshot(
            ReportResults.Day1, 30, 20, 2, 8, 26.7, 12, 10, 150_000m,
            new[] { new VehicleTypeCount(1, "Xe máy", 6), new VehicleTypeCount(2, "Xe ô tô", 2) }, 3, 1, 4));
    }

    public Task<IReadOnlyList<ParkingSession>> GetRecentSessionsAsync(int count = 15, CancellationToken cancellationToken = default)
    {
        RecentSessionsCalls++;
        return Task.FromResult<IReadOnlyList<ParkingSession>>(Array.Empty<ParkingSession>());
    }

    private static TaskCompletionSource<ReportFilter> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
