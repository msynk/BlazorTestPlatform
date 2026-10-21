using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Performance;

namespace BlazorTestPlatform.Results;

/// <summary>Final outcome of a story run.</summary>
public enum StoryOutcome
{
    /// <summary>Passed on the first attempt.</summary>
    Passed,

    /// <summary>Failed every attempt.</summary>
    Failed,

    /// <summary>Failed, then passed on retry. Treated as passing but reported prominently.</summary>
    Flaky,

    /// <summary>Not run.</summary>
    Skipped,
}

/// <summary>Visual comparison status.</summary>
public enum VisualStatus
{
    /// <summary>Matches the baseline within tolerance.</summary>
    Matched,

    /// <summary>Differs from the baseline.</summary>
    Mismatched,

    /// <summary>Image dimensions differ from the baseline.</summary>
    SizeMismatch,

    /// <summary>No baseline existed and one was written.</summary>
    NewBaseline,

    /// <summary>No baseline existed and writing new baselines is disabled (CI).</summary>
    MissingBaseline,

    /// <summary>The baseline was overwritten (update mode).</summary>
    Updated,
}

/// <summary>Outcome of a visual snapshot comparison.</summary>
public sealed record VisualResult
{
    /// <summary>Snapshot name (relative baseline path).</summary>
    public required string SnapshotName { get; init; }

    /// <summary>Status.</summary>
    public VisualStatus Status { get; init; }

    /// <summary>Baseline image path.</summary>
    public string? BaselinePath { get; init; }

    /// <summary>Actual image path (written on mismatch).</summary>
    public string? ActualPath { get; init; }

    /// <summary>Diff image path (written on mismatch).</summary>
    public string? DiffPath { get; init; }

    /// <summary>Number of differing pixels.</summary>
    public int DiffPixels { get; init; }

    /// <summary>Ratio of differing pixels.</summary>
    public double DiffRatio { get; init; }

    /// <summary>Image width.</summary>
    public int Width { get; init; }

    /// <summary>Image height.</summary>
    public int Height { get; init; }

    /// <summary>Why the comparison may have failed for reasons other than the component (e.g. baselines from another browser build).</summary>
    public string? Note { get; init; }

    /// <summary>True for statuses that fail the build.</summary>
    public bool IsFailure => Status is VisualStatus.Mismatched or VisualStatus.SizeMismatch or VisualStatus.MissingBaseline;
}

/// <summary>A file produced while running a story.</summary>
/// <param name="Kind">Kind: <c>screenshot</c>, <c>trace</c>, <c>dom</c>, <c>log</c>, <c>baseline</c>, <c>actual</c>, <c>diff</c>.</param>
/// <param name="Path">Absolute path.</param>
/// <param name="Description">Optional description.</param>
public sealed record ArtifactReference(string Kind, string Path, string? Description = null);

/// <summary>A browser console message or page error.</summary>
public sealed record ConsoleEntry
{
    /// <summary>Level: <c>log</c>, <c>info</c>, <c>warning</c>, <c>error</c>, <c>pageerror</c>.</summary>
    public required string Level { get; init; }

    /// <summary>Message text.</summary>
    public required string Text { get; init; }

    /// <summary>Source location, if any.</summary>
    public string? Location { get; init; }

    /// <summary>Milliseconds since the story started.</summary>
    public double AtMilliseconds { get; init; }
}

/// <summary>A network request observed while running a story.</summary>
public sealed record NetworkEntry
{
    /// <summary>HTTP method.</summary>
    public required string Method { get; init; }

    /// <summary>URL.</summary>
    public required string Url { get; init; }

    /// <summary>Resource type (document, script, fetch, websocket...).</summary>
    public string? ResourceType { get; init; }

    /// <summary>Status code (0 when failed).</summary>
    public int Status { get; init; }

    /// <summary>Failure text.</summary>
    public string? Failure { get; init; }

    /// <summary>Milliseconds since the story started.</summary>
    public double StartMilliseconds { get; init; }

    /// <summary>Duration, when completed.</summary>
    public double? DurationMilliseconds { get; init; }
}

/// <summary>Everything known about one story run (one matrix cell). Serialized to JSON for reports and CLI tooling.</summary>
public sealed record StoryResult
{
    /// <summary>Unique key of the run (<c>story|driver|viewport|theme|mode</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Story id.</summary>
    public required string StoryId { get; init; }

