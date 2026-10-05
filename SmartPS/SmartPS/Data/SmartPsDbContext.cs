using Microsoft.EntityFrameworkCore;
using SmartPS.Models.Auth;
using SmartPS.Models.Parking;

namespace SmartPS.Data;

public class SmartPsDbContext : DbContext
{
    public SmartPsDbContext(DbContextOptions<SmartPsDbContext> options) : base(options) { }

    // Auth Tables
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Parking & Billing Tables
    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();
    public DbSet<ParkingZone> ParkingZones => Set<ParkingZone>();
    public DbSet<ParkingSlot> ParkingSlots => Set<ParkingSlot>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<CustomerTier> CustomerTiers => Set<CustomerTier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MonthlyTicketPlan> MonthlyTicketPlans => Set<MonthlyTicketPlan>();
    public DbSet<MonthlyTicket> MonthlyTickets => Set<MonthlyTicket>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartPsDbContext).Assembly);
    }
}
