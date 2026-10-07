using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.TestSupport;

/// <summary>Records calls; returns one listed customer, empty details and Ok for every write.</summary>
public sealed class FakeCustomerService : ICustomerService
{
    public List<string> Calls { get; } = new();

    public static readonly CustomerListItem SampleItem = new(
        1, "Khách mẫu", "0988000111", true, "A-0101", "A", CustomerType.Resident, true,
        new[] { "51F00011" }, "MT-202610-0001", DateTime.UtcNow.AddDays(20), TicketDisplayStatus.Active);

    public Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetVehicleTypesAsync));
        return Task.FromResult<IReadOnlyList<VehicleType>>(new[] { new VehicleType { VehicleTypeId = 1, TypeName = "Xe máy" } });
    }

    public Task<CustomerPage> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(SearchAsync));
        return Task.FromResult(new CustomerPage(new[] { SampleItem }, 1, query.PageIndex, query.PageSize));
    }

    public Task<CustomerSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetSummaryAsync));
        return Task.FromResult(new CustomerSummary(1, 1, 1, 0, 120_000m));
    }

    public Task<CustomerDetails?> GetDetailsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(GetDetailsAsync));
        return Task.FromResult<CustomerDetails?>(null);
    }

    public Task<OperationResult<int>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(CreateCustomerAsync));
        return Task.FromResult(OperationResult<int>.Ok(1));
    }

    public Task<OperationResult> UpdateCustomerAsync(int customerId, CustomerUpsertRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(UpdateCustomerAsync));
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> SetCustomerActiveAsync(int customerId, bool isActive, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(SetCustomerActiveAsync));
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult<int>> AddVehicleAsync(int customerId, NewVehicle vehicle, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(AddVehicleAsync));
        return Task.FromResult(OperationResult<int>.Ok(1));
    }

    public Task<OperationResult> RemoveVehicleAsync(int customerVehicleId, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(RemoveVehicleAsync));
        return Task.FromResult(OperationResult.Ok());
    }
}
