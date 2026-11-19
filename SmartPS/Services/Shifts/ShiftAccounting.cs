using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.Common;
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

        var code = await NewCodeAsync(db, DateTime.UtcNow, ct);
        db.FinancialTransactions.Add(new FinancialTransaction
        {
            TransactionCode = code,
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

    /// <summary>
    /// Mã giao dịch GD-yyMMdd-0001 (số thứ tự trong ngày, giờ Việt Nam). Gọi trong giao dịch có khoá nhật ký;
    /// tính cả giao dịch đã thêm vào DbContext nhưng chưa lưu.
    /// </summary>
    public static async Task<string> NewCodeAsync(SmartPsDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var prefix = DocumentCodes.DailyPrefix(DocumentCodes.FinancialTransaction, nowUtc);
        var saved = await db.FinancialTransactions.AsNoTracking()
            .Where(t => t.TransactionCode.StartsWith(prefix))
            .Select(t => t.TransactionCode)
            .ToListAsync(ct);
        var pending = db.FinancialTransactions.Local.Select(t => t.TransactionCode);
        return DocumentCodes.Format(prefix, DocumentCodes.NextNumber(prefix, saved.Concat(pending)), 4);
    }
}
