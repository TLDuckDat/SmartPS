namespace SmartPS.Tests.TestSupport;

/// <summary>
/// All integration test classes join this collection: they never run in parallel with each other
/// (the audit chain lock is global per database server connection pool and tests measure timings).
/// Each class still gets its own database through <see cref="PostgresDatabaseFixture"/> (IClassFixture).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection
{
    public const string Name = "Postgres";
}
