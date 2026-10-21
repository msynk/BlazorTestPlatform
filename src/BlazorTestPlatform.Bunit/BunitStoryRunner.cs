using System.Diagnostics;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit;

/// <summary>
/// Runs stories in-memory: render, play, static accessibility gate, render-performance budgets and baselines.
/// Produces a <see cref="StoryResult"/> (written to the results directory for reports) and can throw on failure.
/// </summary>
/// <example>
/// <code>
/// public static TheoryData&lt;string&gt; Stories => new(StoryCatalog.FromAssemblyOf&lt;ButtonStories&gt;().Ids);
///
/// [Theory, MemberData(nameof(Stories))]
/// public Task Story_passes(string id) => BunitStoryRunner.VerifyAsync(Catalog.Get(id));
/// </code>
/// </example>
public static class BunitStoryRunner
{
    /// <summary>Runs the story and returns its result without throwing on failure.</summary>
    public static async Task<StoryResult> RunAsync(Story story, BunitStoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(story);
        options ??= new BunitStoryOptions();
        var settings = options.Settings ?? BtpSettings.Current;
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var key = StoryResult.CreateKey(story.Id, "bunit");

        if (story.SkipReason is { } skip)
        {
            return Write(options, settings, Base(story, key, started) with { Outcome = StoryOutcome.Skipped, SkipReason = skip });
        }

        var recorder = new StepRecorder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout ?? story.Timeout ?? TimeSpan.FromSeconds(30));

        string? markup = null;
        IReadOnlyList<HttpRequestRecord> httpRequests = [];
        AccessibilityReport? a11y = null;
        IReadOnlyList<string> a11yFailures = [];
        RenderMetrics? metrics = null;
        PerformanceComparison? comparison = null;
        IReadOnlyList<BudgetViolation> budgetViolations = [];

