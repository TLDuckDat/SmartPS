using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AC-R7 / spec R-B: nothing of the removed overlap with the household/vehicle module is left in the application:
/// no entity, table, service, permission, audit action or customer-type value of the old resident/visitor
/// customer management. History migrations of other modules (which legitimately mention old tables) are not scanned.
/// The household module's own files are compared against the base branch by the merge gate (manual check).
/// </summary>
public class NoOverlapSourceScanTests
{
    /// <summary>Application sources (.cs/.xaml/.sql) outside Migrations/, bin/ and obj/.</summary>
    private static IEnumerable<string> AppSources()
        => Directory.EnumerateFiles(RepoPaths.AppProject, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                        || p.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string MigrationsFolder => Path.Combine(RepoPaths.AppProject, "Migrations");

    [Theory]
    [InlineData("CustomerVehicle")]
    [InlineData("MonthlyTicketPurchase")]
    [InlineData("BlacklistEntr")]
    [InlineData("MonthlyFeeSplit")]
    [InlineData("MonthlyCoverage")]
    [InlineData("CoveragePeriod")]
    [InlineData("TicketDates")]
    [InlineData("TicketStatusEvaluator")]
    [InlineData("CustomerValidator")]
    [InlineData("AuditPii")]
    [InlineData("ICustomerService")]
    [InlineData("IMonthlyTicketService")]
    [InlineData("IBlacklistService")]
    [InlineData("IBlacklistReader")]
    [InlineData("NoBlacklistReader")]
    [InlineData("CustomerEditorViewModel")]
    [InlineData("TicketPurchaseKind")]
    [InlineData("CustomerType.Regular")]
    [InlineData("CustomerType.Loyal")]
    [InlineData("CustomerType.VIP")]
    [InlineData("\"Customer.View\"")]
    [InlineData("\"Customer.Manage\"")]
    [InlineData("\"Blacklist.Manage\"")]
    [InlineData("GATE_BLACKLIST_EXIT_WARNING")]
    [InlineData("TicketExpiredDuringStay")]
    [InlineData("TicketNoLongerValid")]
    public void Application_sources_do_not_mention_removed_overlap(string token)
    {
        var hits = AppSources().Where(p => File.ReadAllText(p).Contains(token, StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(RepoPaths.AppProject, p))
            .ToList();

        Assert.True(hits.Count == 0, $"'{token}' still appears in: {string.Join(", ", hits)}");
    }

    [Theory]
    [InlineData("CustomerVehicles")]
    [InlineData("MonthlyTicketPurchases")]
    [InlineData("BlacklistEntries")]
    [InlineData("IsResident")]
    [InlineData("\"Building\"")]
    public void Model_snapshot_has_no_overlap_table_or_column(string token)
    {
        var snapshot = File.ReadAllText(Path.Combine(MigrationsFolder, "SmartPsDbContextModelSnapshot.cs"));

        Assert.DoesNotContain(token, snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void No_resident_visitor_or_hardening_migration_files_remain()
    {
        var names = Directory.EnumerateFiles(MigrationsFolder, "*.cs").Select(Path.GetFileName).ToList();

        Assert.DoesNotContain(names, n => n!.Contains("ResidentVisitor", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n!.Contains("HardenAuditTriggers", StringComparison.Ordinal));
        Assert.Single(names, n => n!.EndsWith("_AddGateZoneAudience.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_migration_creates_no_table_and_touches_no_household_module_table()
    {
        var file = Directory.EnumerateFiles(MigrationsFolder, "*_AddGateZoneAudience.cs").Single();
        var text = File.ReadAllText(file);

        Assert.DoesNotContain("CreateTable", text, StringComparison.Ordinal);
        foreach (var table in new[] { "\"Customers\"", "\"Households\"", "\"Vehicles\"", "\"MonthlyTickets\"", "\"BlacklistedVehicles\"", "\"PricingRules\"", "\"Permissions\"" })
        {
            Assert.DoesNotContain(table, text, StringComparison.Ordinal);
        }

        Assert.Contains("Audience", text, StringComparison.Ordinal);
        Assert.Contains("IX_ParkingSessions_SlotId_Active", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IsResident")]
    [InlineData("ApartmentCode")]
    [InlineData("Building")]
    [InlineData("Vehicles")]
    [InlineData("Purchases")]
    public void Customer_entity_has_no_overlap_property(string property)
    {
        Assert.Null(typeof(Customer).GetProperty(property, BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void DbContext_exposes_no_overlap_DbSet_and_keeps_the_household_module_sets()
    {
        var sets = typeof(SmartPsDbContext).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("CustomerVehicles", sets);
        Assert.DoesNotContain("MonthlyTicketPurchases", sets);
        Assert.DoesNotContain("BlacklistEntries", sets);
        Assert.Superset(new HashSet<string>(StringComparer.Ordinal) { "Households", "Customers", "Vehicles", "MonthlyTickets", "BlacklistedVehicles", "PricingRules", "AuditLogs" }, sets);
    }

    [Fact]
    public void EF_model_has_no_overlap_entity_and_parking_zone_has_audience()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);
        var names = ctx.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("CustomerVehicle", names);
        Assert.DoesNotContain("MonthlyTicketPurchase", names);
        Assert.DoesNotContain("BlacklistEntry", names);
        Assert.DoesNotContain("CustomerTier", names);
        Assert.DoesNotContain("MonthlyTicketPlan", names);

        var zone = ctx.Model.FindEntityType(typeof(ParkingZone))!;
        var audience = zone.FindProperty(nameof(ParkingZone.Audience));
        Assert.NotNull(audience);
        Assert.Equal(0, Convert.ToInt32(audience!.GetDefaultValue(), System.Globalization.CultureInfo.InvariantCulture));

        var session = ctx.Model.FindEntityType(typeof(ParkingSession))!;
        var index = Assert.Single(session.GetIndexes(), i => i.GetDatabaseName() == "IX_ParkingSessions_SlotId_Active");
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { nameof(ParkingSession.SlotId) }, index.Properties.Select(p => p.Name).ToArray());
        Assert.Contains("\"Status\" = 0", index.GetFilter(), StringComparison.Ordinal);
        Assert.Contains("\"SlotId\" IS NOT NULL", index.GetFilter(), StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_code_reads_the_household_model_only_through_the_read_only_reader()
    {
        // The ownership reader must not write to the household module's tables.
        var path = Path.Combine(RepoPaths.Services, "GateControl", "PlateOwnershipReader.cs");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);

        foreach (var write in new[] { "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", ".Attach(", "EntityState.", "db.Add", "db.Update", "db.Remove" })
        {
            Assert.DoesNotContain(write, text, StringComparison.Ordinal);
        }

        Assert.Contains("AsNoTracking", text, StringComparison.Ordinal);
    }
}
