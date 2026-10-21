using System.Diagnostics;

namespace BlazorTestPlatform.Drivers;

/// <summary>
/// Retry loop used by auto-waiting actions and assertions. The back-off (0, 20, 50, 100, 100, 250, 500 ms...)
/// keeps fast stories fast while tolerating slow async work.
/// </summary>
public static class Poll
{
    private static readonly int[] Intervals = [0, 20, 50, 100, 100, 250, 500];

    /// <summary>
    /// Calls <paramref name="probe"/> until it reports success or the timeout elapses.
    /// Returns the last probe result in both cases.
    /// </summary>
    public static async Task<(bool Success, T Value)> UntilAsync<T>(
        Func<CancellationToken, Task<(bool Success, T Value)>> probe,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var clock = Stopwatch.StartNew();
        var attempt = 0;
        (bool Success, T Value) last;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await probe(cancellationToken).ConfigureAwait(false);
            if (last.Success)
            {
                return last;
            }

            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return last;
            }

            var delay = TimeSpan.FromMilliseconds(Intervals[Math.Min(attempt, Intervals.Length - 1)]);
            attempt++;
            if (delay > remaining)
            {
                delay = remaining;
            }

            await Task.Delay(delay == TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
