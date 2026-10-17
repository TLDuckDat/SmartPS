using System.IO;
using System.Net.Sockets;
using Npgsql;
using SmartPS.Services.Audit;

namespace SmartPS.Data;

/// <summary>
/// Classifies only real connectivity failures (socket, timeout of the connection layer, PostgreSQL connection
/// exceptions of class 08). Constraint, serialization, deadlock and trigger errors are not connection loss.
/// The inner-exception chain is inspected.
/// </summary>
public static class DbConnectionHelper
{
    public static bool IsConnectionException(Exception? ex)
    {
        while (ex != null)
        {
            switch (ex)
            {
                case AuditLockTimeoutException:
                case AuditLockHeldException:
                case OperationCanceledException:
                    return false;
                case SocketException:
                case TimeoutException:
                    return true;
                case PostgresException pg:
                    // IsTransient is also true for 40001/40P01, so use the SQLSTATE class instead
                    if (pg.SqlState is { Length: >= 2 } state && state.StartsWith("08", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    return false;
                case NpgsqlException { IsTransient: true }:
                    return true;
                case IOException when ex.InnerException is SocketException:
                    return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }
}
