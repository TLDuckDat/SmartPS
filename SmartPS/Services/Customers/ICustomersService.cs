using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.Customers;

public record CreateHouseholdRequest(string ApartmentCode, int? MaxVehicles, string? Notes);
public record CreateCustomerRequest(string FullName, string PhoneNumber, string? IdentityCard, int? HouseholdId, string? InitialPlate, int? InitialVehicleTypeId);

public interface ICustomersService
{
    Task<IReadOnlyList<Customer>> GetCustomersAsync(string? keyword = null, CancellationToken ct = default);
    Task<IReadOnlyList<Household>> GetHouseholdsAsync(string? keyword = null, CancellationToken ct = default);
    Task<IReadOnlyList<BlacklistedVehicle>> GetBlacklistAsync(string? keyword = null, CancellationToken ct = default);

    Task<OperationResult> AddHouseholdAsync(CreateHouseholdRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteHouseholdAsync(int householdId, CancellationToken ct = default);

    Task<OperationResult> AddCustomerAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteCustomerAsync(int customerId, CancellationToken ct = default);

    Task<OperationResult> AddBlacklistAsync(string plate, string reason, string? notes, int? customerId, CancellationToken ct = default);
    Task<OperationResult> ToggleBlacklistAsync(int blacklistId, CancellationToken ct = default);
    Task<OperationResult> DeleteBlacklistAsync(int blacklistId, CancellationToken ct = default);
}
