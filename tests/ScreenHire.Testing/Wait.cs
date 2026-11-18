namespace ScreenHire.Testing;

/// <summary>
/// How a test waits for something that is not on a page (a page waits with Playwright's <c>Expect</c>): until a condition
/// holds, polled against a deadline, or through a window in which something must not happen. A test never sleeps for a
/// fixed time and then looks: that is too short on a loaded runner and wasted on a quiet one
/// (docs/process/testing.md, "Flake policy"). <c>TestSuiteRulesTests</c> fails on a <c>Task.Delay</c> outside this
/// project, so every wait in a test says what it waits for.
/// </summary>
public static class Wait
{
    /// <summary>As long as the suite gives the worker to do something: the deadline of a wait that names no other.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly object Held = new();

    /// <summary>Polls <paramref name="probe"/> until it holds, failing with <paramref name="because"/> after the deadline.</summary>
    public static Task UntilAsync(Func<Task<bool>> probe, string because, CancellationToken cancellationToken, TimeSpan? deadline = null) =>
        UntilAsync(probe, () => because, cancellationToken, deadline);

    /// <summary>The same, with what it waited for worked out when it fails, so the message can say how far things got.</summary>
    public static async Task UntilAsync(Func<Task<bool>> probe, Func<string> because, CancellationToken cancellationToken, TimeSpan? deadline = null)
    {
        _ = await ForAsync(async _ => await probe() ? Held : null, because, cancellationToken, deadline);
    }

    /// <summary>
    /// Polls <paramref name="read"/> until it returns something, and returns that, failing with
    /// <paramref name="because"/> after the deadline: for a row, an email or a link a test goes on to use.
    /// </summary>
    public static Task<T> ForAsync<T>(Func<Task<T?>> read, string because, CancellationToken cancellationToken, TimeSpan? deadline = null)
        where T : class =>
        ForAsync(_ => read(), () => because, cancellationToken, deadline);

    /// <summary>
    /// The same, with the deadline's token passed to <paramref name="read"/>, so a request that hangs ends with the wait,
    /// and with what it waited for worked out when it fails.
    /// </summary>
    public static async Task<T> ForAsync<T>(Func<CancellationToken, Task<T?>> read, Func<string> because, CancellationToken cancellationToken, TimeSpan? deadline = null)
        where T : class
    {
        var limit = deadline ?? Deadline;
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expiry.CancelAfter(limit);
        try
        {
            while (true)
            {
                if (await read(expiry.Token) is { } found)
                {
                    return found;
                }

                await Task.Delay(PollInterval, expiry.Token);
            }
        }
        // Only the deadline reads as a timeout. A cancellation from anywhere else, such as an HttpClient's own timeout
        // inside the read, is thrown as it is: it did not wait this long.
        catch (OperationCanceledException) when (expiry.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Waited {limit.TotalSeconds:0} seconds for {because()}.");
        }
    }

    /// <summary>
    /// Lets <paramref name="window"/> pass so a test can then show that something did not happen in it, such as a second
    /// email inside a cooldown. There is nothing to poll for when the point is that nothing comes, so this is the one fixed
    /// wait a test may make, and <paramref name="because"/> names what must not happen. When the thing that would cause it
    /// can be waited for instead (a message the worker handles, a later event that must arrive), wait for that.
    /// </summary>
    public static Task ForNothingAsync(TimeSpan window, string because, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);
        return Task.Delay(window, cancellationToken);
    }
}
