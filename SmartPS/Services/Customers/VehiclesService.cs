using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Customers;

public class VehiclesService : IVehiclesService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;

    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;

    public VehiclesService(IDbContextFactory<SmartPsDbContext> dbFactory, IAuthorizationGuard guard, IAuditService audit)
    {
        _dbFactory = dbFactory;
        _guard = guard;
        _audit = audit;
    }

    private Task DemandAsync(CancellationToken ct, string? entityId = null)
        => _guard.DemandAsync(Permissions.ParkingConfigure, "Vehicle", entityId, ct);

    public string NormalizePlate(string licensePlate)
    {
        if (string.IsNullOrWhiteSpace(licensePlate)) return string.Empty;
        return Regex.Replace(licensePlate.Trim(), @"[^a-zA-Z0-9]", "").ToUpperInvariant();
    }

    public async Task<bool> CheckVehicleLimitAsync(int householdId, int? excludeVehicleId = null, CancellationToken ct = default)
    {
        await DemandAsync(ct);
        return await CheckVehicleLimitCoreAsync(householdId, excludeVehicleId, ct);
    }

    private async Task<bool> CheckVehicleLimitCoreAsync(int householdId, int? excludeVehicleId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var household = await db.Households.FirstOrDefaultAsync(h => h.HouseholdId == householdId, ct);
        if (household == null) return false;

        var settings = await db.ParkingSettings.FirstOrDefaultAsync(ct);
        int max = household.MaxVehicles ?? settings?.DefaultMaxVehiclesPerHousehold ?? 2;

        int current = await db.Vehicles.CountAsync(v => 
            v.OwnerCustomer != null && 
            v.OwnerCustomer.HouseholdId == householdId && 
            v.IsActive && 
            (excludeVehicleId == null || v.VehicleId != excludeVehicleId), ct);

        return current < max;
    }

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken ct = default)
    {
        await DemandAsync(ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles
            .AsNoTracking()
            .Include(v => v.VehicleType)
            .Include(v => v.OwnerCustomer)
            .Where(v => v.IsActive)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesByCustomerAsync(int customerId, CancellationToken ct = default)
    {
        await DemandAsync(ct, customerId.ToString());
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles
            .AsNoTracking()
            .Include(v => v.VehicleType)
            .Where(v => v.OwnerCustomerId == customerId && v.IsActive)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken ct = default)
    {
        await DemandAsync(ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.VehicleTypes
            .AsNoTracking()
            .OrderBy(vt => vt.VehicleTypeId)
            .ToListAsync(ct);
    }

    public async Task<OperationResult> AddVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default)
    {
        await DemandAsync(ct);
        var normPlate = NormalizePlate(request.LicensePlate);
        if (string.IsNullOrEmpty(normPlate))
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Common_InvalidPlate"));
        }
        if (normPlate.Length > 20)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Common_PlateTooLong"));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        
        // Kiểm tra khách hàng tồn tại
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId, ct);
        if (customer == null)
        {
            return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Customers_DlgChooseOwner"));
        }

        // Kiểm tra trùng biển số đang active
        var plateExists = await db.Vehicles.AnyAsync(v => v.LicensePlate == normPlate && v.IsActive, ct);
        if (plateExists)
        {
            return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_VehicleExists", normPlate));
        }

        // Kiểm tra quota hộ gia đình nếu là cư dân
        if (customer.HouseholdId.HasValue)
        {
            var canAdd = await CheckVehicleLimitCoreAsync(customer.HouseholdId.Value, null, ct);
            if (!canAdd)
            {
                var h = await db.Households.FirstOrDefaultAsync(x => x.HouseholdId == customer.HouseholdId.Value, ct);
                var settings = await db.ParkingSettings.FirstOrDefaultAsync(ct);
                int max = h?.MaxVehicles ?? settings?.DefaultMaxVehiclesPerHousehold ?? 2;
                return OperationResult.Fail(OperationError.Validation, AppText.Get("Msg_Customers_HouseholdLimit", max));
            }
        }

        var vehicle = new Vehicle
        {
            LicensePlate = normPlate,
            VehicleTypeId = request.VehicleTypeId,
            OwnerCustomerId = request.CustomerId,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await using (var tx = await _audit.BeginAuditedTransactionAsync(db, IsolationLevel.ReadCommitted, ct))
        {
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync(ct);
            await _audit.AppendChangeAsync(db, AuditActions.VehicleCreate, "Vehicle", vehicle.VehicleId, null, new
            {
                vehicle.LicensePlate,
                vehicle.VehicleTypeId,
                vehicle.OwnerCustomerId,
                vehicle.IsActive
            }, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteVehicleAsync(int vehicleId, CancellationToken ct = default)
    {
        await DemandAsync(ct, vehicleId.ToString());
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == vehicleId, ct);
            if (vehicle == null)
            {
                return OperationResult.Fail(OperationError.NotFound, AppText.Get("Msg_Common_NotFound"));
            }

            // Chặn không cho xóa nếu xe đang có vé tháng Active còn hạn
            var now = DateTime.UtcNow;
            var hasActiveTicket = await db.MonthlyTickets.AnyAsync(t => 
                t.VehicleId == vehicleId && 
                t.Status == MonthlyTicketStatus.Active && 
                t.EndDate > now, ct);

            if (hasActiveTicket)
            {
                return OperationResult.Fail(OperationError.Validation, "Phương tiện này đang có vé tháng còn hiệu lực. Vui lòng hủy hoặc đổi biển số vé trước khi xóa phương tiện.");
            }

            // Kiểm tra xem xe đã từng có vé tháng cũ hoặc phiên gửi xe chưa
            var hasHistory = await db.MonthlyTickets.AnyAsync(t => t.VehicleId == vehicleId, ct)
                || await db.ParkingSessions.AnyAsync(s => s.LicensePlate == vehicle.LicensePlate, ct);

            var oldSnapshot = new { vehicle.LicensePlate, vehicle.VehicleTypeId, vehicle.OwnerCustomerId, vehicle.IsActive };

            await using (var tx = await _audit.BeginAuditedTransactionAsync(db, IsolationLevel.ReadCommitted, ct))
            {
                object newSnapshot;
                if (hasHistory)
                {
                    // Xóa mềm: đánh dấu xe ngưng hoạt động để bảo toàn lịch sử
                    vehicle.IsActive = false;
                    newSnapshot = new { vehicle.LicensePlate, vehicle.VehicleTypeId, vehicle.OwnerCustomerId, IsActive = false, Deleted = false };
                }
                else
                {
                    // Xe chưa từng phát sinh dữ liệu: cho phép xóa cứng khỏi DB
                    db.Vehicles.Remove(vehicle);
                    newSnapshot = new { Deleted = true };
                }

                await db.SaveChangesAsync(ct);
                await _audit.AppendChangeAsync(db, AuditActions.VehicleDelete, "Vehicle", vehicleId, oldSnapshot, newSnapshot, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VehiclesService.DeleteVehicleAsync Error]: {ex}");
            return OperationResult.Fail(OperationError.DatabaseError, "Không thể xóa phương tiện do lỗi cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
    }
}
