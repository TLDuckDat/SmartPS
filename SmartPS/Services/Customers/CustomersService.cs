using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Customers;

public class CustomersService : ICustomersService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    private readonly IVehiclesService _vehiclesService;

    public CustomersService(IDbContextFactory<SmartPsDbContext> dbFactory, IVehiclesService vehiclesService)
    {
        _dbFactory = dbFactory;
        _vehiclesService = vehiclesService;
    }

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(string? keyword = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Customers
            .AsNoTracking()
            .Include(c => c.Household)
            .Include(c => c.MonthlyTickets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToUpperInvariant();
            query = query.Where(c =>
                c.FullName.ToUpper().Contains(kw) ||
                c.PhoneNumber.Contains(kw) ||
                (c.IdentityCard != null && c.IdentityCard.Contains(kw)) ||
                (c.Household != null && c.Household.ApartmentCode.ToUpper().Contains(kw)));
        }

        return await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Household>> GetHouseholdsAsync(string? keyword = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Households.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToUpperInvariant();
            query = query.Where(h =>
                h.ApartmentCode.ToUpper().Contains(kw) ||
                (h.Notes != null && h.Notes.ToUpper().Contains(kw)));
        }

        return await query.OrderByDescending(h => h.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BlacklistedVehicle>> GetBlacklistAsync(string? keyword = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.BlacklistedVehicles
            .AsNoTracking()
            .Include(b => b.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToUpperInvariant();
            query = query.Where(b =>
                b.LicensePlate.ToUpper().Contains(kw) ||
                (b.Reason != null && b.Reason.ToUpper().Contains(kw)) ||
                (b.Notes != null && b.Notes.ToUpper().Contains(kw)) ||
                (b.Customer != null && b.Customer.FullName.ToUpper().Contains(kw)));
        }

        return await query.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
    }

    public async Task<OperationResult> AddHouseholdAsync(CreateHouseholdRequest request, CancellationToken ct = default)
    {
        var code = request.ApartmentCode?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_DlgAptRequired"));
        }

        if (request.MaxVehicles.HasValue && request.MaxVehicles.Value < 1)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_DlgAptMaxVehicles"));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.Households.AnyAsync(h => h.ApartmentCode == code, ct);
        if (exists)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_ApartmentExists"));
        }

        var household = new Household
        {
            ApartmentCode = code,
            MaxVehicles = request.MaxVehicles,
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Households.Add(household);
        await db.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteHouseholdAsync(int householdId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var h = await db.Households.FirstOrDefaultAsync(x => x.HouseholdId == householdId, ct);
        if (h == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        // Chuyển các khách hàng thuộc căn hộ này sang khách vãng lai
        var customers = await db.Customers.Where(c => c.HouseholdId == householdId).ToListAsync(ct);
        foreach (var c in customers)
        {
            c.HouseholdId = null;
            c.Type = CustomerType.External;
        }

        db.Households.Remove(h);
        await db.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> AddCustomerAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        var name = request.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_DlgNameRequired"));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Nếu có đăng ký xe ban đầu
        var normPlate = _vehiclesService.NormalizePlate(request.InitialPlate ?? string.Empty);
        if (!string.IsNullOrEmpty(normPlate))
        {
            if (normPlate.Length > 20)
            {
                return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Common_PlateTooLong"));
            }

            var plateExists = await db.Vehicles.AnyAsync(v => v.LicensePlate == normPlate && v.IsActive, ct);
            if (plateExists)
            {
                return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_VehicleExists", normPlate));
            }

            if (request.HouseholdId.HasValue)
            {
                var canAdd = await _vehiclesService.CheckVehicleLimitAsync(request.HouseholdId.Value, null, ct);
                if (!canAdd)
                {
                    var h = await db.Households.FirstOrDefaultAsync(x => x.HouseholdId == request.HouseholdId.Value, ct);
                    var settings = await db.ParkingSettings.FirstOrDefaultAsync(ct);
                    int max = h?.MaxVehicles ?? settings?.DefaultMaxVehiclesPerHousehold ?? 2;
                    return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_HouseholdLimitAdd", max));
                }
            }
        }

        var customer = new Customer
        {
            FullName = name,
            PhoneNumber = request.PhoneNumber?.Trim() ?? string.Empty,
            IdentityCard = request.IdentityCard?.Trim(),
            HouseholdId = request.HouseholdId,
            Type = request.HouseholdId.HasValue ? CustomerType.Resident : CustomerType.External,
            CreatedAt = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(normPlate))
        {
            var vehicle = new Vehicle
            {
                LicensePlate = normPlate,
                VehicleTypeId = request.InitialVehicleTypeId ?? 1,
                OwnerCustomerId = customer.CustomerId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync(ct);
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteCustomerAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var c = await db.Customers.FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
            if (c == null)
            {
                return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
            }

            // Chặn không cho xóa nếu khách đang có vé tháng Active còn hạn
            var now = DateTime.UtcNow;
            var hasActiveTicket = await db.MonthlyTickets.AnyAsync(t => 
                t.CustomerId == customerId && 
                t.Status == MonthlyTicketStatus.Active && 
                t.EndDate > now, ct);

            if (hasActiveTicket)
            {
                return OperationResult.Fail(OperationError.Validation, "Khách hàng này đang có vé tháng còn hiệu lực. Vui lòng xử lý vé tháng trước khi xóa khách hàng.");
            }

            // Hủy liên kết phiên đỗ xe cũ
            var sessions = await db.ParkingSessions.Where(s => s.CustomerId == customerId).ToListAsync(ct);
            foreach (var s in sessions)
            {
                s.CustomerId = null;
            }

            // Vô hiệu hóa các xe của khách (xóa mềm xe)
            var vehicles = await db.Vehicles.Where(v => v.OwnerCustomerId == customerId).ToListAsync(ct);
            foreach (var v in vehicles)
            {
                v.IsActive = false;
            }

            db.Customers.Remove(c);
            await db.SaveChangesAsync(ct);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomersService.DeleteCustomerAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể xóa khách hàng do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }

    public async Task<OperationResult> AddBlacklistAsync(string plate, string reason, string? notes, int? customerId, CancellationToken ct = default)
    {
        var cleanPlate = _vehiclesService.NormalizePlate(plate);
        if (string.IsNullOrEmpty(cleanPlate))
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Common_InvalidPlate"));
        }
        if (cleanPlate.Length > 20)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Common_PlateTooLong"));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.BlacklistedVehicles.FirstOrDefaultAsync(b => b.LicensePlate == cleanPlate, ct);
        if (existing != null)
        {
            if (existing.IsActive)
            {
                return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_AlreadyBanned", cleanPlate));
            }

            // Kích hoạt lại xe đã từng bị cấm
            existing.IsActive = true;
            existing.Reason = reason;
            existing.Notes = notes;
            existing.CustomerId = customerId;
            existing.CreatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await LogAuditLineAsync("BlacklistReactivate", $"Plate={cleanPlate}, Reason={reason}");
            return OperationResult.Ok();
        }

        var item = new BlacklistedVehicle
        {
            LicensePlate = cleanPlate,
            Reason = reason,
            Notes = notes,
            CustomerId = customerId,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.BlacklistedVehicles.Add(item);
        await db.SaveChangesAsync(ct);

        await LogAuditLineAsync("BlacklistAdd", $"Plate={cleanPlate}, Reason={reason}, Notes={notes}");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ToggleBlacklistAsync(int blacklistId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var b = await db.BlacklistedVehicles.FirstOrDefaultAsync(x => x.BlacklistId == blacklistId, ct);
        if (b == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        b.IsActive = !b.IsActive;
        await db.SaveChangesAsync(ct);

        var action = b.IsActive ? "BlacklistEnable" : "BlacklistDisable";
        await LogAuditLineAsync(action, $"Plate={b.LicensePlate}, IsActive={b.IsActive}");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteBlacklistAsync(int blacklistId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var b = await db.BlacklistedVehicles.FirstOrDefaultAsync(x => x.BlacklistId == blacklistId, ct);
        if (b == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
        }

        // Xóa mềm: đánh dấu IsActive = false
        b.IsActive = false;
        await db.SaveChangesAsync(ct);

        await LogAuditLineAsync("BlacklistSoftDelete", $"Plate={b.LicensePlate}");
        return OperationResult.Ok();
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
