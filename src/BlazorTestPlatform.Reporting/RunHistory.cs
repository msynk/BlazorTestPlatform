using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Where a result key stands over the analysed window of runs.</summary>
public enum Stability
{
    /// <summary>Fewer than two runs: too early to tell.</summary>
    New,

    /// <summary>Passed cleanly in every run.</summary>
    Healthy,

    /// <summary>Failed recently after a clean history: a regression in progress, not (yet) a pattern.</summary>
    Failing,

    /// <summary>Alternates between passing and failing, or passes only on retry.</summary>
    Flaky,

    /// <summary>Fails consistently: three runs in a row or more, or every run.</summary>
    Broken,

    /// <summary>Failed in one stretch of runs and passes since, or was unstable but the last five runs passed cleanly.</summary>
    Fixed,
}

/// <summary>How flaky, in the bands Cypress Cloud uses (share of unstable runs).</summary>
public enum FlakeSeverity
{
    /// <summary>Not flaky.</summary>
    None,

    /// <summary>Up to 10% of runs.</summary>
    Low,

    /// <summary>Up to 50% of runs.</summary>
    Medium,

    /// <summary>More than half of the runs.</summary>
    High,
}

/// <summary>What the latest run says about a key compared with the runs before it.</summary>
public enum ResultTag
{
    /// <summary>First run of this key.</summary>
    NewTest,

    /// <summary>Failed now, passed in the previous run.</summary>
    NewFailure,

    /// <summary>Failed now and in the previous run.</summary>
    StillFailing,

    /// <summary>Failed in every analysed run.</summary>
    AlwaysFailing,

    /// <summary>Passed now, failed in the previous run.</summary>
    Fixed,

    /// <summary>Passed only on retry now, or unstable over the window.</summary>
    Flaky,

    /// <summary>Took more than 1.5× its median and 100 ms longer.</summary>
    Slower,

    /// <summary>Failed, but the quarantine file covers it.</summary>
    Quarantined,
}

/// <summary>One run of one key.</summary>
/// <param name="RunId">Run.</param>
/// <param name="At">When the run finished.</param>
/// <param name="Outcome">Outcome, or null when the key did not run.</param>
/// <param name="DurationMilliseconds">Duration, when it ran.</param>
/// <param name="Signature">Failure signature, when it failed.</param>
public sealed record HistoryPoint(string RunId, DateTimeOffset At, StoryOutcome? Outcome, double? DurationMilliseconds, string? Signature);

/// <summary>Stability of one result key over a window of runs.</summary>
public sealed record KeyStability
{
    /// <summary>The latest archived result of the key.</summary>
    public required ArchivedResult Last { get; init; }

    /// <summary>Result key.</summary>
    public string Key => Last.Key;

    /// <summary>One point per run in the window, oldest first.</summary>
    public IReadOnlyList<HistoryPoint> Points { get; init; } = [];

    /// <summary>Runs in which the key ran (not skipped).</summary>
    public int Runs { get; init; }

    /// <summary>Runs that failed.</summary>
    public int Failures { get; init; }

    /// <summary>Runs that passed only on retry.</summary>
    public int FlakyRuns { get; init; }

    /// <summary>Changes between failed and passing from one run to the next.</summary>
    public int Flips { get; init; }

    /// <summary>Share of unstable runs; 0 for broken keys (broken is not flaky).</summary>
    public double FlakeRate { get; init; }

    /// <summary>95% Wilson interval of the flake rate: how much the rate can be trusted with this many runs.</summary>
    public double FlakeRateLow { get; init; }

    /// <summary>Upper bound of the 95% interval.</summary>
    public double FlakeRateHigh { get; init; }

    /// <summary>Status.</summary>
    public Stability Status { get; init; }

    /// <summary>Severity band.</summary>
    public FlakeSeverity Severity { get; init; }

    /// <summary>Tags for the latest run.</summary>
    public IReadOnlyList<ResultTag> Tags { get; init; } = [];

