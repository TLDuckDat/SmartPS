using SmartPS.Services.Logging;

namespace SmartPS.Tests.Unit;

public class ErrorLogTests
{
    [Fact]
    public void Error_writes_source_message_and_exception_to_todays_log_file()
    {
        var marker = "ERRORLOG_TEST_" + Guid.NewGuid().ToString("N");

        ErrorLog.Error("UnitTest", new InvalidOperationException(marker));

        var file = Path.Combine(ErrorLog.LogDirectory, $"error-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(file));
        var text = File.ReadAllText(file);
        Assert.Contains("[UnitTest]", text);
        Assert.Contains(marker, text);
    }

    [Fact]
    public void Error_never_throws_even_without_exception()
    {
        var ex = Record.Exception(() => ErrorLog.Error("UnitTest", null, "message only"));
        Assert.Null(ex);
    }
}
