using SmartPS.Models.Shifts;

namespace SmartPS.DTOs.Shifts;

public class ShiftDashboardData
{
    public Shift Shift { get; init; } = null!;
    public decimal CashRevenue { get; init; }
    public decimal QrRevenue { get; init; }
    public decimal CardRevenue { get; init; }
    public decimal TotalRevenue { get; init; }
    public int TransactionCount { get; init; }
    public decimal RefundTotal { get; init; }
    public decimal AdjustmentTotal { get; init; }
    public decimal ExpectedCash { get; init; }
    public IReadOnlyList<FinancialTransaction> Transactions { get; init; } = Array.Empty<FinancialTransaction>();
}
