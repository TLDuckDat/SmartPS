using System.IO;
using System.Text;

namespace SmartPS.Services.Logging;

/// <summary>
/// Ghi lỗi hệ thống (ngoại lệ không được xử lý, lỗi tác vụ nền) ra tệp theo ngày: logs/error-yyyyMMdd.log
/// nằm cạnh file chạy. Tự cài đặt, không phụ thuộc thư viện ngoài. Không bao giờ ném ngoại lệ ra ngoài
/// để việc ghi log không làm hỏng luồng nghiệp vụ.
/// </summary>
public static class ErrorLog
{
    private static readonly object Sync = new();

    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    public static void Error(string source, Exception? exception, string? message = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var file = Path.Combine(LogDirectory, $"error-{DateTime.Now:yyyyMMdd}.log");
            var detail = exception?.ToString() ?? string.Empty;
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message ?? exception?.Message ?? "Unknown error"}"
                       + Environment.NewLine + detail + Environment.NewLine + new string('-', 80) + Environment.NewLine;

            lock (Sync)
            {
                File.AppendAllText(file, text, Encoding.UTF8);
            }
        }
        catch
        {
            // Ghi log thất bại (ví dụ đĩa đầy) không được phép làm ứng dụng văng thêm lần nữa.
        }
    }
}
