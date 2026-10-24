using System.IO;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using SmartPS.Services.Audit;

namespace SmartPS.Data;

/// <summary>
/// Classifies only real connectivity failures (socket, timeout of the connection layer, SQL Server errors that mean
/// the server or database cannot be reached). Constraint, deadlock and trigger errors are not connection loss.
/// The inner-exception chain is inspected.
/// </summary>
public static class DbConnectionHelper
{
    /// <summary>SQL Server LocalDB đi kèm Visual Studio / SQL Server Express; ghi đè trong appsettings.local.json nếu dùng máy chủ khác.</summary>
    public const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=SmartPS;Trusted_Connection=True;TrustServerCertificate=True";

    // -2: hết thời gian chờ; 2, 26, 40, 53: không tìm thấy / không mở được máy chủ; 64, 121, 233, 1231, 10053, 10054, 10060,
    // 10061, 11001: đứt, bị từ chối hoặc không mở được kết nối mạng; 4060: không mở được database; 40613: database tạm thời không sẵn sàng
    private static readonly HashSet<int> ConnectionErrorNumbers = new()
    {
        -2, 2, 26, 40, 53, 64, 121, 233, 1231, 4060, 10053, 10054, 10060, 10061, 11001, 40613
    };

    // Mức nghiêm trọng từ 20 trở lên: lỗi làm đứt kết nối (lỗi tầng truyền tải, máy chủ dừng...)
    private const byte FatalErrorClass = 20;

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
                case SqlException sql:
                    // Mọi lỗi trong lô phải là lỗi kết nối (lỗi 1205 deadlock hay 2627 trùng khoá thì không phải)
                    return sql.Errors.Count > 0
                           && sql.Errors.Cast<SqlError>().All(e => ConnectionErrorNumbers.Contains(e.Number) || e.Class >= FatalErrorClass);
                case IOException when ex.InnerException is SocketException:
                    return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }
}
