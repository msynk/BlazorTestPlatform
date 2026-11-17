using System.Collections.Concurrent;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Results;

/// <summary>Where a story runs: the driver (<c>bunit</c>, <c>chromium</c>...) and the render mode.</summary>
/// <param name="Driver">Driver name.</param>
/// <param name="HostingModel">Render mode.</param>
public sealed record StoryRunInfo(string Driver, HostingModel HostingModel);

/// <summary>
/// A known failure (Playwright's <c>test.fail()</c>, pytest's strict <c>xfail</c>, Swift Testing's <c>withKnownIssue</c>):
/// the story keeps running, a failure is reported as expected and passes, and a run that passes on its first attempt
/// fails with "expected to fail", so the annotation is removed once the bug is fixed. Declared with
/// <c>ExpectFailure(...)</c> on a story.
/// </summary>
/// <param name="Reason">Why the story fails (an issue link), shown in results and reports.</param>
/// <param name="When">Limits the expectation to some runs (a driver or render mode); null for every run.</param>
public sealed record ExpectedFailure(string Reason, Func<StoryRunInfo, bool>? When = null)
{
    /// <summary>True when the expectation covers a run.</summary>
    public bool AppliesTo(StoryRunInfo run) => When?.Invoke(run ?? throw new ArgumentNullException(nameof(run))) ?? true;
}

/// <summary>
/// The run-wide rules every runner applies to a finished result before it is written: expected failures, quarantine,
/// <see cref="BtpSettings.FailOnFlaky"/> and <see cref="BtpSettings.MaxFailures"/>. Custom runners call
/// <see cref="Apply"/> on their results and <see cref="StopReason"/> before starting a story.
/// </summary>
public static class RunPolicy
{
    private static int _blockingFailures;

    /// <summary>Blocking failures this process has seen (<see cref="BtpSettings.MaxFailures"/> counts them).</summary>
    public static int BlockingFailures => Volatile.Read(ref _blockingFailures);

    /// <summary>
    /// Why a story must not start: the process already saw <see cref="BtpSettings.MaxFailures"/> blocking failures. Null
    /// when it may run.
    /// </summary>
    public static string? StopReason(BtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var failures = BlockingFailures;
        return settings.MaxFailures > 0 && failures >= settings.MaxFailures
            ? $"Not run: the run stopped after {failures} failure(s) (BTP_MAX_FAILURES={settings.MaxFailures})."
            : null;
    }

    /// <summary>
    /// Applies the story's expected failure, the flaky policy and the quarantine list to <paramref name="result"/>, and
    /// counts it toward <see cref="BtpSettings.MaxFailures"/> when it still fails the build. Applying it twice changes
    /// nothing more.
    /// </summary>
    public static StoryResult Apply(Story? story, StoryResult result, BtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        var applied = ApplyExpectedFailure(story, result);
        if (settings.FailOnFlaky && applied.Outcome == StoryOutcome.Flaky && applied.ExpectedFailure is null)
        {
            applied = applied with
            {
                Outcome = StoryOutcome.Failed,
                Error = $"Flaky: passed only on attempt {applied.Attempts}, and BTP_FAIL_ON_FLAKY fails flaky runs.{Environment.NewLine}  Earlier attempts: {string.Join(" | ", applied.AttemptErrors)}",
                ErrorType = FlakyErrorType,
            };
        }

        applied = settings.Quarantine.Apply(applied);
        if (applied.IsBlockingFailure && Counted.TryAdd(applied.Key + "|" + applied.StartedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), 0))
        {
            Interlocked.Increment(ref _blockingFailures);
        }

