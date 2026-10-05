using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartPS.Services.Payment.Webhook;

public class PaymentWebhookServer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _prefix;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerLoopTask;

    public bool IsRunning => _listener?.IsListening ?? false;
    public string ListeningUrl => _prefix;

    public PaymentWebhookServer(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        var configuredPrefix = configuration["PaymentWebhook:Prefix"]
            ?? configuration["PayOS:WebhookPrefix"]
            ?? "http://localhost:5005/";

        if (!configuredPrefix.EndsWith('/'))
        {
            configuredPrefix += "/";
        }

        _prefix = configuredPrefix;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(_prefix);
            _listener.Start();

            _cts = new CancellationTokenSource();
            _listenerLoopTask = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);

            PaymentLog.Event("WebhookServerStarted", $"Webhook server đang lắng nghe tại {_prefix}api/payment/webhook");
        }
        catch (Exception ex)
        {
            PaymentLog.Event("WebhookServerStartError", $"Không thể khởi động Webhook server tại {_prefix}: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_listener == null)
        {
            return;
        }

        try
        {
            _cts?.Cancel();

            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
            _listener = null;

            if (_listenerLoopTask != null)
            {
                await Task.WhenAny(_listenerLoopTask, Task.Delay(2000, cancellationToken));
            }

            PaymentLog.Event("WebhookServerStopped", "Webhook server đã dừng an toàn.");
        }
        catch (Exception ex)
        {
            PaymentLog.Event("WebhookServerStopError", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || _listener == null || !_listener.IsListening)
            {
                // Bình thường khi dừng server
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                PaymentLog.Event("WebhookServerLoopError", ex.Message);
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;

            // Health check endpoint
            if (string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(path, "/api/payment/health", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteJsonResponseAsync(response, 200, new
                {
                    status = "healthy",
                    service = "SmartPS Payment Webhook Server",
                    timestamp = DateTime.UtcNow
                });
                return;
            }

            // Webhook endpoint
            if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(path, "/api/payment/webhook", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(path, "/payos/webhook", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(path, "/webhook", StringComparison.OrdinalIgnoreCase)))
            {
                string rawPayload;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                {
                    rawPayload = await reader.ReadToEndAsync(cancellationToken);
                }

                var signature = request.Headers["x-signature"]
                    ?? request.Headers["signature"]
                    ?? request.QueryString["signature"];

                var provider = request.Headers["x-provider"]
                    ?? request.QueryString["provider"]
                    ?? "PayOS";

                using var scope = _serviceProvider.CreateScope();
                var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

                var result = await paymentService.ProcessWebhookAsync(rawPayload, signature, provider, cancellationToken);

                await WriteJsonResponseAsync(response, result.HttpStatusCode, new
                {
                    success = result.Accepted,
                    accepted = result.Accepted,
                    isDuplicate = result.IsDuplicate,
                    paymentPaid = result.PaymentPaid,
                    checkoutCompleted = result.CheckoutCompleted,
                    message = result.Message
                });
                return;
            }

            // 404 for other paths
            await WriteJsonResponseAsync(response, 404, new { error = "Not Found", path });
        }
        catch (Exception ex)
        {
            PaymentLog.Event("WebhookHandlingError", ex.Message);
            try
            {
                await WriteJsonResponseAsync(response, 500, new
                {
                    success = false,
                    error = "Internal Server Error"
                });
            }
            catch
            {
                // Bỏ qua lỗi gửi phản hồi thứ cấp
            }
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch
            {
                // Bỏ qua lỗi đóng kết nối
            }
        }
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }
}