        try
        {
            await using (var session = await BunitStorySession.StartAsync(story, options, recorder, timeout.Token).ConfigureAwait(false))
            {
                try
                {
                    if (options.RunPlay)
                    {
                        await session.PlayAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    markup = Truncate(session.Markup, 20_000);
                    httpRequests = await session.Driver.GetHttpRequestsAsync(timeout.Token).ConfigureAwait(false);
                }

                if (options.CheckAccessibility && !story.Accessibility.Skip)
                {
                    var policy = options.AccessibilityPolicy ?? settings.Accessibility;
                    a11y = await recorder.RunAsync("accessibility (markup rules)", StepKind.Check, () => Task.FromResult(session.AnalyzeAccessibility(policy))).ConfigureAwait(false);
                    a11y = a11y with { Waivers = policy.WaiversFor(story.Id, story.Accessibility) };
                    var evaluation = policy.Evaluate(a11y, story.Id, story.Accessibility);
                    a11yFailures = evaluation.Failures.Select(f => f.RuleId).ToArray();
                    if (!evaluation.Passed)
                    {
                        throw new AccessibilityViolationException(evaluation, story.Id);
                    }
                }
            }

            if (options.MeasurePerformance && story.Performance is { } budget)
            {
                metrics = await recorder.RunAsync("measure render performance", StepKind.Check, () => MeasureAsync(story, budget, options, timeout.Token)).ConfigureAwait(false);
                budgetViolations = PerformanceBudgetEvaluator.Evaluate(metrics, budget);
                if (budget.TrackBaseline)
                {
                    var store = new PerformanceBaselineStore(settings.PerformanceBaselineFilePath);
                    var baseline = store.Get(story.Id);
                    comparison = PerformanceComparer.Compare(metrics, baseline, settings.Performance);
                    if (settings.UpdatePerformanceBaselines || (baseline is null && !settings.IsContinuousIntegration))
                    {
                        store.Set(story.Id, PerformanceBaselineEntry.From(metrics));
                    }
                }

                var problems = budgetViolations.Select(v => v.ToString())
                    .Concat(settings.UpdatePerformanceBaselines ? [] : comparison?.Failures.Select(f => f.ToString()) ?? [])
                    .ToArray();
                if (problems.Length > 0)
                {
                    throw new StoryAssertionException($"Render performance regression in '{story.Id}' ({metrics}):{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", problems)
                        + $"{Environment.NewLine}  Accept intentional changes with BTP_UPDATE_PERF=1 or 'btp perf approve'.");
                }
            }

            return Write(options, settings, Base(story, key, started) with
            {
                Outcome = StoryOutcome.Passed,
                DurationMilliseconds = clock.Elapsed.TotalMilliseconds,
                Steps = recorder.Steps,
                Accessibility = a11y,
                Performance = metrics,
                PerformanceComparison = comparison,
                BudgetViolations = budgetViolations,
                Markup = markup,
                HttpRequests = httpRequests,
            });
        }
        catch (StorySkippedException ex)
        {
            return Write(options, settings, Base(story, key, started) with
            {
                Outcome = StoryOutcome.Skipped,
                SkipReason = ex.Message,
                Steps = recorder.Steps,
                DurationMilliseconds = clock.Elapsed.TotalMilliseconds,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || timeout.IsCancellationRequested)
        {
            var message = ex is OperationCanceledException ? $"Story timed out after {(options.Timeout ?? story.Timeout ?? TimeSpan.FromSeconds(30)).TotalSeconds:0.#} s." : ex.Message;
            return Write(options, settings, Base(story, key, started) with
            {
                Outcome = StoryOutcome.Failed,
                Error = message,
                ErrorType = ex.GetType().FullName,
                StackTrace = ex.StackTrace,
                DurationMilliseconds = clock.Elapsed.TotalMilliseconds,
                Steps = recorder.Steps,
                Accessibility = a11y,
                AccessibilityFailures = a11yFailures,
                Performance = metrics,
                PerformanceComparison = comparison,
                BudgetViolations = budgetViolations,
                Markup = markup,
                HttpRequests = httpRequests,
            });
        }
    }

    /// <summary>Runs the story and throws <see cref="StoryFailedException"/> when it fails (skips pass silently).</summary>
    public static async Task<StoryResult> VerifyAsync(Story story, BunitStoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(story, options, cancellationToken).ConfigureAwait(false);
        return result.Outcome == StoryOutcome.Failed ? throw new StoryFailedException(result) : result;
    }

    /// <summary>Renders the story for custom assertions. Dispose the session when done.</summary>
    public static Task<BunitStorySession> RenderAsync(Story story, BunitStoryOptions? options = null, CancellationToken cancellationToken = default) =>
        BunitStorySession.StartAsync(story, options, cancellationToken: cancellationToken);

    /// <summary>Measures render + play over warm-up and measured iterations (median reported).</summary>
    public static async Task<RenderMetrics> MeasureAsync(Story story, PerformanceBudget budget, BunitStoryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(budget);
        var measureOptions = new BunitStoryOptions
        {
            ConfigureContext = options?.ConfigureContext,
            JSInteropMode = options?.JSInteropMode ?? global::Bunit.JSRuntimeMode.Loose,
            Settings = options?.Settings,
            WriteResults = false,
        };
        foreach (var (name, value) in options?.Args ?? new Dictionary<string, string?>())
        {
            measureOptions.Args[name] = value;
        }

        var durations = new List<double>();
        var allocations = new List<long>();
        RenderMetrics? counts = null;

        for (var i = 0; i < budget.WarmupIterations + Math.Max(1, budget.Iterations); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var measurement = new RenderMeasurement();
            await using var session = await BunitStorySession.StartAsync(story, measureOptions, recorder: null, measurement, cancellationToken).ConfigureAwait(false);
            if (story.Play is { } play)
            {
                await play(session.StoryContext).ConfigureAwait(false);
            }

            var (milliseconds, allocated) = measurement.Stop();

            if (i >= budget.WarmupIterations)
            {
                durations.Add(milliseconds);
                allocations.Add(allocated);
                counts ??= session.CaptureRenderCounts();
            }
        }

        durations.Sort();
        allocations.Sort();
        var floor = await AllocationFloorAsync(measureOptions, cancellationToken).ConfigureAwait(false);
        return counts! with
        {
            MedianMilliseconds = durations[durations.Count / 2],
            MinMilliseconds = durations[0],
            MaxMilliseconds = durations[^1],
            AllocatedBytes = Math.Max(0, allocations[allocations.Count / 2] - floor),
            Iterations = durations.Count,
        };
    }

    private static readonly SemaphoreSlim FloorGate = new(1, 1);
    private static long? _allocationFloor;

    /// <summary>
    /// Allocations of rendering an empty story: bUnit's own per-render overhead (root wrappers, markup generation).
    /// Subtracted from measurements so allocation budgets reflect the component, not the harness.
    /// </summary>
    private static async Task<long> AllocationFloorAsync(BunitStoryOptions options, CancellationToken cancellationToken)
    {
        if (_allocationFloor is { } cached)
        {
            return cached;
        }

        await FloorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_allocationFloor is { } again)
            {
                return again;
            }

            var empty = StoryCatalog.FromCollections([new CalibrationStories()]).Stories[0];
            var samples = new List<long>();
            for (var i = 0; i < 6; i++)
            {
                var measurement = new RenderMeasurement();
                await using var session = await BunitStorySession.StartAsync(empty, options, recorder: null, measurement, cancellationToken).ConfigureAwait(false);
                var (_, allocated) = measurement.Stop();
                if (i > 0)
                {
                    samples.Add(allocated);
                }
            }

            samples.Sort();
            _allocationFloor = samples[0];
            return samples[0];
        }
        finally
        {
            FloorGate.Release();
        }
    }

    private sealed class CalibrationStories : Stories<Composition>
    {
        public override string Title => "BlazorTestPlatform/Calibration";

        protected override void Define(StoryCollectionBuilder<Composition> stories) => stories.Add("Empty");
    }

    private static StoryResult Base(Story story, string key, DateTimeOffset started) => new()
    {
        Key = key,
        StoryId = story.Id,
        Title = story.Title,
        Name = story.Name,
        Component = story.ComponentType.FullName,
        Driver = "bunit",
        HostingModel = HostingModel.InMemory.ToToken(),
        StartedAt = started,
    };

    private static StoryResult Write(BunitStoryOptions options, BtpSettings settings, StoryResult result)
    {
        if (options.WriteResults)
        {
            try
            {
                settings.CreateResultsStore().Write(result);
            }
            catch (IOException)
            {
                // Results are diagnostics; never fail a test because the results directory is locked.
            }
        }

        return result;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