    /// <summary>Median duration over the window.</summary>
    public double? MedianDurationMilliseconds { get; init; }

    /// <summary>Estimated CI time lost to unstable runs (duration of failed or retried runs).</summary>
    public double WastedMilliseconds { get; init; }
}

/// <summary>Failures that share a signature: one problem, however many stories and cells it breaks.</summary>
public sealed record ErrorCluster
{
    /// <summary>Signature.</summary>
    public required string Signature { get; init; }

    /// <summary>Category.</summary>
    public FailureCategory Category { get; init; }

    /// <summary>An example message (the latest).</summary>
    public string? Message { get; init; }

    /// <summary>Exception type.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Failed results in the window.</summary>
    public int Occurrences { get; init; }

    /// <summary>Result keys affected.</summary>
    public IReadOnlyList<string> Keys { get; init; } = [];

    /// <summary>Stories affected.</summary>
    public IReadOnlyList<string> Stories { get; init; } = [];

    /// <summary>Drivers affected.</summary>
    public IReadOnlyList<string> Drivers { get; init; } = [];

    /// <summary>Render modes affected.</summary>
    public IReadOnlyList<string> HostingModels { get; init; } = [];

    /// <summary>First run with this failure.</summary>
    public required string FirstSeenRunId { get; init; }

    /// <summary>When first seen.</summary>
    public DateTimeOffset FirstSeen { get; init; }

    /// <summary>Latest run with this failure.</summary>
    public required string LastSeenRunId { get; init; }

    /// <summary>When last seen.</summary>
    public DateTimeOffset LastSeen { get; init; }

    /// <summary>Whether the latest run has it.</summary>
    public bool Active { get; init; }

    /// <summary>Runs in the window that had it.</summary>
    public int Runs { get; init; }
}

/// <summary>Totals of one run, for trend charts.</summary>
public sealed record RunTrendPoint(
    string RunId,
    DateTimeOffset At,
    string Trigger,
    int Total,
    int Passed,
    int Failed,
    int Flaky,
    int Skipped,
    int Quarantined,
    double PassRate,
    double DurationMilliseconds,
    int AccessibilityViolations,
    int VisualChanges,
    int BudgetViolations);

/// <summary>A key in a comparison: its result in each run.</summary>
public sealed record ComparedResult(string Key, ArchivedResult? Base, ArchivedResult? Head)
{
    /// <summary>The newest result available.</summary>
    public ArchivedResult Latest => Head ?? Base!;
}

/// <summary>Base run versus head run, Cypress branch-review style.</summary>
public sealed record RunComparison
{
    /// <summary>Base run.</summary>
    public required RunRecord Base { get; init; }

    /// <summary>Head run.</summary>
    public required RunRecord Head { get; init; }

    /// <summary>Fail in head, did not fail in base.</summary>
    public IReadOnlyList<ComparedResult> NewFailures { get; init; } = [];

    /// <summary>Fail in both.</summary>
    public IReadOnlyList<ComparedResult> StillFailing { get; init; } = [];

    /// <summary>Failed in base, pass in head.</summary>
    public IReadOnlyList<ComparedResult> Fixed { get; init; } = [];

    /// <summary>Flaky in head, not in base.</summary>
    public IReadOnlyList<ComparedResult> NewFlaky { get; init; } = [];

    /// <summary>Only in head.</summary>
    public IReadOnlyList<ComparedResult> Added { get; init; } = [];

    /// <summary>Only in base.</summary>
    public IReadOnlyList<ComparedResult> Removed { get; init; } = [];

    /// <summary>Took more than 1.5× as long and 100 ms longer.</summary>
    public IReadOnlyList<ComparedResult> Slower { get; init; } = [];

    /// <summary>Took less than 2/3 as long and 100 ms less.</summary>
    public IReadOnlyList<ComparedResult> Faster { get; init; } = [];
}

/// <summary>New, existing or fixed accessibility violation between two runs.</summary>
public enum ViolationChange
{
    /// <summary>Only in the head run.</summary>
    New,

