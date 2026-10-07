using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.Customers;

/// <summary>Vé tháng: tạo, gia hạn, tạm ngưng và kích hoạt lại. Mọi thao tác ghi cần Customer.Manage và ghi một dòng mua cho mỗi kỳ trả phí.</summary>
public interface IMonthlyTicketService
{
    Task<IReadOnlyList<MonthlyTicketPlan>> GetActivePlansAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<int>> CreateTicketAsync(CreateTicketRequest request, CancellationToken cancellationToken = default);

    Task<OperationResult> RenewTicketAsync(int ticketId, int planId, CancellationToken cancellationToken = default);

    Task<OperationResult> SuspendTicketAsync(int ticketId, string? reason = null, CancellationToken cancellationToken = default);

    Task<OperationResult> ResumeTicketAsync(int ticketId, CancellationToken cancellationToken = default);
}
