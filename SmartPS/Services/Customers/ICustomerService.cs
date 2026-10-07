using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.Customers;

/// <summary>
/// Khách hàng và phương tiện. Các thao tác đọc cần Customer.View và ném PermissionDeniedException khi bị từ chối;
/// các thao tác ghi cần Customer.Manage và trả về kết quả thay vì ném ngoại lệ.
/// </summary>
public interface ICustomerService
{
    Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default);

    Task<CustomerPage> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default);

    Task<CustomerSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<CustomerDetails?> GetDetailsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<OperationResult<int>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateCustomerAsync(int customerId, CustomerUpsertRequest request, CancellationToken cancellationToken = default);

    Task<OperationResult> SetCustomerActiveAsync(int customerId, bool isActive, CancellationToken cancellationToken = default);

    Task<OperationResult<int>> AddVehicleAsync(int customerId, NewVehicle vehicle, CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveVehicleAsync(int customerVehicleId, CancellationToken cancellationToken = default);
}
