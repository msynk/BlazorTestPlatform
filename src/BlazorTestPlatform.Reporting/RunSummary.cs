using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Aggregated counts of a run.</summary>
public sealed record RunSummary
{
    /// <summary>All results.</summary>
    public IReadOnlyList<StoryResult> Results { get; init; } = [];

    /// <summary>Total results.</summary>
    public int Total => Results.Count;

    /// <summary>Passed results.</summary>
    public int Passed => Results.Count(r => r.Outcome == StoryOutcome.Passed);

    /// <summary>Failed results.</summary>
    public int Failed => Results.Count(r => r.Outcome == StoryOutcome.Failed);

    /// <summary>Flaky results (passed after retry).</summary>
    public int Flaky => Results.Count(r => r.Outcome == StoryOutcome.Flaky);

    /// <summary>Skipped results.</summary>
    public int Skipped => Results.Count(r => r.Outcome == StoryOutcome.Skipped);

    /// <summary>Visual snapshots that changed or are missing.</summary>
    public int VisualChanges => Results.Count(r => r.Visual?.IsFailure == true);

    /// <summary>New visual baselines written in this run.</summary>
    public int NewBaselines => Results.Count(r => r.Visual?.Status is VisualStatus.NewBaseline);

    /// <summary>Results with accessibility gate failures.</summary>
    public int AccessibilityFailures => Results.Count(r => r.AccessibilityFailures.Count > 0);

    /// <summary>Results with performance regressions or budget violations.</summary>
    public int PerformanceRegressions => Results.Count(r => r.BudgetViolations.Count > 0 || r.PerformanceComparison?.HasFailures == true);

    /// <summary>Distinct stories.</summary>
    public int Stories => Results.Select(r => r.StoryId).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Distinct drivers/browsers.</summary>
    public IReadOnlyList<string> Drivers => Results.Select(r => r.Driver).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Distinct hosting models.</summary>
    public IReadOnlyList<string> HostingModels => Results.Select(r => r.HostingModel).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Sum of durations in milliseconds.</summary>
    public double TotalDurationMilliseconds => Results.Sum(r => r.DurationMilliseconds);

    /// <summary>True when nothing failed.</summary>
    public bool Success => Failed == 0;

    /// <summary>Creates a summary.</summary>
    public static RunSummary From(IEnumerable<StoryResult> results) =>
        new() { Results = results.OrderBy(r => r.Title, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray() };
}
