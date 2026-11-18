using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.Customers;

public record CreateVehicleRequest(int CustomerId, string LicensePlate, int VehicleTypeId);

public interface IVehiclesService
{
    Task<bool> CheckVehicleLimitAsync(int householdId, int? excludeVehicleId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Vehicle>> GetVehiclesByCustomerAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken ct = default);
    Task<OperationResult> AddVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteVehicleAsync(int vehicleId, CancellationToken ct = default);
    string NormalizePlate(string licensePlate);
}
