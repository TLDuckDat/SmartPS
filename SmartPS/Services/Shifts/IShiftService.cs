using SmartPS.DTOs.Shifts;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Services.Shifts;

public interface IShiftService
{
    Task<Shift?> GetActiveShiftAsync(int userId, CancellationToken cancellationToken = default);
    Task<Shift> OpenShiftAsync(int userId, decimal beginningCash, CancellationToken cancellationToken = default);
    Task<ShiftDashboardData> GetShiftDashboardAsync(int shiftId, CancellationToken cancellationToken = default);
    Task<Shift> CloseShiftAsync(int shiftId, int actorUserId, decimal actualCash, CancellationToken cancellationToken = default);
    Task<List<Shift>> GetShiftHistoryAsync(ShiftFilterRequest filter, CancellationToken cancellationToken = default);
    Task<Shift?> GetShiftDetailAsync(int shiftId, CancellationToken cancellationToken = default);
    Task<List<FinancialTransaction>> GetTransactionsAsync(FinancialTransactionFilterRequest? filter = null, CancellationToken cancellationToken = default);
    Task<FinancialTransaction> CreateManualTransactionAsync(
        int shiftId,
        int createdByUserId,
        FinancialTransactionType type,
        PaymentMethod paymentMethod,
        decimal amount,
        string? referenceCode,
        string? note,
        CancellationToken cancellationToken = default);
    Task<Shift> ReviewShiftAsync(int shiftId, int managerUserId, string? note, CancellationToken cancellationToken = default);
}
