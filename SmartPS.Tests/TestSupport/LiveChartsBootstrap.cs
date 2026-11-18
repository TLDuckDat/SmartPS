using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// App.OnStartup cấu hình LiveCharts một lần; trong test không có bước đó, nên LiveCharts tự khởi tạo
/// lúc dùng lần đầu. Khi nhiều class test chạy song song, các khởi tạo đó đua nhau trên Dictionary dùng chung
/// và gây TypeInitializationException / InvalidOperationException ngẫu nhiên.
/// ModuleInitializer chạy một lần, đơn luồng, trước mọi test, nên loại bỏ race này.
/// </summary>
internal static class LiveChartsBootstrap
{
    [ModuleInitializer]
    internal static void Configure()
    {
        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers()
            .AddLightTheme());
    }
}
