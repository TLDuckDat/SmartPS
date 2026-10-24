using Microsoft.Extensions.DependencyInjection;
using SmartPS.Services.Common;
using SmartPS.Services.Settings;

namespace SmartPS.Tests.Integration;

/// <summary>Operational settings are Admin-only (Settings.Manage), range-checked, and the database status never exposes the password.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class SystemSettingsServiceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public SystemSettingsServiceTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    [Fact]
    public async Task Admin_saves_settings_and_reads_them_back()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var settings = sp.GetRequiredService<ISystemSettingsService>();

        var result = await settings.UpdateOperationalSettingsAsync(new OperationalSettings(72, 3));

        Assert.True(result.Success, result.Message);
        Assert.Equal(new OperationalSettings(72, 3), await settings.GetOperationalSettingsAsync());
    }

    [Fact]
    public async Task Manager_without_Settings_Manage_is_denied()
    {
        _db.RequireAvailable();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);

        var result = await sp.GetRequiredService<ISystemSettingsService>().UpdateOperationalSettingsAsync(new OperationalSettings(12, 2));

        Assert.True(result.IsPermissionDenied);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(721, 2)]
    [InlineData(48, 0)]
    [InlineData(48, 21)]
    public async Task Out_of_range_values_are_rejected(int overstayHours, int maxVehicles)
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();

        var result = await sp.GetRequiredService<ISystemSettingsService>().UpdateOperationalSettingsAsync(new OperationalSettings(overstayHours, maxVehicles));

        Assert.False(result.Success);
        Assert.Equal(OperationError.Validation, result.Error);
    }

    [Fact]
    public async Task Database_status_is_online_with_no_pending_migrations()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);

        var status = await sp.GetRequiredService<ISystemSettingsService>().GetDatabaseStatusAsync();

        Assert.True(status.IsOnline, status.Error);
        Assert.Equal(0, status.PendingMigrations);
        Assert.True(status.AppliedMigrations > 0);
        Assert.EndsWith("_InitialSqlServer", status.LastMigration);
        Assert.False(string.IsNullOrEmpty(status.ServerVersion));
    }
}