        return applied;
    }

    /// <summary>The <see cref="StoryResult.ErrorType"/> of flaky runs failed by <see cref="BtpSettings.FailOnFlaky"/>.</summary>
    public const string FlakyErrorType = "BlazorTestPlatform.Flaky";

    // A burn-in rewrites the same result; each failed run counts once.
    private static readonly ConcurrentDictionary<string, byte> Counted = new(StringComparer.Ordinal);

    /// <summary>Forgets the failures counted so far (tests of the policy itself).</summary>
    internal static void Reset()
    {
        Interlocked.Exchange(ref _blockingFailures, 0);
        Counted.Clear();
    }

    private static StoryResult ApplyExpectedFailure(Story? story, StoryResult result)
    {
        // Already decided (burn-in and runners write a result more than once), or nothing expected.
        if (result.ExpectedFailure is not null || story?.ExpectedFailure is not { } expected || result.Outcome == StoryOutcome.Skipped)
        {
            return result;
        }

        var run = new StoryRunInfo(result.Driver, HostingModelExtensions.TryParse(result.HostingModel, out var model) ? model : HostingModel.InMemory);
        if (!expected.AppliesTo(run))
        {
            return result;
        }

        return result.Outcome switch
        {
            // A failure on any attempt is the known issue showing up (intermittent known issues pass on a retry).
            StoryOutcome.Failed or StoryOutcome.Flaky => result with
            {
                Outcome = StoryOutcome.Passed,
                ExpectedFailure = expected.Reason,
                Quarantine = null,
            },
            _ => result with
            {
                Outcome = StoryOutcome.Failed,
                ExpectedFailure = expected.Reason,
                Error = $"Story '{result.StoryId}' was expected to fail ({expected.Reason}) but passed. If the issue is fixed, remove ExpectFailure(...) from the story.",
                ErrorType = typeof(StoryAssertionException).FullName,
            },
        };
    }
}

/// <summary>
/// Selects the stories a run executes from <see cref="BtpSettings"/>: <see cref="BtpSettings.TagFilter"/>
/// (<c>BTP_TAGS</c>), <see cref="BtpSettings.Grep"/> and <see cref="BtpSettings.GrepInvert"/> (<c>BTP_GREP</c>,
/// <c>BTP_GREP_INVERT</c>) and <see cref="BtpSettings.LastFailed"/> (<c>BTP_LAST_FAILED</c>). The test framework packages
/// report stories it leaves out as skipped, with the reason; runners skip them too, so plain <c>MemberData</c> tests
/// honor the same switches.
/// </summary>
public static class StoryRunFilter
{
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlySet<string>>> FailedStories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Why the run leaves <paramref name="story"/> out, or null when it runs.</summary>
    public static string? ExcludeReason(Story story, BtpSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        settings ??= BtpSettings.Current;
        if (settings.TagFilter is { } tags && !tags.Matches(story))
        {
            return $"Not selected by the tag filter '{tags.Text}' (BTP_TAGS).";
        }

        if (settings.Grep is { } grep && !grep.IsMatch(story.Id) && !grep.IsMatch(story.Path))
        {
            return $"Not selected by BTP_GREP '{grep}'.";
        }

        if (settings.GrepInvert is { } invert && (invert.IsMatch(story.Id) || invert.IsMatch(story.Path)))
        {
            return $"Left out by BTP_GREP_INVERT '{invert}'.";
        }

        if (settings.LastFailed && FailedInPreviousRun(settings) is { Count: > 0 } failed && !failed.Contains(story.Id))
        {
            return "Passed in the previous run (BTP_LAST_FAILED reruns only the stories that failed).";
        }

        return null;
    }

    /// <summary>
    /// Ids of the stories with a failed result in the results directory, read once per process (before this run writes
    /// new results), so every test of the run sees the same selection. Test projects that share a results directory and
    /// run in parallel each read it when they start; give them their own directories for exact reruns.
    /// </summary>
    public static IReadOnlySet<string> FailedInPreviousRun(BtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return FailedStories.GetOrAdd(settings.ResultsDirectoryPath, path => new Lazy<IReadOnlySet<string>>(() =>
            new ResultsStore(path).ReadAll().Where(r => r.Outcome == StoryOutcome.Failed).Select(r => r.StoryId).ToHashSet(StringComparer.Ordinal))).Value;
    }
}
