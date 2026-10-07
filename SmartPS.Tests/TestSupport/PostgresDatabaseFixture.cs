using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Data;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Per test class PostgreSQL database (used as IClassFixture inside the "Postgres" collection).
/// <list type="bullet">
/// <item>Connection string from env <c>SMARTPS_TEST_CONNECTION</c>; default targets the dedicated test container on port 5434.</item>
/// <item>Refuses to run unless the configured database name contains "test".</item>
/// <item>Creates a fresh database <c>&lt;db&gt;_&lt;guid&gt;</c>, applies migrations and the seed via <see cref="DbInitializer"/>, drops it at the end.</item>
/// <item>If the server is unreachable, <see cref="IsAvailable"/> is false and each test skips with <see cref="SkipReason"/>.</item>
/// </list>
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SMARTPS_TEST_CONNECTION";
    public const string DefaultConnectionString = "Host=localhost;Port=5434;Database=smartps_test;Username=smartps;Password=smartps;";

    private readonly List<string> _createdDatabases = new();

    public PostgresDatabaseFixture()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        BaseConnectionString = string.IsNullOrWhiteSpace(configured) ? DefaultConnectionString : configured;

        var builder = new NpgsqlConnectionStringBuilder(BaseConnectionString);
        var baseName = builder.Database ?? string.Empty;
        if (!baseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run integration tests against database '{baseName}': the name must contain 'test'.");
        }

        DatabaseName = $"{baseName}_{Guid.NewGuid():N}"[..Math.Min(baseName.Length + 1 + 16, 63)].ToLowerInvariant();
        ConnectionString = WithDatabase(DatabaseName);
    }

    public string BaseConnectionString { get; }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = "PostgreSQL fixture not initialized.";

    public IDbContextFactory<SmartPsDbContext> Factory => new TestDbContextFactory(ConnectionString);

    public string WithDatabase(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = databaseName };
        return builder.ConnectionString;
    }

    private string MaintenanceConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(BaseConnectionString)
            {
                Database = "postgres",
                Timeout = 3,
                Pooling = false
            };
            return builder.ConnectionString;
        }
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var probe = new NpgsqlConnection(MaintenanceConnectionString);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await probe.OpenAsync(cts.Token);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"PostgreSQL test server unreachable ({Redact(BaseConnectionString)}): {ex.GetType().Name}: {ex.Message}. " +
                         $"Start the test container or set {EnvironmentVariable}.";
            return;
        }

        var seedPath = Path.Combine(AppContext.BaseDirectory, "seed_data.sql");
        if (!File.Exists(seedPath))
        {
            throw new FileNotFoundException("seed_data.sql was not copied to the test output directory.", seedPath);
        }

        await CreateDatabaseAsync(DatabaseName);

        await using (var db = CreateContext())
        {
            await DbInitializer.InitializeAsync(db);
        }

        IsAvailable = true;
        SkipReason = string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        foreach (var name in _createdDatabases.AsEnumerable().Reverse())
        {
            try
            {
                await using var conn = new NpgsqlConnection(MaintenanceConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PostgresDatabaseFixture] could not drop {name}: {ex.Message}");
            }
        }
    }

    /// <summary>Skips the calling test when the server is unreachable.</summary>
    public void RequireAvailable() => Assert.SkipUnless(IsAvailable, SkipReason);

    /// <summary>Creates an empty sibling database (dropped with the fixture) and returns its connection string.</summary>
    public async Task<string> CreateSiblingDatabaseAsync(string suffix)
    {
        var name = $"{DatabaseName}_{suffix}".ToLowerInvariant();
        if (name.Length > 63)
        {
            name = name[..63];
        }

        await CreateDatabaseAsync(name);
        return WithDatabase(name);
    }

    public SmartPsDbContext CreateContext() => CreateContext(ConnectionString);

    public static SmartPsDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SmartPsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new SmartPsDbContext(options);
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
        => await ScalarAsync<T>(ConnectionString, sql, parameters);

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull)
        {
            return default;
        }

        return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task CreateDatabaseAsync(string name)
    {
        await using var conn = new NpgsqlConnection(MaintenanceConnectionString);
        await conn.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn))
        {
            await create.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(name);
    }

    private static string Redact(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Password = "***" };
        return builder.ConnectionString;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<SmartPsDbContext>
    {
        private readonly string _connectionString;

        public TestDbContextFactory(string connectionString) => _connectionString = connectionString;

        public SmartPsDbContext CreateDbContext() => CreateContext(_connectionString);
    }
}
