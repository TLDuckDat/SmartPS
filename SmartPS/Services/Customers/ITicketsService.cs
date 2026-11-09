using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.Customers;

public record IssueTicketRequest(int CustomerId, int VehicleId, int DurationMonths, PaymentMethod PaymentMethod);
public record RenewTicketRequest(int TicketId, int AddedMonths, PaymentMethod PaymentMethod);
public record ChangePlateRequest(int TicketId, int NewVehicleId, string Reason);

public interface ITicketsService
{
    Task<IReadOnlyList<MonthlyTicket>> GetTicketsAsync(string? keyword = null, bool onlyExpiringSoon = false, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyTicketHistory>> GetTicketHistoriesAsync(int ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<PricingRule>> GetPricingRulesAsync(CancellationToken ct = default);
    Task<decimal?> CalculateTicketFeeAsync(int vehicleTypeId, int durationMonths, CancellationToken ct = default);
    Task<OperationResult> IssueTicketAsync(IssueTicketRequest request, CancellationToken ct = default);
    Task<OperationResult> RenewTicketAsync(RenewTicketRequest request, CancellationToken ct = default);
    Task<OperationResult> ChangePlateAsync(ChangePlateRequest request, CancellationToken ct = default);
    Task<OperationResult> ToggleLockTicketAsync(int ticketId, string reason, CancellationToken ct = default);
    Task<OperationResult> CancelTicketAsync(int ticketId, string reason, CancellationToken ct = default);
}