    /// <summary>In both runs: known debt.</summary>
    Existing,

    /// <summary>Only in the base run.</summary>
    Fixed,
}

/// <summary>One violation's change between runs.</summary>
/// <param name="Key">Result key.</param>
/// <param name="StoryId">Story.</param>
/// <param name="Title">Story title.</param>
/// <param name="Name">Story name.</param>
/// <param name="Violation">The violation.</param>
/// <param name="Change">New, existing or fixed.</param>
public sealed record ViolationDelta(string Key, string StoryId, string Title, string Name, ArchivedViolation Violation, ViolationChange Change);

/// <summary>
/// Analysis over archived runs: stability per key (healthy, failing, flaky, broken, fixed) with smart tags, error
/// clusters, trends, run comparisons and accessibility baselines. Everything is deterministic and computed from the
/// archive alone.
/// </summary>
public sealed class RunHistory
{
    private const int FixedAfterCleanRuns = 5;
    private const int BrokenAfterConsecutiveFailures = 3;

    /// <summary>Creates an analysis over runs (any order).</summary>
    public RunHistory(IEnumerable<RunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        Runs = runs.OrderBy(r => r.Run.FinishedAt).ToArray();
    }

    /// <summary>Runs, oldest first.</summary>
    public IReadOnlyList<RunRecord> Runs { get; }

    /// <summary>The newest run, if any.</summary>
    public RunRecord? Latest => Runs.Count == 0 ? null : Runs[^1];

    /// <summary>The run before the newest, if any.</summary>
    public RunRecord? Previous => Runs.Count < 2 ? null : Runs[^2];

    /// <summary>A run by id.</summary>
    public RunRecord? Find(string runId) => Runs.FirstOrDefault(r => string.Equals(r.Run.RunId, runId, StringComparison.Ordinal));

