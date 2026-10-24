using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Shifts;

// Used inside the checkout/payment transaction so the session and ledger cannot diverge.
public static class ShiftAccounting
{
    public static async Task<Shift?> FindActiveShiftAsync(SmartPsDbContext db, int actorUserId, CancellationToken ct)
    {
        if (actorUserId <= 0) return null;
        return await db.Shifts.FirstOrDefaultAsync(
            s => s.OpenedByUserId == actorUserId && s.Status == ShiftStatus.Active, ct);
    }

    public static async Task AddParkingFeeAsync(
        SmartPsDbContext db, Shift shift, int actorUserId, int sessionId,
        PaymentMethod method, decimal amount, string? reference, CancellationToken ct)
    {
        if (await db.FinancialTransactions.AnyAsync(
            t => t.ParkingSessionId == sessionId && t.Type == FinancialTransactionType.ParkingFee, ct))
            throw new InvalidOperationException(AppText.Get("Msg_Shift_SessionAlreadyCharged"));

        db.FinancialTransactions.Add(new FinancialTransaction
        {
            TransactionCode = NewCode(),
            ShiftId = shift.ShiftId,
            ParkingSessionId = sessionId,
            CreatedByUserId = actorUserId,
            Type = FinancialTransactionType.ParkingFee,
            PaymentMethod = method,
            Amount = method == PaymentMethod.Free ? 0 : amount,
            ReferenceCode = reference,
            CreatedAt = DateTime.UtcNow
        });
    }

    public static string NewCode() => $"TX-{Guid.NewGuid():N}";
}
