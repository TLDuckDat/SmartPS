using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using SmartPS.Data;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Fix round 2 — FY1 (CH33): only real connectivity failures may switch the gate to offline mode.
/// Connection: SocketException, SqlException whose errors are all connection errors (-2 timeout, 2/53 server not found,
/// 10054 connection reset, 4060 database unavailable...) — also when wrapped (DbUpdateException / InvalidOperationException).
/// Not connection: constraint violations, deadlocks, trigger errors, audit-lock timeouts,
/// cancellations and plain exceptions, whatever their message says.
/// </summary>
public class DbConnectionHelperTests
{
    private static SqlException Sql(int number, string message = "server error") => SqlExceptionFactory.Create(number, message);

    private static readonly IReadOnlyDictionary<string, Func<Exception>> Cases = new Dictionary<string, Func<Exception>>
    {
        // ---- connection failures (true)
        ["SocketException refused"] = () => new SocketException((int)SocketError.ConnectionRefused),
        ["SqlException -2 timeout"] = () => Sql(-2, "Execution Timeout Expired."),
        ["SqlException 53 server not found"] = () => Sql(53, "A network-related or instance-specific error occurred while establishing a connection to SQL Server."),
        ["SqlException 10054 connection reset"] = () => Sql(10054, "An existing connection was forcibly closed by the remote host."),
        ["SqlException 4060 database unavailable"] = () => Sql(4060, "Cannot open database requested by the login."),
        ["DbUpdateException over SqlException 10054"] = () => new DbUpdateException("save failed", Sql(10054, "connection reset")),
        ["InvalidOperationException over SqlException 53"] = () => new InvalidOperationException("transient failure", Sql(53, "server not found")),

        // ---- not connection failures (false)
        ["SqlException 547 FK"] = () => Sql(547, "The INSERT statement conflicted with the FOREIGN KEY constraint."),
        ["SqlException 2627 unique"] = () => Sql(2627, "Violation of UNIQUE KEY constraint."),
        ["SqlException 1205 deadlock"] = () => Sql(1205, "Transaction was deadlocked on lock resources with another process and has been chosen as the deadlock victim."),
        ["SqlException 50001 trigger"] = () => Sql(50001, "AuditLogs is append-only: UPDATE/DELETE is not allowed."),
        ["SqlException mixed timeout and FK"] = () => SqlExceptionFactory.Create(new[] { (-2, "timeout"), (547, "foreign key") }),
        ["DbUpdateException over 547"] = () => new DbUpdateException("save failed", Sql(547)),
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
        ("SqlException -2 timeout", true),
        ("SqlException 53 server not found", true),
        ("SqlException 10054 connection reset", true),
        ("SqlException 4060 database unavailable", true),
        ("DbUpdateException over SqlException 10054", true),
        ("InvalidOperationException over SqlException 53", true),
        ("SqlException 547 FK", false),
        ("SqlException 2627 unique", false),
        ("SqlException 1205 deadlock", false),
        ("SqlException 50001 trigger", false),
        ("SqlException mixed timeout and FK", false),
        ("DbUpdateException over 547", false),
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
