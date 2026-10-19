using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-R10 for the reporting branch (plan §3.5 step 4): the regenerated <c>&lt;ts&gt;_AddReportingIndexes</c> migration exists
/// once, sorts after the gate migration (and so after the household, blacklist and audit migrations), is the head of the
/// chain, creates only <c>IX_ParkingSessions_CheckOutTime</c>, and Down/Up are valid. The model has no pending changes.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ReportingIndexMigrationTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string BlacklistMigration = "20261010112452_AddBlacklistTable";
    private const string IndexName = "IX_ParkingSessions_CheckOutTime";

    private readonly PostgresDatabaseFixture _db;

    public ReportingIndexMigrationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static List<string> Migrations()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);
        return ctx.Database.GetMigrations().ToList();
    }

    private static async Task MigrateToAsync(string connectionString, string? target)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<long> IndexCountAsync(string cs)
        => PostgresDatabaseFixture.ScalarAsync<long>(cs, $"SELECT count(*) FROM pg_indexes WHERE tablename = 'ParkingSessions' AND indexname = '{IndexName}'");

    [Fact]
    public void Reporting_migration_exists_once_and_is_the_head_after_the_gate_migration()
    {
        var migrations = Migrations();
        var reporting = Assert.Single(migrations, m => m.EndsWith("_AddReportingIndexes", StringComparison.Ordinal));
        var gate = Assert.Single(migrations, m => m.EndsWith("_AddGateZoneAudience", StringComparison.Ordinal));

        // the reporting migration follows the gate migration directly; migrations of other modules may come after it
        Assert.Equal(migrations.IndexOf(gate) + 1, migrations.IndexOf(reporting));
        Assert.True(string.CompareOrdinal(reporting, gate) > 0);
        Assert.True(string.CompareOrdinal(reporting, BlacklistMigration) > 0);
        Assert.Equal(migrations.OrderBy(m => m, StringComparer.Ordinal), migrations);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(RepoPaths.AppProject, "Migrations"), "*_AddReportingIndexes.cs"));
    }

    [Fact]
    public void Model_has_the_check_out_index_and_no_pending_changes()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);
        var session = ctx.Model.FindEntityType(typeof(ParkingSession))!;

        Assert.Single(session.GetIndexes(), i => i.GetDatabaseName() == IndexName
                                                 && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ParkingSession.CheckOutTime) })
                                                 && !i.IsUnique);
        Assert.False(ctx.Database.HasPendingModelChanges(), "dotnet ef migrations has-pending-model-changes must report none");
    }

    [Fact]
    public async Task Fresh_database_has_the_index_and_down_then_up_drops_and_recreates_it()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;
        var migrations = Migrations();
        var gate = Assert.Single(migrations, m => m.EndsWith("_AddGateZoneAudience", StringComparison.Ordinal));

        Assert.Equal(1, await IndexCountAsync(cs));

        await MigrateToAsync(cs, gate);
        Assert.Equal(0, await IndexCountAsync(cs));
        Assert.Equal(1, await PostgresDatabaseFixture.ScalarAsync<long>(cs, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'ParkingZones' AND column_name = 'Audience'"));

        await MigrateToAsync(cs, null);
        Assert.Equal(1, await IndexCountAsync(cs));
        Assert.Equal(migrations[^1], await PostgresDatabaseFixture.ScalarAsync<string>(cs,
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1"));
    }
}
