using SmartPS.DTOs.Shifts;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Services.Shifts;

public static class ShiftFinancialCalculator
{
    public static ShiftDashboardData BuildDashboard(Shift shift, IReadOnlyList<FinancialTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(transactions);

        var parkingFees = transactions.Where(x => x.Type == FinancialTransactionType.ParkingFee && x.Amount > 0).ToList();

        var cashRevenue = parkingFees.Where(x => x.PaymentMethod == PaymentMethod.Cash).Sum(x => x.Amount);
        var qrRevenue = parkingFees.Where(x => x.PaymentMethod == PaymentMethod.VietQR).Sum(x => x.Amount);
        var cardRevenue = parkingFees.Where(x => x.PaymentMethod == PaymentMethod.Card).Sum(x => x.Amount);
        var totalRevenue = parkingFees.Sum(x => x.Amount);

        var refundTotal = Math.Abs(transactions
            .Where(x => x.Type == FinancialTransactionType.Refund)
            .Sum(x => x.Amount));

        var adjustmentTotal = transactions
            .Where(x => x.Type == FinancialTransactionType.Adjustment)
            .Sum(x => x.Amount);

        var expectedCash = shift.BeginningCash + transactions
            .Where(x => x.PaymentMethod == PaymentMethod.Cash)
            .Sum(x => x.Amount);

        return new ShiftDashboardData
        {
            Shift = shift,
            CashRevenue = cashRevenue,
            QrRevenue = qrRevenue,
            CardRevenue = cardRevenue,
            TotalRevenue = totalRevenue,
            TransactionCount = transactions.Count,
            RefundTotal = refundTotal,
            AdjustmentTotal = adjustmentTotal,
            ExpectedCash = expectedCash,
            Transactions = transactions
        };
    }
}
