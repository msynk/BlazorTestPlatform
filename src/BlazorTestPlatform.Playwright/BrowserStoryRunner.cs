using System.Diagnostics;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;
using BlazorTestPlatform.Visual;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Playwright;

/// <summary>Options for browser story runs.</summary>
public sealed class BrowserRunOptions
{
    /// <summary>Execute play functions (skipped automatically under static SSR). Default true.</summary>
    public bool RunPlay { get; set; } = true;

    /// <summary>Compare screenshots against baselines. Default true.</summary>
    public bool Visual { get; set; } = true;

    /// <summary>Run axe-core. Default true.</summary>
    public bool Accessibility { get; set; } = true;

    /// <summary>Fail on console errors and uncaught page errors. Default true. A visible Blazor error UI always fails the run.</summary>
    public bool FailOnConsoleErrors { get; set; } = true;

    /// <summary>Console messages containing any of these are ignored.</summary>
    public List<string> IgnoredConsoleMessages { get; } = ["favicon", "Failed to load resource: the server responded with a status of 404"];

    /// <summary>Retries for failed attempts (default from <see cref="BtpSettings.Retries"/>). A pass after a failure is reported as flaky.</summary>
    public int? Retries { get; set; }

    /// <summary>Accessibility policy (default <see cref="BtpSettings.Accessibility"/>).</summary>
    public AccessibilityPolicy? AccessibilityPolicy { get; set; }

    /// <summary>Settings (default <see cref="BtpSettings.Current"/>).</summary>
    public BtpSettings? Settings { get; set; }

    /// <summary>Baseline store (default from settings).</summary>
    public SnapshotStore? Snapshots { get; set; }

    /// <summary>Per-attempt timeout (default story timeout or 60 s).</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Prerender interactive modes. Default true. Stories that call <c>.Prerender(false)</c> are never prerendered.</summary>
    public bool Prerender { get; set; } = true;

    /// <summary>Write JSON results for reports. Default true.</summary>
    public bool WriteResults { get; set; } = true;

    /// <summary>Runs before each navigation (e.g. <c>page.RouteAsync</c> for third-party scripts). See <see cref="StoryPageOptions.ConfigurePageAsync"/>.</summary>
    public Func<Microsoft.Playwright.IPage, MatrixCase, Task>? ConfigurePageAsync { get; set; }
}

/// <summary>Recognizes failures caused by the machine, not the story (exhausted sockets, browser network-stack errors).</summary>
public static class TransientFailures
{
    private static readonly string[] Signatures = ["ERR_NO_BUFFER_SPACE", "ERR_INSUFFICIENT_RESOURCES", "ERR_ADDRESS_IN_USE", "ERR_ADDRESS_UNREACHABLE"];

    /// <summary>True when the failure (message or captured console) carries a known infrastructure signature.</summary>
    public static bool IsInfrastructureFailure(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Signatures.Any(signature =>
            (result.Error?.Contains(signature, StringComparison.Ordinal) ?? false)
            || result.Console.Any(entry => entry.Text.Contains(signature, StringComparison.Ordinal)));
    }
}

/// <summary>
/// Runs one matrix cell end to end in a real browser: navigate (hydration-aware), play, visual snapshot, axe scan,
/// console/Blazor-error checks — with retries, flaky classification and failure artifacts (trace, screenshot, DOM).
/// </summary>
public sealed class BrowserStoryRunner
{
    private readonly WorkbenchBrowser _browser;
    private readonly BrowserRunOptions _options;
    private readonly BtpSettings _settings;
    private readonly SnapshotStore _snapshots;

