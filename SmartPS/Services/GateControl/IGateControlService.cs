using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

public interface IGateControlService
{
    Task<List<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default);
    Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, CancellationToken cancellationToken = default);
    Task<MonthlyTicket?> FindActiveMonthlyTicketAsync(string licensePlate, CancellationToken cancellationToken = default);
    Task<GateCheckInResult> ProcessCheckInAsync(GateCheckInRequest request, CancellationToken cancellationToken = default);
    Task<GateCheckOutCalculationResult> CalculateCheckOutAsync(string licensePlate, CancellationToken cancellationToken = default);
    Task<GateCheckOutResult> CompleteCheckOutAsync(GateCheckOutRequest request, CancellationToken cancellationToken = default);
    Task<List<ParkingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default);
    Task<List<ParkingSlot>> GetAllSlotsAsync(CancellationToken cancellationToken = default);
    Task<List<ParkingSession>> GetAllSessionsHistoryAsync(CancellationToken cancellationToken = default);
    Task<OverviewKpiData> GetOverviewKpiAsync(CancellationToken cancellationToken = default);
}
