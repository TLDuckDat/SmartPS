using System.Diagnostics;
using System.IO;
using System.Text;

namespace SmartPS.Services.Payment;

public static class PaymentLog
{
    private static readonly object Sync = new();

    public static void Event(string eventName, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Payment:{eventName}] {message}";
        Debug.WriteLine(line);

        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var logFile = Path.Combine(logDirectory, "payment.log");

            lock (Sync)
            {
                File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Không để lỗi ghi log làm hỏng nghiệp vụ thanh toán.
        }
    }
}