    /// <summary>Stability of every key that ran in the window, least stable first.</summary>
    public IReadOnlyList<KeyStability> Stability(int window = 30)
    {
        var runs = Window(window);
        return runs
            .SelectMany(r => r.Results)
            .Select(r => r.Key)
            .Distinct(StringComparer.Ordinal)
            .Select(key => Analyze(key, runs))
            .OfType<KeyStability>()
            .OrderBy(s => Rank(s.Status))
            .ThenByDescending(s => s.FlakeRate)
            .ThenByDescending(s => s.Flips)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Stability of one key, or null when it never ran in the window.</summary>
    public KeyStability? StabilityOf(string key, int window = 30) => Analyze(key, Window(window));

    /// <summary>Failures grouped by signature, active and most frequent first.</summary>
    public IReadOnlyList<ErrorCluster> Clusters(int window = 30)
    {
        var runs = Window(window);
        var latest = runs.Count == 0 ? null : runs[^1].Run.RunId;
        return runs
            .SelectMany(run => run.Results.Where(r => r.Outcome == StoryOutcome.Failed && r.Signature is not null).Select(r => (Run: run, Result: r)))
            .GroupBy(x => x.Result.Signature!, StringComparer.Ordinal)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Run.Run.FinishedAt).ToArray();
                var last = ordered[^1];
                return new ErrorCluster
                {
                    Signature = g.Key,
                    Category = last.Result.Category,
                    Message = last.Result.Error,
                    ErrorType = last.Result.ErrorType,
                    Occurrences = ordered.Length,
                    Keys = ordered.Select(x => x.Result.Key).Distinct(StringComparer.Ordinal).ToArray(),
                    Stories = ordered.Select(x => x.Result.StoryId).Distinct(StringComparer.Ordinal).ToArray(),
                    Drivers = ordered.Select(x => x.Result.Driver).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    HostingModels = ordered.Select(x => x.Result.HostingModel ?? "in-memory").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    FirstSeenRunId = ordered[0].Run.Run.RunId,
                    FirstSeen = ordered[0].Run.Run.FinishedAt,
                    LastSeenRunId = last.Run.Run.RunId,
                    LastSeen = last.Run.Run.FinishedAt,
                    Active = last.Run.Run.RunId == latest,
                    Runs = ordered.Select(x => x.Run.Run.RunId).Distinct(StringComparer.Ordinal).Count(),
                };
            })
            .OrderByDescending(c => c.Active)
            .ThenByDescending(c => c.Keys.Count)
            .ThenByDescending(c => c.Occurrences)
            .ThenBy(c => c.Signature, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Totals per run, oldest first.</summary>
    public IReadOnlyList<RunTrendPoint> Trend() => Runs
        .Select(r => new RunTrendPoint(
            r.Run.RunId,
            r.Run.FinishedAt,
            r.Run.Trigger,
            r.Results.Count,
            r.Count(StoryOutcome.Passed),
            r.Failed,
            r.Count(StoryOutcome.Flaky),
            r.Count(StoryOutcome.Skipped),
            r.Quarantined,
            r.PassRate,
            r.DurationMilliseconds,
            r.Results.Sum(x => x.Violations.Count),
            r.Results.Count(x => x.Visual is VisualStatus.Mismatched or VisualStatus.SizeMismatch or VisualStatus.MissingBaseline),
            r.Results.Sum(x => x.BudgetViolations)))
        .ToArray();

    /// <summary>Compares two runs.</summary>
    public static RunComparison Compare(RunRecord @base, RunRecord head)
    {
        ArgumentNullException.ThrowIfNull(@base);
        ArgumentNullException.ThrowIfNull(head);
        var before = @base.Results.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var after = head.Results.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var pairs = before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(k => new ComparedResult(k, before.GetValueOrDefault(k), after.GetValueOrDefault(k)))
            .ToArray();

        static bool Failed(ArchivedResult? r) => r is { Outcome: StoryOutcome.Failed };
        static bool Ran(ArchivedResult? r) => r is not null && r.Outcome != StoryOutcome.Skipped;
        return new RunComparison
        {
            Base = @base,
            Head = head,
            NewFailures = pairs.Where(p => Failed(p.Head) && p.Base is not null && !Failed(p.Base)).ToArray(),
            StillFailing = pairs.Where(p => Failed(p.Head) && Failed(p.Base)).ToArray(),
            Fixed = pairs.Where(p => Failed(p.Base) && Ran(p.Head) && !Failed(p.Head)).ToArray(),
            NewFlaky = pairs.Where(p => p.Head is { Outcome: StoryOutcome.Flaky } && p.Base is not { Outcome: StoryOutcome.Flaky }).ToArray(),
            Added = pairs.Where(p => p.Base is null).ToArray(),
            Removed = pairs.Where(p => p.Head is null).ToArray(),
            Slower = pairs.Where(p => Ran(p.Base) && Ran(p.Head) && IsSlower(p.Head!.DurationMilliseconds, p.Base!.DurationMilliseconds)).ToArray(),
            Faster = pairs.Where(p => Ran(p.Base) && Ran(p.Head) && IsSlower(p.Base!.DurationMilliseconds, p.Head!.DurationMilliseconds)).ToArray(),
        };
    }

    /// <summary>
    /// Accessibility violations of <paramref name="head"/> compared with <paramref name="base"/> (null: every violation
    /// is new). A violation is identified by result key, rule and first element, so moving elements around does not
    /// make known debt look new.
    /// </summary>
    public static IReadOnlyList<ViolationDelta> AccessibilityDelta(RunRecord? @base, RunRecord head)
    {
        ArgumentNullException.ThrowIfNull(head);
        static string Id(ArchivedResult r, ArchivedViolation v) => $"{r.Key}|{v.RuleId}|{v.Targets.FirstOrDefault()}";
        var before = (@base?.Results ?? []).SelectMany(r => r.Violations.Select(v => (Result: r, Violation: v))).ToDictionary(x => Id(x.Result, x.Violation), StringComparer.Ordinal);
        var after = head.Results.SelectMany(r => r.Violations.Select(v => (Result: r, Violation: v))).ToDictionary(x => Id(x.Result, x.Violation), StringComparer.Ordinal);
        var ranInHead = head.Results.Where(r => r.Outcome != StoryOutcome.Skipped).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        var deltas = new List<ViolationDelta>();
        foreach (var (id, x) in after)
        {
            deltas.Add(new ViolationDelta(x.Result.Key, x.Result.StoryId, x.Result.Title, x.Result.Name, x.Violation, before.ContainsKey(id) ? ViolationChange.Existing : ViolationChange.New));
        }

        foreach (var (id, x) in before.Where(b => !after.ContainsKey(b.Key) && ranInHead.Contains(b.Value.Result.Key)))
        {
            deltas.Add(new ViolationDelta(x.Result.Key, x.Result.StoryId, x.Result.Title, x.Result.Name, x.Violation, ViolationChange.Fixed));
        }

        return deltas.OrderBy(d => d.Change).ThenByDescending(d => d.Violation.Impact).ThenBy(d => d.Key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Median duration per key over the window, for duration-balanced sharding.</summary>
    public IReadOnlyDictionary<string, double> MedianDurations(int window = 30) => Window(window)
        .SelectMany(r => r.Results.Where(x => x.Outcome != StoryOutcome.Skipped))
        .GroupBy(r => r.Key, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => Median(g.Select(r => r.DurationMilliseconds)), StringComparer.Ordinal);

    /// <summary>The 95% Wilson score interval of <paramref name="successes"/> out of <paramref name="trials"/>.</summary>
    public static (double Low, double High) WilsonInterval(int successes, int trials)
    {
        if (trials <= 0)
        {
            return (0, 1);
        }

        const double Z = 1.96;
        var p = (double)successes / trials;
        var denominator = 1 + (Z * Z / trials);
        var center = (p + (Z * Z / (2 * trials))) / denominator;
        var half = Z * Math.Sqrt((p * (1 - p) / trials) + (Z * Z / (4.0 * trials * trials))) / denominator;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    private IReadOnlyList<RunRecord> Window(int window) => Runs.Count <= window ? Runs : Runs.Skip(Runs.Count - window).ToArray();

    private static KeyStability? Analyze(string key, IReadOnlyList<RunRecord> runs)
    {
        var points = new List<HistoryPoint>(runs.Count);
        ArchivedResult? last = null;
        foreach (var run in runs)
        {
            var result = run.Results.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
            points.Add(new HistoryPoint(run.Run.RunId, run.Run.FinishedAt, result?.Outcome, result?.DurationMilliseconds, result?.Signature));
            last = result ?? last;
        }

        if (last is null)
        {
            return null;
        }

        var ran = points.Where(p => p.Outcome is { } o && o != StoryOutcome.Skipped).ToArray();
        var failures = ran.Count(p => p.Outcome == StoryOutcome.Failed);
        var flakyRuns = ran.Count(p => p.Outcome == StoryOutcome.Flaky);
        var flips = ran.Zip(ran.Skip(1), (a, b) => (a.Outcome == StoryOutcome.Failed) != (b.Outcome == StoryOutcome.Failed)).Count(x => x);
        var trailingFailures = Enumerable.Reverse(ran).TakeWhile(p => p.Outcome == StoryOutcome.Failed).Count();
        var unstableBeforeTrailing = ran.Take(ran.Length - trailingFailures).Count(p => p.Outcome is StoryOutcome.Failed or StoryOutcome.Flaky);
        var cleanTail = Enumerable.Reverse(ran).TakeWhile(p => p.Outcome == StoryOutcome.Passed).Count();

        // A single run of failures followed by clean passes is a regression that was fixed, not flakiness: flakiness is
        // failing again after passing (several episodes) or passing only on retry.
        var episodes = ran.Select((p, i) => p.Outcome == StoryOutcome.Failed && (i == 0 || ran[i - 1].Outcome != StoryOutcome.Failed)).Count(start => start);

        var status = ran.Length switch
        {
            0 => Reporting.Stability.New,
            _ when failures == ran.Length && ran.Length >= 2 => Reporting.Stability.Broken,
            _ when trailingFailures >= BrokenAfterConsecutiveFailures => Reporting.Stability.Broken,
            _ when trailingFailures > 0 && unstableBeforeTrailing == 0 => ran.Length < 2 ? Reporting.Stability.New : Reporting.Stability.Failing,
            _ when failures + flakyRuns == 0 => ran.Length < 2 ? Reporting.Stability.New : Reporting.Stability.Healthy,
            _ when trailingFailures == 0 && flakyRuns == 0 && episodes == 1 => Reporting.Stability.Fixed,
            _ when cleanTail >= FixedAfterCleanRuns => Reporting.Stability.Fixed,
            1 => Reporting.Stability.New,
            _ => Reporting.Stability.Flaky,
        };

        var unstable = failures + flakyRuns;
        var rate = status is Reporting.Stability.Broken or Reporting.Stability.Failing or Reporting.Stability.Fixed || ran.Length == 0 ? 0 : (double)unstable / ran.Length;
        var (low, high) = WilsonInterval(unstable, ran.Length);
        var median = ran.Length == 0 ? (double?)null : Median(ran.Select(p => p.DurationMilliseconds ?? 0));

        var tags = new List<ResultTag>();
        var latestPoint = points[^1];
        var previous = points.Take(points.Count - 1).LastOrDefault(p => p.Outcome is { } o && o != StoryOutcome.Skipped);
        if (points.Take(points.Count - 1).All(p => p.Outcome is null))
        {
            tags.Add(ResultTag.NewTest);
        }

        if (latestPoint.Outcome == StoryOutcome.Failed)
        {
            if (failures == ran.Length && ran.Length >= 2)
            {
                tags.Add(ResultTag.AlwaysFailing);
            }
            else if (previous?.Outcome == StoryOutcome.Failed)
            {
                tags.Add(ResultTag.StillFailing);
            }
            else if (previous is not null)
            {
                tags.Add(ResultTag.NewFailure);
            }

            if (last.Quarantine is not null)
            {
                tags.Add(ResultTag.Quarantined);
            }
        }
        else if (latestPoint.Outcome is StoryOutcome.Passed or StoryOutcome.Flaky && previous?.Outcome == StoryOutcome.Failed)
        {
            tags.Add(ResultTag.Fixed);
        }

        if (latestPoint.Outcome == StoryOutcome.Flaky || status == Reporting.Stability.Flaky)
        {
            tags.Add(ResultTag.Flaky);
        }

        if (latestPoint.DurationMilliseconds is { } now && median is { } m && ran.Length >= 3 && IsSlower(now, m))
        {
            tags.Add(ResultTag.Slower);
        }

        return new KeyStability
        {
            Last = last,
            Points = points,
            Runs = ran.Length,
            Failures = failures,
            FlakyRuns = flakyRuns,
            Flips = flips,
            FlakeRate = rate,
            FlakeRateLow = rate == 0 ? 0 : low,
            FlakeRateHigh = rate == 0 ? 0 : high,
            Status = status,
            Severity = status != Reporting.Stability.Flaky ? FlakeSeverity.None : rate <= 0.1 ? FlakeSeverity.Low : rate <= 0.5 ? FlakeSeverity.Medium : FlakeSeverity.High,
            Tags = tags,
            MedianDurationMilliseconds = median,
            WastedMilliseconds = ran.Where(p => p.Outcome is StoryOutcome.Failed or StoryOutcome.Flaky).Sum(p => p.DurationMilliseconds ?? 0),
        };
    }

    private static bool IsSlower(double now, double before) => now > before * 1.5 && now - before > 100;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static int Rank(Stability status) => status switch
    {
        Reporting.Stability.Broken => 0,
        Reporting.Stability.Failing => 1,
        Reporting.Stability.Flaky => 2,
        Reporting.Stability.New => 3,
        Reporting.Stability.Fixed => 4,
        _ => 5,
    };
}
