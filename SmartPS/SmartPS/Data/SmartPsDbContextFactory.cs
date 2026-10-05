using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmartPS.Data;

/// <summary>
/// Cung cấp DbContext lúc Design-time cho Npgsql PostgreSQL để dotnet-ef migrations hoạt động chính xác
/// </summary>
public class SmartPsDbContextFactory : IDesignTimeDbContextFactory<SmartPsDbContext>
{
    public SmartPsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=SmartPS;Username=smartps;Password=smartps;";

        var optionsBuilder = new DbContextOptionsBuilder<SmartPsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SmartPsDbContext(optionsBuilder.Options);
    }
}
