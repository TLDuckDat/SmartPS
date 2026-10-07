using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.TestSupport;

/// <summary>Records calls; returns one active entry and Ok for every write.</summary>
public sealed class FakeBlacklistService : IBlacklistService
{
    public List<string> Calls { get; } = new();

    public static readonly BlacklistEntryDto SampleEntry = new(
        7, "29A99999", "Nợ phí", DateTime.UtcNow.AddDays(-1), 1, "admin", true, null, null, null, null);

    public Task<IReadOnlyList<BlacklistEntryDto>> GetEntriesAsync(BlacklistQuery query, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetEntriesAsync));
        return Task.FromResult<IReadOnlyList<BlacklistEntryDto>>(new[] { SampleEntry });
    }

    public Task<OperationResult<int>> AddAsync(string licensePlate, string reason, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(AddAsync));
        return Task.FromResult(OperationResult<int>.Ok(1));
    }

    public Task<OperationResult> RemoveAsync(int blacklistEntryId, string removeReason, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(RemoveAsync));
        return Task.FromResult(OperationResult.Ok());
    }
}
