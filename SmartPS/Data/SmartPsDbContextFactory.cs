using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmartPS.Data;

/// <summary>
/// Cung cấp DbContext lúc Design-time cho SQL Server để dotnet-ef migrations hoạt động chính xác
/// </summary>
public class SmartPsDbContextFactory : IDesignTimeDbContextFactory<SmartPsDbContext>
{
    public SmartPsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables("SMARTPS_")
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? DbConnectionHelper.DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<SmartPsDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new SmartPsDbContext(optionsBuilder.Options);
    }
}
