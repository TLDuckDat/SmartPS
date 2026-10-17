using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Data;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Fix round 2 — FY1 (CH33): only real connectivity failures may switch the gate to offline mode.
/// Connection: SocketException, transient NpgsqlException (I/O, socket or timeout on the connection layer),
/// PostgresException with SqlState class 08 — also when wrapped (DbUpdateException / InvalidOperationException).
/// Not connection: constraint violations, serialization failures, deadlocks, trigger errors, audit-lock timeouts,
/// cancellations and plain exceptions, whatever their message says.
/// </summary>
public class DbConnectionHelperTests
{
    private static PostgresException Pg(string sqlState, string message = "server error")
        => new(message, "ERROR", "ERROR", sqlState);

    private static readonly IReadOnlyDictionary<string, Func<Exception>> Cases = new Dictionary<string, Func<Exception>>
    {
        // ---- connection failures (true)
        ["SocketException refused"] = () => new SocketException((int)SocketError.ConnectionRefused),
        ["NpgsqlException over SocketException"] = () => new NpgsqlException("Failed to connect to 127.0.0.1:1", new SocketException((int)SocketError.ConnectionRefused)),
        ["NpgsqlException over TimeoutException"] = () => new NpgsqlException("Exception while connecting", new TimeoutException("connect timed out")),
        ["NpgsqlException over IOException"] = () => new NpgsqlException("Exception while reading from stream", new IOException("connection reset")),
        ["PostgresException 08006"] = () => new PostgresException("connection failure", "FATAL", "FATAL", "08006"),
        ["PostgresException 08001"] = () => new PostgresException("unable to connect", "FATAL", "FATAL", "08001"),
        ["DbUpdateException over transient NpgsqlException"] = () => new DbUpdateException("save failed",
            new NpgsqlException("Exception while writing to stream", new IOException("broken pipe"))),
        ["InvalidOperationException over transient NpgsqlException"] = () => new InvalidOperationException("transient failure",
            new NpgsqlException("Failed to connect", new SocketException((int)SocketError.HostUnreachable))),

        // ---- not connection failures (false)
        ["PostgresException 23503 FK"] = () => Pg(PostgresErrorCodes.ForeignKeyViolation, "insert or update violates foreign key constraint"),
        ["PostgresException 23505 unique"] = () => Pg(PostgresErrorCodes.UniqueViolation, "duplicate key value"),
        ["PostgresException 40001 serialization"] = () => Pg(PostgresErrorCodes.SerializationFailure, "could not serialize access"),
        ["PostgresException 40P01 deadlock"] = () => Pg(PostgresErrorCodes.DeadlockDetected, "deadlock detected"),
        ["PostgresException P0001 trigger"] = () => Pg(PostgresErrorCodes.RaiseException, "AuditLogs is append-only: UPDATE is not allowed."),
        ["DbUpdateException over 23503"] = () => new DbUpdateException("save failed", Pg(PostgresErrorCodes.ForeignKeyViolation)),
        ["AuditLockTimeoutException"] = () => new AuditLockTimeoutException("Timed out waiting for the audit chain lock (timeout)."),
        ["AuditLockHeldException"] = () => new AuditLockHeldException("lock held in current flow"),
        ["Plain InvalidOperationException"] = () => new InvalidOperationException("Sequence contains no elements"),
        ["InvalidOperationException mentioning timeout"] = () => new InvalidOperationException("Operation timeout while waiting for the server host to connect"),
        ["OperationCanceledException"] = () => new OperationCanceledException(),
        ["Null"] = () => null!,
    };

    private static readonly (string Name, bool Expected)[] Rows =
    {
        ("SocketException refused", true),
        ("NpgsqlException over SocketException", true),
        ("NpgsqlException over TimeoutException", true),
        ("NpgsqlException over IOException", true),
        ("PostgresException 08006", true),
        ("PostgresException 08001", true),
        ("DbUpdateException over transient NpgsqlException", true),
        ("InvalidOperationException over transient NpgsqlException", true),
        ("PostgresException 23503 FK", false),
        ("PostgresException 23505 unique", false),
        ("PostgresException 40001 serialization", false),
        ("PostgresException 40P01 deadlock", false),
        ("PostgresException P0001 trigger", false),
        ("DbUpdateException over 23503", false),
        ("AuditLockTimeoutException", false),
        ("AuditLockHeldException", false),
        ("Plain InvalidOperationException", false),
        ("InvalidOperationException mentioning timeout", false),
        ("OperationCanceledException", false),
        ("Null", false),
    };

    public static TheoryData<string, bool> Table()
    {
        var data = new TheoryData<string, bool>();
        foreach (var (name, expected) in Rows)
        {
            data.Add(name, expected);
        }

        return data;
    }

    [Fact]
    public void Table_covers_every_case()
    {
        Assert.Equal(Cases.Keys.OrderBy(k => k, StringComparer.Ordinal), Rows.Select(r => r.Name).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void FY1_IsConnectionException_classifies_only_connectivity_failures(string name, bool expected)
    {
        var exception = Cases[name]();

        Assert.Equal(expected, DbConnectionHelper.IsConnectionException(exception));
    }
}
