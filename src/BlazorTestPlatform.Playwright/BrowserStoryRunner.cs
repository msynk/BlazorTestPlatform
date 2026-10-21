using System.Diagnostics;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
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
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();

        if (cell.Story.SkipReason is { } skip)
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout ?? story.Timeout ?? TimeSpan.FromSeconds(60));

        StoryPage? page = null;
        VisualResult? visual = null;
        AccessibilityReport? a11y = null;
        IReadOnlyList<string> a11yFailures = [];
        string? markup = null;

        try
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
                visual = await CaptureAsync(page, recorder, story.Play is null ? null : "initial").ConfigureAwait(false);
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
                visual = await CaptureAsync(page, recorder, story.Play is not null && !playRuns ? "initial" : null).ConfigureAwait(false);
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
            };
        }
        catch (StorySkippedException ex)
        {
            return Base(cell, key, started) with { Outcome = StoryOutcome.Skipped, SkipReason = ex.Message, Steps = recorder.Steps };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || timeout.IsCancellationRequested)
        {
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

            return Base(cell, key, started) with
            {
                Outcome = StoryOutcome.Failed,
                Attempts = attempt,
                Error = ex is OperationCanceledException ? "Story timed out." : ex.Message,
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
            };
        }
        finally
        {
            if (page is not null)
            {
                await page.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<VisualResult> CaptureAsync(StoryPage page, StepRecorder recorder, string? variant)
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
                await Task.Delay(story.Visual.Delay).ConfigureAwait(false);
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
            return _snapshots.Compare(
                name,
                png,
                new ImageCompareOptions { Threshold = story.Visual.Threshold ?? _settings.VisualThreshold },
                story.Visual.MaxDiffPixels ?? 0,
                story.Visual.MaxDiffRatio ?? _settings.VisualMaxDiffRatio,
                RendererOf(page));
        }).ConfigureAwait(false);
    }

    private async Task CheckRuntimeErrorsAsync(StoryPage page)
    {
        var errorUi = await page.Page.EvaluateAsync<string?>(Scripts.BlazorErrorUi).ConfigureAwait(false);
        if (errorUi is not null)
        {
            throw new StoryAssertionException($"The Blazor error UI is visible: {errorUi}{Environment.NewLine}{FormatErrors(page.Diagnostics.Errors([]))}");
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

    private static async Task CollectArtifactsAsync(StoryPage page, string directory, List<ArtifactReference> artifacts, bool failed)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var screenshot = Path.Combine(directory, failed ? "failure.png" : "final.png");
            await page.Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true }).ConfigureAwait(false);
            artifacts.Add(new ArtifactReference("screenshot", screenshot, failed ? "Page at failure" : "Final page"));

            var dom = Path.Combine(directory, "dom.html");
            await File.WriteAllTextAsync(dom, await page.Page.ContentAsync().ConfigureAwait(false)).ConfigureAwait(false);
            artifacts.Add(new ArtifactReference("dom", dom, "DOM snapshot"));

            var trace = await page.SaveTraceAsync(Path.Combine(directory, "trace.zip")).ConfigureAwait(false);
            if (trace is not null)
            {
                artifacts.Add(new ArtifactReference("trace", trace, "Open with: pwsh playwright.ps1 show-trace " + trace));
            }
        }
        catch (PlaywrightException)
        {
            // The page may be gone (crash, closed circuit); keep whatever was collected.
        }
    }

    private static async Task<IReadOnlyList<HttpRequestRecord>> SafeHttpRequestsAsync(StoryPage page)
    {
        try
        {
            return await page.Driver.GetHttpRequestsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            return [];
        }
    }

    private static async Task<string?> SafeMarkupAsync(StoryPage page)
    {
        try
        {
            var html = await page.Root.InnerHTMLAsync(new LocatorInnerHTMLOptions { Timeout = 2000 }).ConfigureAwait(false);
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
        Driver = cell.Browser,
        HostingModel = cell.HostingModel.ToToken(),
        Viewport = cell.Viewport.Name,
        Theme = cell.Theme.Name,
        StartedAt = started,
    };

    private StoryResult Write(StoryResult result)
    {
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
