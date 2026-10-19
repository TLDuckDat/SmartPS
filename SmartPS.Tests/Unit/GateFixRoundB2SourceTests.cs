using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Branch 2 fix round 1.
/// <list type="bullet">
/// <item>H2: the CustomerType fix-up after inserting a session is unconditional (no comparison with EF's read-back).</item>
/// <item>H3: the household module's ParkingSessionConfiguration.cs / ParkingZoneConfiguration.cs are byte-identical to
/// origin/pricing (content hash, line endings normalized), while our own configuration classes still put the unique
/// active-slot index and the Audience default into the model, and no new migration is needed.</item>
/// </list>
/// </summary>
public class GateFixRoundB2SourceTests
{
    // SHA-256 of the origin/pricing (7ba6da0) blobs with CRLF normalized to LF and without a UTF-8 BOM.
    private const string PricingParkingSessionConfigurationSha256 = "d36b960c67301e5e43e47ffda554b37dd450910689900cea111b503f7c686fbc";
    private const string PricingParkingZoneConfigurationSha256 = "3b1a4503b474ad8a4241f58bec5d57046136631e0fc8e390213dcfedd76d0cee";

    private const string GateMigrationId = "20261013091509_AddGateZoneAudience";

    private static string ConfigurationsFolder => Path.Combine(RepoPaths.AppProject, "Data", "Configurations");

    private static string NormalizedSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    // ---- H2 -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void H2_customer_type_fix_up_does_not_compare_with_the_value_read_back_by_EF()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Services, "GateControl", "GateControlService.cs"));

        Assert.DoesNotMatch(new Regex(@"CustomerType\s*!=\s*session\.CustomerType"), text);
        Assert.DoesNotMatch(new Regex(@"session\.CustomerType\s*!=\s*\w+\.CustomerType"), text);
        Assert.Matches(new Regex(@"Property\(\s*\w+\s*=>\s*\w+\.CustomerType\s*\)\.IsModified\s*=\s*true"), text);
    }

    // ---- H3 -------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("ParkingSessionConfiguration.cs", PricingParkingSessionConfigurationSha256)]
    [InlineData("ParkingZoneConfiguration.cs", PricingParkingZoneConfigurationSha256)]
    public void H3_household_module_configuration_files_are_identical_to_origin_pricing(string fileName, string expectedSha256)
    {
        var path = Path.Combine(ConfigurationsFolder, "Parking", fileName);
        Assert.True(File.Exists(path), path);

        Assert.Equal(expectedSha256, NormalizedSha256(path));
    }

    [Theory]
    [InlineData("ParkingSessionConfiguration.cs", "IX_ParkingSessions_SlotId_Active")]
    [InlineData("ParkingZoneConfiguration.cs", "Audience")]
    public void H3_our_additions_are_not_in_the_household_module_files(string fileName, string token)
    {
        var text = File.ReadAllText(Path.Combine(ConfigurationsFolder, "Parking", fileName));

        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(ParkingSession))]
    [InlineData(typeof(ParkingZone))]
    public void H3_our_own_entity_configuration_exists_next_to_the_household_modules(Type entity)
    {
        var configurationType = typeof(IEntityTypeConfiguration<>).MakeGenericType(entity);
        var implementations = typeof(SmartPsDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && configurationType.IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(implementations.Count >= 2, $"expected the household module's and our own configuration of {entity.Name}, found: {string.Join(", ", implementations)}");
    }

    [Fact]
    public void H3_model_still_has_the_unique_active_slot_index_and_the_audience_default()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);

        var session = ctx.Model.FindEntityType(typeof(ParkingSession))!;
        var index = Assert.Single(session.GetIndexes(), i => i.GetDatabaseName() == "IX_ParkingSessions_SlotId_Active");
        Assert.True(index.IsUnique);
        Assert.Equal("\"Status\" = 0 AND \"SlotId\" IS NOT NULL", index.GetFilter());
        Assert.Single(session.GetIndexes(), i => i.GetDatabaseName() == "IX_ParkingSessions_SlotId" && !i.IsUnique);

        var audience = ctx.Model.FindEntityType(typeof(ParkingZone))!.FindProperty(nameof(ParkingZone.Audience))!;
        var defaultValue = audience.GetDefaultValue();
        Assert.NotNull(defaultValue);
        Assert.Equal(0, Convert.ToInt32(defaultValue, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void H3_no_pending_model_changes_and_no_new_migration()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);

        Assert.False(ctx.Database.HasPendingModelChanges(), "moving the configuration must not change the model");
        // the reporting index migration follows the gate migration directly; migrations of other modules may come after it
        var migrations = ctx.Database.GetMigrations().ToList();
        var afterGate = migrations.SkipWhile(m => m != GateMigrationId).ToList();
        Assert.True(afterGate.Count >= 2, "the gate migration must be followed by the reporting index migration");
        Assert.Equal(GateMigrationId, afterGate[0]);
        Assert.EndsWith("_AddReportingIndexes", afterGate[1], StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(RepoPaths.AppProject, "Migrations"), "*_AddGateZoneAudience.cs"));
    }
}
