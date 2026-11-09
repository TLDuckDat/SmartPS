using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Customers;

public class TicketsService : ITicketsService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    private readonly ICurrentUserContext _currentUser;

    public TicketsService(IDbContextFactory<SmartPsDbContext> dbFactory, ICurrentUserContext currentUser)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
    }

    public async Task<decimal?> CalculateTicketFeeAsync(int vehicleTypeId, int durationMonths, CancellationToken ct = default)
    {
        if (durationMonths <= 0) return null;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rule = await db.PricingRules.FirstOrDefaultAsync(r => r.VehicleTypeId == vehicleTypeId, ct);
        if (rule == null)
        {
            return null; // Không có cấu hình giá -> trả về null để báo lỗi rõ ràng
        }

        return durationMonths switch
        {
            1 => rule.Monthly1Price,
            3 => rule.Monthly3Price > 0 ? rule.Monthly3Price : rule.Monthly1Price * 3,
            6 => rule.Monthly6Price > 0 ? rule.Monthly6Price : rule.Monthly1Price * 6,
            _ => rule.Monthly1Price * durationMonths
        };
    }

    public async Task<IReadOnlyList<MonthlyTicket>> GetTicketsAsync(string? keyword = null, bool onlyExpiringSoon = false, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.MonthlyTickets
            .AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.Vehicle)
            .Include(t => t.VehicleType)
            .AsQueryable();

        var now = DateTime.UtcNow;
        if (onlyExpiringSoon)
        {
            var threshold = now.AddDays(15);
            query = query.Where(t => t.Status == MonthlyTicketStatus.Active && t.EndDate > now && t.EndDate <= threshold);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToUpperInvariant();
            query = query.Where(t =>
                t.TicketCode.ToUpper().Contains(kw) ||
                t.RegisteredLicensePlate.ToUpper().Contains(kw) ||
                (t.Customer != null && t.Customer.FullName.ToUpper().Contains(kw)) ||
                (t.Customer != null && t.Customer.PhoneNumber.Contains(kw)));
        }

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MonthlyTicketHistory>> GetTicketHistoriesAsync(int ticketId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.MonthlyTicketHistories
            .AsNoTracking()
            .Where(h => h.TicketId == ticketId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PricingRule>> GetPricingRulesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PricingRules.AsNoTracking().ToListAsync(ct);
    }

    public async Task<OperationResult> IssueTicketAsync(IssueTicketRequest request, CancellationToken ct = default)
    {
        if (request.DurationMonths <= 0)
        {
            return OperationResult.Fail(OperationError.Validation, "Thời hạn vé phải lớn hơn 0.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var vehicle = await db.Vehicles.Include(v => v.OwnerCustomer).FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId, ct);
        if (vehicle == null || !vehicle.IsActive)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Customers_VehicleNotOwned"));
        }
        if (vehicle.OwnerCustomerId != request.CustomerId)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_DlgVehicleNotOwned"));
        }

        // Kiểm tra xe có vé Active hoặc Suspended (tạm khóa) còn hạn không
        var hasExistingValidTicket = await db.MonthlyTickets.AnyAsync(t => 
            t.VehicleId == request.VehicleId && 
            (t.Status == MonthlyTicketStatus.Active || t.Status == MonthlyTicketStatus.Suspended) && 
            t.EndDate > now, ct);
        if (hasExistingValidTicket)
        {
            return OperationResult.Fail(OperationError.Validation, "Phương tiện này hiện đang có vé tháng còn hiệu lực (đang sử dụng hoặc tạm khóa). Vui lòng xử lý vé cũ trước khi cấp vé mới.");
        }

        // Kiểm tra xe có trong Blacklist không
        var isBlacklisted = await db.BlacklistedVehicles.AnyAsync(b => b.LicensePlate == vehicle.LicensePlate && b.IsActive, ct);
        if (isBlacklisted)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_AlreadyBanned", vehicle.LicensePlate));
        }

        // Tự động tính cước từ PricingRules (không hardcode giá)
        var fee = await CalculateTicketFeeAsync(vehicle.VehicleTypeId, request.DurationMonths, ct);
        if (!fee.HasValue)
        {
            return OperationResult.Fail(OperationError.Validation, "Loại phương tiện này chưa được cấu hình bảng giá vé tháng trong hệ thống.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var ticket = new MonthlyTicket
            {
                CustomerId = request.CustomerId,
                VehicleId = request.VehicleId,
                RegisteredLicensePlate = vehicle.LicensePlate,
                VehicleTypeId = vehicle.VehicleTypeId,
                DurationMonths = request.DurationMonths,
                MonthlyPrice = fee.Value,
                StartDate = now,
                EndDate = now.AddMonths(request.DurationMonths),
                TicketCode = $"MT-{now:yyyyMM}-{Random.Shared.Next(1000, 9999)}",
                Status = MonthlyTicketStatus.Active,
                CreatedAt = now
            };

            db.MonthlyTickets.Add(ticket);
            await db.SaveChangesAsync(ct);

            // Ghi lịch sử cấp mới
            var history = new MonthlyTicketHistory
            {
                TicketId = ticket.TicketId,
                Action = TicketHistoryAction.Issue,
                NewVehicleId = vehicle.VehicleId,
                NewLicensePlate = vehicle.LicensePlate,
                NewEndDate = ticket.EndDate,
                AddedMonths = request.DurationMonths,
                Amount = fee.Value,
                PaymentMethod = request.PaymentMethod,
                Reason = "Cấp mới vé tháng",
                PerformedByUserId = _currentUser.User?.UserId,
                CreatedAt = now
            };
            db.MonthlyTicketHistories.Add(history);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            await LogAuditLineAsync("IssueTicket", $"TicketCode={ticket.TicketCode}, Plate={ticket.RegisteredLicensePlate}, Fee={fee.Value}, PaymentMethod={request.PaymentMethod}");
            return OperationResult.Ok(ticket.TicketCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            Debug.WriteLine($"[TicketsService.IssueTicketAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể hoàn tất cấp vé tháng do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }

    public async Task<OperationResult> RenewTicketAsync(RenewTicketRequest request, CancellationToken ct = default)
    {
        if (request.AddedMonths <= 0)
        {
            return OperationResult.Fail(OperationError.Validation, "Số tháng gia hạn phải lớn hơn 0.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == request.TicketId, ct);
        if (ticket == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        if (ticket.Status == MonthlyTicketStatus.Cancelled)
        {
            return OperationResult.Fail(OperationError.Validation, "Vé đã bị hủy, không thể gia hạn.");
        }

        // Tự động tính cước từ PricingRules (không hardcode giá)
        var fee = await CalculateTicketFeeAsync(ticket.VehicleTypeId, request.AddedMonths, ct);
        if (!fee.HasValue)
        {
            return OperationResult.Fail(OperationError.Validation, "Loại phương tiện này chưa được cấu hình bảng giá vé tháng trong hệ thống.");
        }

        var now = DateTime.UtcNow;
        DateTime baseDate = ticket.EndDate < now ? now : ticket.EndDate;
        DateTime newEnd = baseDate.AddMonths(request.AddedMonths);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var oldEndDate = ticket.EndDate;

            ticket.EndDate = newEnd;
            ticket.Status = MonthlyTicketStatus.Active;
            ticket.DurationMonths += request.AddedMonths;
            ticket.MonthlyPrice += fee.Value;

            var history = new MonthlyTicketHistory
            {
                TicketId = ticket.TicketId,
                Action = TicketHistoryAction.Renew,
                OldEndDate = oldEndDate,
                NewEndDate = newEnd,
                AddedMonths = request.AddedMonths,
                Amount = fee.Value,
                PaymentMethod = request.PaymentMethod,
                Reason = $"Gia hạn thêm {request.AddedMonths} tháng",
                PerformedByUserId = _currentUser.User?.UserId,
                CreatedAt = now
            };

            db.MonthlyTicketHistories.Add(history);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await LogAuditLineAsync("RenewTicket", $"TicketCode={ticket.TicketCode}, DurationAdded={request.AddedMonths}, Fee={fee.Value}, PaymentMethod={request.PaymentMethod}");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            Debug.WriteLine($"[TicketsService.RenewTicketAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể hoàn tất gia hạn vé do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }

    public async Task<OperationResult> ChangePlateAsync(ChangePlateRequest request, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == request.TicketId, ct);
        if (ticket == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        if (ticket.Status == MonthlyTicketStatus.Cancelled)
        {
            return OperationResult.Fail(OperationError.Validation, "Vé đã bị hủy, không thể đổi biển số.");
        }

        if (ticket.VehicleId == request.NewVehicleId)
        {
            return OperationResult.Fail(OperationError.Validation, "Phương tiện mới trùng với phương tiện hiện tại của vé.");
        }

        var newVehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == request.NewVehicleId, ct);
        if (newVehicle == null || !newVehicle.IsActive)
        {
            return OperationResult.Fail(OperationError.NotFound, "Phương tiện mới không tồn tại hoặc đã ngưng hoạt động.");
        }

        if (newVehicle.VehicleTypeId != ticket.VehicleTypeId)
        {
            return OperationResult.Fail(OperationError.Validation, "Không thể chuyển vé giữa hai phương tiện khác loại (ví dụ: từ xe máy sang ô tô). Vui lòng hủy vé cũ và cấp vé mới.");
        }

        // Kiểm tra xe mới có đang có vé Active nào khác không
        var now = DateTime.UtcNow;
        var hasActiveTicket = await db.MonthlyTickets.AnyAsync(t => 
            t.VehicleId == newVehicle.VehicleId && 
            t.TicketId != ticket.TicketId && 
            t.Status == MonthlyTicketStatus.Active && 
            t.EndDate > now, ct);
        if (hasActiveTicket)
        {
            return OperationResult.Fail(OperationError.Validation, "Phương tiện mới đã được đăng ký cho một vé tháng còn hiệu lực khác.");
        }

        // Kiểm tra xe mới có trong Blacklist không
        var isBlacklisted = await db.BlacklistedVehicles.AnyAsync(b => b.LicensePlate == newVehicle.LicensePlate && b.IsActive, ct);
        if (isBlacklisted)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_AlreadyBanned", newVehicle.LicensePlate));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var oldVehicleId = ticket.VehicleId;
            var oldPlate = ticket.RegisteredLicensePlate;

            ticket.VehicleId = newVehicle.VehicleId;
            ticket.RegisteredLicensePlate = newVehicle.LicensePlate;
            ticket.VehicleTypeId = newVehicle.VehicleTypeId;

            var history = new MonthlyTicketHistory
            {
                TicketId = ticket.TicketId,
                Action = TicketHistoryAction.ChangePlate,
                OldVehicleId = oldVehicleId,
                NewVehicleId = newVehicle.VehicleId,
                OldLicensePlate = oldPlate,
                NewLicensePlate = newVehicle.LicensePlate,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Đổi phương tiện / biển số" : request.Reason.Trim(),
                PerformedByUserId = _currentUser.User?.UserId,
                CreatedAt = now
            };

            db.MonthlyTicketHistories.Add(history);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await LogAuditLineAsync("ChangePlate", $"TicketCode={ticket.TicketCode}, OldPlate={oldPlate}, NewPlate={newVehicle.LicensePlate}, Reason={history.Reason}");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            Debug.WriteLine($"[TicketsService.ChangePlateAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể hoàn tất đổi biển số do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }

    public async Task<OperationResult> ToggleLockTicketAsync(int ticketId, string reason, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, ct);
        if (ticket == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        if (ticket.Status == MonthlyTicketStatus.Cancelled)
        {
            return OperationResult.Fail(OperationError.Validation, "Vé đã bị hủy, không thể khóa hoặc mở khóa.");
        }

        var now = DateTime.UtcNow;
        var willLock = ticket.Status == MonthlyTicketStatus.Active;

        // Nếu chuẩn bị mở khóa (Suspended -> Active), kiểm tra xem xe này có vé Active nào khác đang còn hạn không
        if (!willLock)
        {
            var hasOtherActiveTicket = await db.MonthlyTickets.AnyAsync(t =>
                t.VehicleId == ticket.VehicleId &&
                t.TicketId != ticket.TicketId &&
                t.Status == MonthlyTicketStatus.Active &&
                t.EndDate > now, ct);

            if (hasOtherActiveTicket)
            {
                return OperationResult.Fail(OperationError.Validation, "Phương tiện này hiện đã có một vé tháng khác đang hoạt động. Không thể mở khóa vé này.");
            }
        }

        var action = willLock ? TicketHistoryAction.Lock : TicketHistoryAction.Unlock;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            ticket.Status = willLock ? MonthlyTicketStatus.Suspended : MonthlyTicketStatus.Active;

            var history = new MonthlyTicketHistory
            {
                TicketId = ticket.TicketId,
                Action = action,
                Reason = string.IsNullOrWhiteSpace(reason) ? (willLock ? "Khóa vé tạm thời" : "Mở khóa vé hoạt động lại") : reason.Trim(),
                PerformedByUserId = _currentUser.User?.UserId,
                CreatedAt = now
            };

            db.MonthlyTicketHistories.Add(history);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await LogAuditLineAsync(willLock ? "LockTicket" : "UnlockTicket", $"TicketCode={ticket.TicketCode}, Status={ticket.Status}");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            Debug.WriteLine($"[TicketsService.ToggleLockTicketAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể thay đổi trạng thái vé do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }

    public async Task<OperationResult> CancelTicketAsync(int ticketId, string reason, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ticket = await db.MonthlyTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId, ct);
        if (ticket == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        if (ticket.Status == MonthlyTicketStatus.Cancelled)
        {
            return OperationResult.Fail(OperationError.Validation, "Vé này đã ở trạng thái bị hủy từ trước.");
        }

        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            ticket.Status = MonthlyTicketStatus.Cancelled;

            var history = new MonthlyTicketHistory
            {
                TicketId = ticket.TicketId,
                Action = TicketHistoryAction.Cancel,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Hủy vé tháng" : reason.Trim(),
                PerformedByUserId = _currentUser.User?.UserId,
                CreatedAt = now
            };

            db.MonthlyTicketHistories.Add(history);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await LogAuditLineAsync("CancelTicket", $"TicketCode={ticket.TicketCode}, Status=Cancelled, Reason={history.Reason}");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            Debug.WriteLine($"[TicketsService.CancelTicketAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể hủy vé do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }

    private static async Task LogAuditLineAsync(string action, string details)
    {
        try
        {
            var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"{action}\",\"details\":\"{details}\"}}\n";
            await File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);
        }
        catch
        {
        }
    }
}