    /// <summary>Creates a runner.</summary>
    public BrowserStoryRunner(WorkbenchBrowser browser, BrowserRunOptions? options = null)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _options = options ?? new BrowserRunOptions();
        _settings = _options.Settings ?? BtpSettings.Current;
        _snapshots = _options.Snapshots ?? SnapshotStore.FromSettings(_settings);
    }

    /// <summary>Runs a cell and returns the result (never throws for story failures).</summary>
    public async Task<StoryResult> RunAsync(MatrixCase cell, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var key = StoryResult.CreateKey(cell.Story.Id, cell.Browser, cell.Viewport.Name, cell.Theme.Name, cell.HostingModel.ToToken());
        using var activity = BtpTelemetry.StartStory(cell.Story, cell.Browser, cell.HostingModel.ToToken(), cell.Viewport.Name, cell.Theme.Name);
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();

        var capabilities = PlaywrightStoryDriver.CapabilitiesFor(cell.HostingModel, cell.Story.FakeTimeStart is not null);
        if ((cell.Story.SkipReason ?? cell.Story.UnsupportedReason(capabilities, $"{cell.Browser} ({cell.HostingModel.ToToken()})")) is { } skip)
        {
            return Write(Base(cell, key, started) with { Outcome = StoryOutcome.Skipped, SkipReason = skip });
        }

        var maxAttempts = 1 + Math.Max(0, _options.Retries ?? _settings.Retries);
        var attemptErrors = new List<string>();
        var attemptArtifacts = new List<ArtifactReference>();
        var transientRetryUsed = false;
        StoryResult? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await RunAttemptAsync(cell, key, started, attempt, cancellationToken).ConfigureAwait(false);
            if (last.Outcome != StoryOutcome.Failed)
            {
                break;
            }

            attemptErrors.Add($"attempt {attempt}: {last.Error}");
            attemptArtifacts.AddRange(last.Artifacts.Select(a => a with { Description = $"attempt {attempt}: {a.Description ?? a.Kind}" }));

            // Machine-level network-stack exhaustion is not a story failure: allow one extra attempt (reported as flaky).
            if (attempt == maxAttempts && !transientRetryUsed && TransientFailures.IsInfrastructureFailure(last))
            {
                transientRetryUsed = true;
                maxAttempts++;
            }
        }

        var outcome = last!.Outcome == StoryOutcome.Passed && attemptErrors.Count > 0 ? StoryOutcome.Flaky : last.Outcome;
        return Write(last with
        {
            Outcome = outcome,
            Attempts = Math.Min(maxAttempts, attemptErrors.Count + 1),
            AttemptErrors = outcome == StoryOutcome.Failed ? attemptErrors.Take(attemptErrors.Count - 1).ToArray() : attemptErrors,

            // A flaky run keeps the diagnostics of the attempts that failed: they are what explains the flake.
            Artifacts = outcome == StoryOutcome.Flaky ? [.. attemptArtifacts, .. last.Artifacts] : last.Artifacts,
            DurationMilliseconds = clock.Elapsed.TotalMilliseconds,
        });
    }

    /// <summary>Runs a cell and throws <see cref="StoryFailedException"/> when it fails.</summary>
    public async Task<StoryResult> VerifyAsync(MatrixCase cell, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(cell, cancellationToken).ConfigureAwait(false);
        return result.Outcome == StoryOutcome.Failed ? throw new StoryFailedException(result) : result;
    }

    private async Task<StoryResult> RunAttemptAsync(MatrixCase cell, string key, DateTimeOffset started, int attempt, CancellationToken cancellationToken)
    {
        var story = cell.Story;
        var recorder = new StepRecorder();
        var artifacts = new List<ArtifactReference>();
        var artifactDirectory = Path.Combine(_settings.CreateResultsStore().ArtifactsDirectoryFor(key), $"attempt-{attempt}");
        var keepDiagnostics = _settings.Diagnostics;
        var limit = _options.Timeout ?? story.Timeout ?? TimeSpan.FromSeconds(60);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);

        StoryPage? page = null;
        VisualResult? visual = null;
        AccessibilityReport? a11y = null;
        IReadOnlyList<string> a11yFailures = [];
        string? markup = null;
        BrowserMetrics? metrics = null;
        IReadOnlyList<BudgetViolation> budgetViolations = [];

        async Task<StoryResult> AttemptAsync()
        {
            page = await _browser.CreatePageAsync(cell, new StoryPageOptions
            {
                Trace = keepDiagnostics != DiagnosticsMode.Off,
                Prerender = _options.Prerender && story.Prerender,
                ConfigurePageAsync = _options.ConfigurePageAsync,
            }).ConfigureAwait(false);
            await recorder.RunAsync($"open {cell}", StepKind.Render, page.NavigateAsync).ConfigureAwait(false);
            if (page.UnsupportedOverrides is { } unsupported)
            {
                recorder.Log($"warning: these story services do not apply in browsers (the application's are used): {unsupported}");
            }

            var context = page.CreateStoryContext(recorder, _settings.DefaultTimeout, timeout.Token);

            // Snapshots taken before (or without) the play function get their own "initial" baseline, so static SSR —
            // which cannot run play scripts — is compared with the initial render instead of the post-play state.
            var playRuns = _options.RunPlay && story.Play is not null && cell.HostingModel.IsInteractive();
            if (_options.Visual && !story.Visual.Skip && !story.Visual.CaptureAfterPlay)
            {
                visual = await CaptureAsync(page, recorder, story.Play is null ? null : "initial", attempt, timeout.Token).ConfigureAwait(false);
            }

            if (_options.RunPlay && story.Play is { } play)
            {
                if (playRuns)
                {
                    await recorder.RunAsync("play", StepKind.Step, () => play(context)).ConfigureAwait(false);
                    await page.Driver.WaitForIdleAsync(timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    recorder.Log("play skipped: static SSR is not interactive");
                }
            }

            if (_options.Visual && !story.Visual.Skip && story.Visual.CaptureAfterPlay)
            {
                visual = await CaptureAsync(page, recorder, story.Play is not null && !playRuns ? "initial" : null, attempt, timeout.Token).ConfigureAwait(false);
            }

            metrics = await SafeBrowserMetricsAsync(page).ConfigureAwait(false);
            if (story.BrowserBudget is { } budget && metrics is not null)
            {
                await recorder.RunAsync("browser budget", StepKind.Check, () =>
                {
                    var (violations, notReported) = budget.Evaluate(metrics);
                    if (notReported.Count > 0)
                    {
                        recorder.Log($"not checked ({cell.Browser} does not report them): {string.Join(", ", notReported)}");
                    }

                    budgetViolations = violations;
                    return violations.Count == 0
                        ? Task.CompletedTask
                        : throw new StoryAssertionException($"Browser performance budget exceeded in '{story.Id}' ({metrics}):{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", violations));
                }).ConfigureAwait(false);
            }

            if (_options.Accessibility && !story.Accessibility.Skip)
            {
                var policy = _options.AccessibilityPolicy ?? _settings.Accessibility;
                a11y = await recorder.RunAsync("accessibility (axe-core)", StepKind.Check, () => AxeAccessibilityScanner.ScanAsync(page.Page, policy, story.Accessibility)).ConfigureAwait(false);
                a11y = a11y with { Waivers = policy.WaiversFor(story.Id, story.Accessibility) };
                var evaluation = policy.Evaluate(a11y, story.Id, story.Accessibility);
                a11yFailures = evaluation.Failures.Select(f => f.RuleId).ToArray();
                if (!evaluation.Passed)
                {
                    throw new AccessibilityViolationException(evaluation, cell.ToString());
                }
            }

            await CheckRuntimeErrorsAsync(page).ConfigureAwait(false);

            if (visual is { IsFailure: true })
            {
                throw new StoryAssertionException(DescribeVisualFailure(visual));
            }

            markup = await SafeMarkupAsync(page).ConfigureAwait(false);
            var httpRequests = await SafeHttpRequestsAsync(page).ConfigureAwait(false);
            if (keepDiagnostics == DiagnosticsMode.Always)
            {
                await CollectArtifactsAsync(page, artifactDirectory, artifacts, failed: false).ConfigureAwait(false);
            }

            return Base(cell, key, started) with
            {
                Outcome = StoryOutcome.Passed,
                Attempts = attempt,
                Steps = recorder.Steps,
                Visual = visual,
                Accessibility = a11y,
                Console = page.Diagnostics.Console,
                Network = page.Diagnostics.Network,
                HttpRequests = httpRequests,
                Artifacts = artifacts,
                Markup = markup,
                BrowserMetrics = metrics,
            };
        }

        try
        {
            // The timeout is enforced, not just requested: navigation, page evaluation, axe and captures do not all
            // observe the token, and a story that blocks the browser's main thread (a WebAssembly loop) never returns.
            // Disposing the page in the finally block stops whatever is still running.
            return await AttemptAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (StorySkippedException ex)
        {
            return Base(cell, key, started) with { Outcome = StoryOutcome.Skipped, SkipReason = ex.Message, Steps = recorder.Steps };
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled (test run aborted): that is not a story failure, and nothing must be recorded as one.
            throw ex as OperationCanceledException ?? new OperationCanceledException("The story run was cancelled.", ex, cancellationToken);
        }
        catch (Exception ex)
        {
            var timedOut = ex is OperationCanceledException && timeout.IsCancellationRequested;
            IReadOnlyList<HttpRequestRecord> failedRequests = [];
            if (page is not null)
            {
                markup = await SafeMarkupAsync(page).ConfigureAwait(false);
                failedRequests = await SafeHttpRequestsAsync(page).ConfigureAwait(false);
                if (keepDiagnostics != DiagnosticsMode.Off)
                {
                    await CollectArtifactsAsync(page, artifactDirectory, artifacts, failed: true).ConfigureAwait(false);
                }
            }

            if (visual?.DiffPath is { } diff)
            {
                artifacts.Add(new ArtifactReference("diff", diff, "Visual difference"));
            }

            // A baseline written by this attempt (missing/changed update modes) shows a state that failed another check:
            // it must not become the reference.
            if (visual is not null && _snapshots.Revert(visual))
            {
                recorder.Log($"baseline '{visual.SnapshotName}' was not kept because the run failed");
                visual = visual with { Note = "The baseline written by this run was removed because the run failed." };
            }

            return Base(cell, key, started) with
            {
                Outcome = StoryOutcome.Failed,
                Attempts = attempt,
                Error = timedOut ? $"Story timed out after {limit.TotalSeconds:0.#} s." : ex.Message,
                ErrorType = ex.GetType().FullName,
                StackTrace = ex.StackTrace,
                Steps = recorder.Steps,
                Visual = visual,
                Accessibility = a11y,
                AccessibilityFailures = a11yFailures,
                Console = page?.Diagnostics.Console ?? [],
                Network = page?.Diagnostics.Network ?? [],
                HttpRequests = failedRequests,
                Artifacts = artifacts,
                Markup = markup,
                BrowserMetrics = metrics,
                BudgetViolations = budgetViolations,
            };
        }
        finally
        {
            if (page is not null)
            {
                try
                {
                    await page.DisposeAsync().AsTask().WaitAsync(DiagnosticsTimeout * 2).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    // A crashed browser or a hung page: the context is gone or will be collected with the browser.
                }
            }
        }
    }

    /// <summary>Longest time one diagnostics step (screenshot, DOM, trace, markup) may take on a failing page.</summary>
    private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(10);

    private async Task<VisualResult> CaptureAsync(StoryPage page, StepRecorder recorder, string? variant, int attempt, CancellationToken cancellationToken)
    {
        var story = page.Cell.Story;
        return await recorder.RunAsync(variant is null ? "visual snapshot" : $"visual snapshot ({variant})", StepKind.Check, async () =>
        {
            await page.Page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = Scripts.StabilizeCss + (story.Visual.Css ?? string.Empty) }).ConfigureAwait(false);
            if (story.Visual.WaitForSelector is { } selector)
            {
                await page.Page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible }).ConfigureAwait(false);
            }

            if (story.Visual.Delay > TimeSpan.Zero)
            {
                await Task.Delay(story.Visual.Delay, cancellationToken).ConfigureAwait(false);
            }

            await page.Page.EvaluateAsync(Scripts.WaitForAssets).ConfigureAwait(false);
            var masks = story.Visual.Masks.Select(m => page.Page.Locator(m)).ToArray();
            byte[] png;
            if (story.Visual.FullPage)
            {
                png = await page.Page.ScreenshotAsync(new PageScreenshotOptions
                {
                    FullPage = true,
                    Animations = ScreenshotAnimations.Disabled,
                    Caret = ScreenshotCaret.Hide,
                    Scale = ScreenshotScale.Css,
                    Mask = masks,
                    MaskColor = "#FF00FF",
                }).ConfigureAwait(false);
            }
            else
            {
                png = await page.Root.ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Animations = ScreenshotAnimations.Disabled,
                    Caret = ScreenshotCaret.Hide,
                    Scale = ScreenshotScale.Css,
                    Mask = masks,
                    MaskColor = "#FF00FF",
                }).ConfigureAwait(false);
            }

            var name = SnapshotNaming.For(story.Id, page.Cell.Viewport.Name, page.Cell.Theme.Name, page.Cell.Browser, variant: variant);

            // Every render mode and attempt keeps its own actual/diff images: a later mode that matches, or a passing
            // retry, must not delete the evidence of an earlier mismatch.
            return _snapshots.Compare(
                name,
                png,
                new ImageCompareOptions { Threshold = story.Visual.Threshold ?? _settings.VisualThreshold },
                story.Visual.MaxDiffPixels ?? 0,
                story.Visual.MaxDiffRatio ?? _settings.VisualMaxDiffRatio,
                RendererOf(page),
                outputQualifier: $"{page.Cell.HostingModel.ToToken()}.attempt-{attempt}");
        }).ConfigureAwait(false);
    }

    private async Task CheckRuntimeErrorsAsync(StoryPage page)
    {
        var errorUi = await page.Page.EvaluateAsync<string?>(Scripts.BlazorErrorUi).ConfigureAwait(false);
        if (errorUi is not null)
        {
            throw new StoryAssertionException($"The Blazor error UI is visible: {errorUi}{Environment.NewLine}{FormatErrors(page.Diagnostics.Errors([]))}");
        }

        // The story's own error boundary catches exceptions thrown after hydration; they must fail the run even when
        // console errors are tolerated.
        var storyError = await page.Page.EvaluateAsync<string?>(Scripts.StoryError).ConfigureAwait(false);
        if (storyError is not null)
        {
            throw new StoryAssertionException($"The story failed to render: {storyError}{Environment.NewLine}{FormatErrors(page.Diagnostics.Errors([]))}");
        }

        if (_options.FailOnConsoleErrors)
        {
            var errors = page.Diagnostics.Errors(_options.IgnoredConsoleMessages);
            if (errors.Count > 0)
            {
                throw new StoryAssertionException($"{errors.Count} console/page error(s) while running the story:{Environment.NewLine}{FormatErrors(errors)}");
            }
        }
    }

    private static string FormatErrors(IReadOnlyList<ConsoleEntry> errors) =>
        string.Join(Environment.NewLine, errors.Take(10).Select(e => $"  [{e.Level} @{e.AtMilliseconds:0}ms] {e.Text}"));

    /// <summary>The installed Microsoft.Playwright version (e.g. <c>1.63.0</c>; its informational version is not stamped).</summary>
    public static string PlaywrightVersion { get; } = typeof(IPage).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static RenderEnvironment RendererOf(StoryPage page) =>
        new(page.Cell.Browser, SnapshotNaming.CurrentPlatform, PlaywrightVersion, page.Context.Browser?.Version);

    private static string DescribeVisualFailure(VisualResult visual) => DescribeVisualStatus(visual) + (visual.Note is null ? string.Empty : " " + visual.Note);

    private static string DescribeVisualStatus(VisualResult visual) => visual.Status switch
    {
        VisualStatus.MissingBaseline => $"Missing visual baseline '{visual.SnapshotName}'. Run locally (or set BTP_UPDATE_SNAPSHOTS=missing) to create it, then commit it. Actual: {visual.ActualPath}",
        VisualStatus.SizeMismatch => $"Visual snapshot '{visual.SnapshotName}' changed size ({visual.Width}x{visual.Height}). Diff: {visual.DiffPath}. Approve with 'btp approve' if intended.",
        _ => $"Visual snapshot '{visual.SnapshotName}' differs by {visual.DiffPixels} px ({visual.DiffRatio:P2}). Diff: {visual.DiffPath}. Approve with 'btp approve' if intended.",
    };

    /// <summary>Collects what can still be collected: each step is bounded, and a dead or hung page only loses its own step.</summary>
    private static async Task CollectArtifactsAsync(StoryPage page, string directory, List<ArtifactReference> artifacts, bool failed)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException)
        {
            return;
        }

        var screenshot = Path.Combine(directory, failed ? "failure.png" : "final.png");
        if (await BoundedAsync(() => page.Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true, Timeout = (float)DiagnosticsTimeout.TotalMilliseconds })).ConfigureAwait(false))
        {
            artifacts.Add(new ArtifactReference("screenshot", screenshot, failed ? "Page at failure" : "Final page"));
        }

        var dom = Path.Combine(directory, "dom.html");
        if (await BoundedAsync(async () => await File.WriteAllTextAsync(dom, await page.Page.ContentAsync().ConfigureAwait(false)).ConfigureAwait(false)).ConfigureAwait(false))
        {
            artifacts.Add(new ArtifactReference("dom", dom, "DOM snapshot"));
        }

        string? trace = null;
        if (await BoundedAsync(async () => trace = await page.SaveTraceAsync(Path.Combine(directory, "trace.zip")).ConfigureAwait(false)).ConfigureAwait(false) && trace is not null)
        {
            artifacts.Add(new ArtifactReference("trace", trace, "Open with: pwsh playwright.ps1 show-trace " + trace));
        }
    }

    /// <summary>Runs one diagnostics step with <see cref="DiagnosticsTimeout"/>; false when it failed or timed out.</summary>
    private static async Task<bool> BoundedAsync(Func<Task> step)
    {
        try
        {
            await step().WaitAsync(DiagnosticsTimeout).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The page may be gone (crash, closed circuit) or hung; keep whatever was collected.
            return false;
        }
    }

    private static async Task<IReadOnlyList<HttpRequestRecord>> SafeHttpRequestsAsync(StoryPage page)
    {
        try
        {
            return await page.Driver.GetHttpRequestsAsync(CancellationToken.None).WaitAsync(DiagnosticsTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return [];
        }
    }

    // Metrics are diagnostics: a page that cannot report them (crashed, navigated away) does not fail the run for it.
    private static async Task<BrowserMetrics?> SafeBrowserMetricsAsync(StoryPage page)
    {
        try
        {
            var json = await page.Page.EvaluateAsync<string?>(Scripts.Vitals).WaitAsync(DiagnosticsTimeout).ConfigureAwait(false);
            return json is null ? null : System.Text.Json.JsonSerializer.Deserialize<BrowserMetrics>(json, MetricsJson);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions MetricsJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static async Task<string?> SafeMarkupAsync(StoryPage page)
    {
        try
        {
            var html = await page.Root.InnerHTMLAsync(new LocatorInnerHTMLOptions { Timeout = 2000 }).WaitAsync(DiagnosticsTimeout).ConfigureAwait(false);
            return html.Length > 20_000 ? html[..20_000] + "…" : html;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return null;
        }
    }

    private static StoryResult Base(MatrixCase cell, string key, DateTimeOffset started) => new()
    {
        Key = key,
        StoryId = cell.Story.Id,
        Title = cell.Story.Title,
        Name = cell.Story.Name,
        Component = cell.Story.ComponentType.FullName,
        SourceFile = cell.Story.SourceFile,
        SourceLine = cell.Story.SourceLine > 0 ? cell.Story.SourceLine : null,
        Driver = cell.Browser,
        HostingModel = cell.HostingModel.ToToken(),
        Viewport = cell.Viewport.Name,
        Theme = cell.Theme.Name,
        StartedAt = started,
    };

    private StoryResult Write(StoryResult result)
    {
        BtpTelemetry.Complete(System.Diagnostics.Activity.Current is { OperationName: "btp.story" } story ? story : null, result);
        if (_options.WriteResults)
        {
            try
            {
                _settings.CreateResultsStore().Write(result);
            }
            catch (IOException)
            {
                // Diagnostics only.
            }
        }

        return result;
    }
}
