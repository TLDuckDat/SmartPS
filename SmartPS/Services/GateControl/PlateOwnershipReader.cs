using Microsoft.EntityFrameworkCore;
using SmartPS.Data;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Tra chủ xe theo biển số. Biển số lưu thô được chuẩn hoá trong SQL (bỏ mọi ký tự không phải chữ/số, chữ hoa)
/// và so với biển số đã chuẩn hoá của cổng. Chỉ đọc, không ghi.
/// </summary>
public sealed class PlateOwnershipReader : IPlateOwnershipReader
{
    public PlateOwnershipReader()
    {
    }

    public async Task<PlateOwnership> ReadAsync(SmartPsDbContext db, string normalizedPlate, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrEmpty(normalizedPlate))
        {
            return PlateOwnership.None(normalizedPlate);
        }

        var norm = normalizedPlate;

        // Mọi biến thể biển số (khác dấu gạch/chấm/khoảng trắng) của xe đang hoạt động; mới nhất đứng đầu.
        var vehicles = await db.Vehicles.FromSql($"""
                SELECT * FROM "Vehicles"
                WHERE "IsActive" AND upper(regexp_replace("LicensePlate", '[^a-zA-Z0-9]', '', 'g')) = {norm}
                """)
            .AsNoTracking()
            .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.VehicleId)
            .Select(v => new
            {
                v.VehicleId,
                v.VehicleTypeId,
                v.OwnerCustomerId,
                Name = v.OwnerCustomer!.FullName,
                Active = v.OwnerCustomer.IsActive,
                v.OwnerCustomer.HouseholdId,
                HouseholdActive = v.OwnerCustomer.Household != null && v.OwnerCustomer.Household.IsActive,
                Apartment = v.OwnerCustomer.Household != null ? v.OwnerCustomer.Household.ApartmentCode : null
            })
            .ToListAsync(cancellationToken);

        var vehicle = vehicles.FirstOrDefault();
        var vehicleIds = vehicles.Select(v => v.VehicleId).ToArray();

        var tickets = await ReadTicketsAsync(db, norm, vehicleIds, nowUtc, cancellationToken);

        if (vehicle is not null)
        {
            var validTickets = vehicle.Active
                ? tickets.Select(t => t.Ticket).ToList()
                : new List<PlateTicket>();
            return new PlateOwnership(norm, vehicle.VehicleId, vehicle.VehicleTypeId, vehicle.OwnerCustomerId, vehicle.Name,
                vehicle.Active, vehicle.HouseholdId, vehicle.HouseholdActive, vehicle.Apartment, validTickets);
        }

        var first = tickets.FirstOrDefault();
        if (first is null)
        {
            return PlateOwnership.None(norm);
        }

        return new PlateOwnership(norm, null, null, first.Ticket.CustomerId, first.Name, true,
            first.HouseholdId, first.HouseholdActive, first.Apartment,
            tickets.Select(t => t.Ticket).ToList());
    }

    private sealed record TicketRow(PlateTicket Ticket, string Name, int? HouseholdId, bool HouseholdActive, string? Apartment);

    private static async Task<List<TicketRow>> ReadTicketsAsync(SmartPsDbContext db, string norm, int[] vehicleIds,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var rows = await db.MonthlyTickets.FromSql($"""
                SELECT * FROM "MonthlyTickets"
                WHERE "Status" = 0 AND "StartDate" <= {nowUtc} AND {nowUtc} <= "EndDate"
                  AND ("VehicleId" = ANY({vehicleIds})
                       OR upper(regexp_replace("RegisteredLicensePlate", '[^a-zA-Z0-9]', '', 'g')) = {norm})
                """)
            .AsNoTracking()
            .Where(t => t.Customer!.IsActive)
            .OrderByDescending(t => t.EndDate).ThenByDescending(t => t.TicketId)
            .Select(t => new
            {
                t.TicketId,
                t.TicketCode,
                t.CustomerId,
                t.VehicleTypeId,
                t.StartDate,
                t.EndDate,
                Name = t.Customer!.FullName,
                t.Customer.HouseholdId,
                HouseholdActive = t.Customer.Household != null && t.Customer.Household.IsActive,
                Apartment = t.Customer.Household != null ? t.Customer.Household.ApartmentCode : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new TicketRow(
                new PlateTicket(r.TicketId, r.TicketCode, r.CustomerId, r.VehicleTypeId,
                    DateTime.SpecifyKind(r.StartDate, DateTimeKind.Utc), DateTime.SpecifyKind(r.EndDate, DateTimeKind.Utc)),
                r.Name, r.HouseholdId, r.HouseholdActive, r.Apartment))
            .ToList();
    }
}
