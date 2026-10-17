namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Waits for a condition that an async-void command handler (AsyncRelayCommand.Execute) will eventually make true.
/// The timeout only bounds a broken test; no assertion depends on elapsed time.
/// </summary>
public static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, string because, int timeoutMilliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(10);
        }
    }

    public static async Task<T> WithTimeout<T>(Task<T> task, string because, int timeoutMilliseconds = 10_000)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeoutMilliseconds));
        Assert.True(finished == task, $"Timed out waiting for {because}.");
        return await task;
    }

    public static async Task WithTimeout(Task task, string because, int timeoutMilliseconds = 10_000)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeoutMilliseconds));
        Assert.True(finished == task, $"Timed out waiting for {because}.");
        await task;
    }
}
