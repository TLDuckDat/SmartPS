using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Per test class SQL Server database (used as IClassFixture inside the "Database" collection).
/// <list type="bullet">
/// <item>Connection string from env <c>SMARTPS_TEST_CONNECTION</c>; default targets SQL Server LocalDB (MSSQLLocalDB).</item>
/// <item>Refuses to run unless the configured database name contains "test".</item>
/// <item>Creates a fresh database <c>&lt;db&gt;_&lt;guid&gt;</c>, applies migrations and the seed via <see cref="DbInitializer"/>, drops it at the end.</item>
/// <item>If the server is unreachable, <see cref="IsAvailable"/> is false and each test skips with <see cref="SkipReason"/>.</item>
/// </list>
/// </summary>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SMARTPS_TEST_CONNECTION";
    public const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=smartps_test;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly List<string> _createdDatabases = new();

    public SqlServerDatabaseFixture()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        BaseConnectionString = string.IsNullOrWhiteSpace(configured) ? DefaultConnectionString : configured;

        var builder = new SqlConnectionStringBuilder(BaseConnectionString);
        var baseName = builder.InitialCatalog ?? string.Empty;
        if (!baseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run integration tests against database '{baseName}': the name must contain 'test'.");
        }

        DatabaseName = $"{baseName}_{Guid.NewGuid():N}"[..Math.Min(baseName.Length + 1 + 16, 128)].ToLowerInvariant();
        ConnectionString = WithDatabase(DatabaseName);
    }

    public string BaseConnectionString { get; }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = "SQL Server fixture not initialized.";

    public IDbContextFactory<SmartPsDbContext> Factory => new TestDbContextFactory(ConnectionString);

    public string WithDatabase(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(BaseConnectionString) { InitialCatalog = databaseName };
        return builder.ConnectionString;
    }

    private string MasterConnectionString
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(BaseConnectionString)
            {
                InitialCatalog = "master",
                // LocalDB khởi động instance ở lần kết nối đầu tiên
                ConnectTimeout = 30,
                Pooling = false
            };
            return builder.ConnectionString;
        }
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var probe = new SqlConnection(MasterConnectionString);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await probe.OpenAsync(cts.Token);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"SQL Server test server unreachable ({BaseConnectionString}): {ex.GetType().Name}: {ex.Message}. " +
                         $"Install SQL Server LocalDB or set {EnvironmentVariable}.";
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
        SqlConnection.ClearAllPools();
        foreach (var name in _createdDatabases.AsEnumerable().Reverse())
        {
            try
            {
                await DropDatabaseAsync(name);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SqlServerDatabaseFixture] could not drop {name}: {ex.Message}");
            }
        }
    }

    /// <summary>Skips the calling test when the server is unreachable.</summary>
    public void RequireAvailable() => Assert.SkipUnless(IsAvailable, SkipReason);

    /// <summary>Creates an empty sibling database (dropped with the fixture) and returns its connection string.</summary>
    public async Task<string> CreateSiblingDatabaseAsync(string suffix)
    {
        var name = $"{DatabaseName}_{suffix}".ToLowerInvariant();
        if (name.Length > 128)
        {
            name = name[..128];
        }

        await CreateDatabaseAsync(name);
        return WithDatabase(name);
    }

    public SmartPsDbContext CreateContext() => CreateContext(ConnectionString);

    public static SmartPsDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SmartPsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new SmartPsDbContext(options);
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            AddParameter(cmd, name, value);
        }

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
        => await ScalarAsync<T>(ConnectionString, sql, parameters);

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            AddParameter(cmd, name, value);
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
        await DropDatabaseAsync(name);

        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE {Quote(name)} COLLATE {SmartPsDbContext.DatabaseCollation}", conn))
        {
            create.CommandTimeout = 120;
            await create.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(name);
    }

    private async Task DropDatabaseAsync(string name)
    {
        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        // Ngắt mọi kết nối còn mở (tương đương DROP DATABASE ... WITH (FORCE) của PostgreSQL)
        await using var drop = new SqlCommand(
            $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(name)}; END",
            conn);
        drop.Parameters.AddWithValue("name", name);
        drop.CommandTimeout = 120;
        await drop.ExecuteNonQueryAsync();
    }

    // AddWithValue gửi DateTime dưới kiểu datetime cũ (làm tròn 3,33 ms): dùng datetime2 như cột của model
    private static void AddParameter(SqlCommand cmd, string name, object? value)
    {
        if (value is DateTime dateTime)
        {
            cmd.Parameters.Add(new SqlParameter(name, System.Data.SqlDbType.DateTime2) { Value = dateTime });
            return;
        }

        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private sealed class TestDbContextFactory : IDbContextFactory<SmartPsDbContext>
    {
        private readonly string _connectionString;

        public TestDbContextFactory(string connectionString) => _connectionString = connectionString;

        public SmartPsDbContext CreateDbContext() => CreateContext(_connectionString);
    }
}
