using SmartPS.Models.Parking;
using SmartPS.Data;
using Microsoft.EntityFrameworkCore;

namespace SmartPS.Services.Customers;

public class VehiclesService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    public VehiclesService(IDbContextFactory<SmartPsDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<bool> CheckVehicleLimitAsync(int householdId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var household = await db.Households.FirstOrDefaultAsync(h => h.HouseholdId == householdId);
        if (household == null) return false;
        var settings = await db.ParkingSettings.FirstOrDefaultAsync();
        int max = household.MaxVehicles ?? settings?.DefaultMaxVehiclesPerHousehold ?? 2;
        int current = await db.Vehicles.CountAsync(v => v.OwnerCustomer!.HouseholdId == householdId && v.IsActive);
        return current < max;
    }
}
