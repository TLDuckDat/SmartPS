using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.TestSupport;

/// <summary>Records calls; returns no plans and Ok for every write.</summary>
public sealed class FakeMonthlyTicketService : IMonthlyTicketService
{
    public List<string> Calls { get; } = new();

    public Task<IReadOnlyList<MonthlyTicketPlan>> GetActivePlansAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetActivePlansAsync));
        return Task.FromResult<IReadOnlyList<MonthlyTicketPlan>>(Array.Empty<MonthlyTicketPlan>());
    }

    public Task<OperationResult<int>> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CreateTicketAsync));
        return Task.FromResult(OperationResult<int>.Ok(1));
    }

    public Task<OperationResult> RenewTicketAsync(int ticketId, int planId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(RenewTicketAsync));
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> SuspendTicketAsync(int ticketId, string? reason = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(SuspendTicketAsync));
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> ResumeTicketAsync(int ticketId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ResumeTicketAsync));
        return Task.FromResult(OperationResult.Ok());
    }
}
