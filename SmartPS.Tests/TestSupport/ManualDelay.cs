namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Replacement for the view model's injectable <c>DelayAsync</c> (plan m7): every call creates a pending task that
/// completes only when the test calls <see cref="Release"/>, or completes as cancelled when its token is cancelled.
/// No wall-clock time is involved.
/// </summary>
public sealed class ManualDelay
{
    private readonly object _gate = new();
    private readonly List<Pending> _calls = new();

    public sealed record Pending(TimeSpan Requested, CancellationToken Token, TaskCompletionSource Completion)
    {
        public bool IsCancelled => Completion.Task.IsCanceled;

        public bool IsCompleted => Completion.Task.IsCompleted;
    }

    public IReadOnlyList<Pending> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _calls.Add(new Pending(delay, cancellationToken, tcs));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetCanceled(cancellationToken);
        }
        else
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    /// <summary>Completes the delay created by the <paramref name="index"/>-th call (0-based).</summary>
    public void Release(int index) => Calls[index].Completion.TrySetResult();

    /// <summary>Completes every delay that is still pending.</summary>
    public void ReleaseAll()
    {
        foreach (var call in Calls)
        {
            call.Completion.TrySetResult();
        }
    }
}
