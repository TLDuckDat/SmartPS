namespace SmartPS.Data;

/// <summary>
/// Lớp tiện ích phát hiện ngoại lệ mất kết nối hoặc không thể kết nối tới cơ sở dữ liệu PostgreSQL
/// Duyệt đệ quy toàn bộ chuỗi InnerException để phát hiện lỗi Socket, Timeout, Port 5432, Connection Refused
/// </summary>
public static class DbConnectionHelper
{
    public static bool IsConnectionException(Exception? ex)
    {
        while (ex != null)
        {
            if (ex is System.Net.Sockets.SocketException ||
                ex is TimeoutException)
            {
                return true;
            }

            var typeName = ex.GetType().FullName ?? string.Empty;
            if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Postgres", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Socket", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var msg = ex.Message;
            if (msg.Contains("5432", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                (msg.Contains("server", StringComparison.OrdinalIgnoreCase) && msg.Contains("connect", StringComparison.OrdinalIgnoreCase)) ||
                msg.Contains("kết nối", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("host", StringComparison.OrdinalIgnoreCase) && msg.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }
}

