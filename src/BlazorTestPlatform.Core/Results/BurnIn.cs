namespace BlazorTestPlatform.Results;

/// <summary>Burn-in (<see cref="Configuration.BtpSettings.RepeatEach"/>): runs a story several times and fails it unless every run passes.</summary>
public static class BurnIn
{
    /// <summary>
    /// Runs <paramref name="run"/> up to <paramref name="runs"/> times and stops at the first run that does not pass
    /// cleanly. A failed or flaky (passed on a retry) run makes the result failed, saying which run broke; a skipped
    /// first run is returned as is.
    /// </summary>
    public static async Task<StoryResult> RunAsync(int runs, Func<int, Task<StoryResult>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (runs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), runs, "Must be at least 1.");
        }

        var result = await run(1).ConfigureAwait(false);
        if (runs == 1)
        {
            return result;
        }

        for (var current = 1; ; current++)
        {
            if (result.Outcome is StoryOutcome.Failed or StoryOutcome.Flaky)
            {
                var why = result.Outcome == StoryOutcome.Flaky
                    ? $"passed only after {result.Attempts} attempts ({string.Join(" | ", result.AttemptErrors)})"
                    : result.Error;
                return result with
                {
                    Outcome = StoryOutcome.Failed,
                    Error = $"Burn-in: run {current} of {runs} failed after {current - 1} passing run(s): {why}",
                    ErrorType = result.ErrorType ?? typeof(StoryAssertionException).FullName,
                };
            }

            if (result.Outcome == StoryOutcome.Skipped || current == runs)
            {
                return result;
            }

            result = await run(current + 1).ConfigureAwait(false);
        }
    }
}