    /// <summary>Story title.</summary>
    public required string Title { get; init; }

    /// <summary>Story name.</summary>
    public required string Name { get; init; }

    /// <summary>Component type name.</summary>
    public string? Component { get; init; }

    /// <summary>Driver (<c>bunit</c>, <c>chromium</c>...).</summary>
    public required string Driver { get; init; }

    /// <summary>Hosting model token.</summary>
    public string? HostingModel { get; init; }

    /// <summary>Viewport name.</summary>
    public string? Viewport { get; init; }

    /// <summary>Theme name.</summary>
    public string? Theme { get; init; }

    /// <summary>Outcome.</summary>
    public StoryOutcome Outcome { get; init; }

    /// <summary>Attempts made (retries + 1).</summary>
    public int Attempts { get; init; } = 1;

    /// <summary>Total duration in milliseconds.</summary>
    public double DurationMilliseconds { get; init; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Failure message (last failed attempt).</summary>
    public string? Error { get; init; }

    /// <summary>Failure exception type.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Failure stack trace.</summary>
    public string? StackTrace { get; init; }

    /// <summary>Errors of earlier, failed attempts (flaky diagnostics).</summary>
    public IReadOnlyList<string> AttemptErrors { get; init; } = [];

    /// <summary>Skip reason.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Execution timeline of the final attempt.</summary>
    public IReadOnlyList<StepRecord> Steps { get; init; } = [];

    /// <summary>Visual comparison.</summary>
    public VisualResult? Visual { get; init; }

    /// <summary>Accessibility scan.</summary>
    public AccessibilityReport? Accessibility { get; init; }

    /// <summary>Accessibility gate failures (rule ids).</summary>
    public IReadOnlyList<string> AccessibilityFailures { get; init; } = [];

    /// <summary>Measured render metrics.</summary>
    public RenderMetrics? Performance { get; init; }

    /// <summary>Comparison against the performance baseline.</summary>
    public PerformanceComparison? PerformanceComparison { get; init; }

    /// <summary>Exceeded performance budgets.</summary>
    public IReadOnlyList<BudgetViolation> BudgetViolations { get; init; } = [];

    /// <summary>Console messages and page errors.</summary>
    public IReadOnlyList<ConsoleEntry> Console { get; init; } = [];

    /// <summary>Network activity.</summary>
    public IReadOnlyList<NetworkEntry> Network { get; init; } = [];

    /// <summary>Requests answered by the story's mocked HTTP backend.</summary>
    public IReadOnlyList<HttpRequestRecord> HttpRequests { get; init; } = [];

    /// <summary>Files produced (screenshots, traces, DOM dumps...).</summary>
    public IReadOnlyList<ArtifactReference> Artifacts { get; init; } = [];

    /// <summary>Final markup (truncated) for diagnostics.</summary>
    public string? Markup { get; init; }

    /// <summary>True for passing outcomes (passed or flaky).</summary>
    public bool IsSuccess => Outcome is StoryOutcome.Passed or StoryOutcome.Flaky;

    /// <summary>Builds a run key.</summary>
    public static string CreateKey(string storyId, string driver, string? viewport = null, string? theme = null, string? hostingModel = null) =>
        string.Join('|', new[] { storyId, driver, viewport, theme, hostingModel }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>Thrown by <c>Verify</c> helpers when a story result is not successful.</summary>
public sealed class StoryFailedException : StoryAssertionException
{
    /// <summary>Creates the exception.</summary>
    public StoryFailedException(StoryResult result, Exception? innerException = null)
        : base(Describe(result ?? throw new ArgumentNullException(nameof(result))), innerException ?? new InvalidOperationException(result.Error))
    {
        Result = result;
    }

    /// <summary>The failed result.</summary>
    public StoryResult Result { get; }

    private static string Describe(StoryResult result)
    {
        var lines = new List<string> { $"Story '{result.StoryId}' failed ({result.Key}) after {result.Attempts} attempt(s):", result.Error ?? "(no message)" };
        foreach (var artifact in result.Artifacts)
        {
            lines.Add($"  {artifact.Kind}: {artifact.Path}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
