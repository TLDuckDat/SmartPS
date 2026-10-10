using Microsoft.EntityFrameworkCore;
using SmartPS.Models.Audit;
using SmartPS.Models.Auth;
using SmartPS.Models.Parking;
using SmartPS.Models.Payment;
using SmartPS.Models.Shifts;

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
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MonthlyTicket> MonthlyTickets => Set<MonthlyTicket>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ParkingSettings> ParkingSettings => Set<ParkingSettings>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<PaymentWebhook> PaymentWebhooks => Set<PaymentWebhook>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();

    // Audit Trail (append-only)
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartPsDbContext).Assembly);
        SmartPS.Data.Configurations.Payment.PaymentConfiguration.Configure(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditLogsAreAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditLogsAreAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureAuditLogsAreAppendOnly()
    {
        ChangeTracker.DetectChanges();
        var blocked = ChangeTracker.Entries<AuditLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (blocked)
        {
            throw new AuditLogImmutableException("AuditLogs is append-only: records cannot be modified or deleted.");
        }
    }
}
