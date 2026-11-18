using BlazorTestPlatform.Workbench;
using BlazorTestPlatform.Demo.UI.Stories;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// The cross-hosting matrix: every interactive story's play script runs under Static SSR, Server, WebAssembly and Auto
/// in a real browser — catching hydration, circuit and runtime differences that in-memory tests cannot see.
/// </summary>
public sealed class CrossHostingTests(WorkbenchFixture workbench)
{
    public static readonly TestMatrix Matrix = TestMatrix.CrossHosting();

    // BTP_SHARD=2/4 runs a quarter of the cells, so CI can spread the matrix over parallel jobs.
    public static TheoryData<string> Cases => new(Matrix.Expand(ComponentStoryTests.Catalog.WithPlay()).InShard(Shard.FromEnvironment()).Select(c => c.Key));

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Story_passes_under_every_render_mode(string key) =>
        workbench.Environment.VerifyAsync(Matrix.Resolve(ComponentStoryTests.Catalog, key), TestContext.Current.CancellationToken);
}

/// <summary>
/// Visual regression + axe-core across themes and viewports. Baselines live in <c>__snapshots__</c>; review diffs in the
/// HTML report and accept intended changes with <c>btp approve</c>. Variant matrices (stories tagged
/// <c>visual-matrix</c>) are about the component's states, so each state is compared once, on desktop in the light theme.
/// </summary>
public sealed class VisualTests(WorkbenchFixture workbench)
{
    public static readonly TestMatrix Matrix = new TestMatrix()
        .WithViewports(Viewport.Desktop, Viewport.Mobile)
        .WithThemes(ThemeDefinition.Light, ThemeDefinition.Dark)
        .WithHostingModels(HostingModel.InteractiveServer)
        .WithBrowsers(Browsers.Chromium);

    public static TheoryData<string> Cases => new(Matrix
        .Expand(ComponentStoryTests.Catalog.Where(s => !s.Visual.Skip))
        .Where(c => c.Viewport == Viewport.Desktop || c.Theme == ThemeDefinition.Light)
        .Where(c => !c.Story.HasTag("visual-matrix") || (c.Viewport == Viewport.Desktop && c.Theme == ThemeDefinition.Light))
        .InShard(Shard.FromEnvironment())
        .Select(c => c.Key));

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Story_matches_baseline(string key) =>
        workbench.Environment.VerifyAsync(Matrix.Resolve(ComponentStoryTests.Catalog, key), TestContext.Current.CancellationToken);
}

/// <summary>
/// Firefox and WebKit: every story's play script, visual snapshot and axe-core scan on desktop/light under Interactive
/// Server, against <c>firefox-*</c> and <c>webkit-*</c> baselines. CI gates on the Linux ones, rendered in the same
/// Playwright container as Chromium's. Variant matrices stay in Chromium: the states they enumerate don't depend on the
/// browser engine.
/// </summary>
public sealed class CrossBrowserTests(WorkbenchFixture workbench)
{
    public static readonly TestMatrix Matrix = new TestMatrix()
        .WithViewports(Viewport.Desktop)
        .WithThemes(ThemeDefinition.Light)
        .WithHostingModels(HostingModel.InteractiveServer)
        .WithBrowsers(Browsers.Firefox, Browsers.WebKit);

    public static TheoryData<string> Cases => new(Matrix
        .Expand(ComponentStoryTests.Catalog.Where(s => !s.HasTag("visual-matrix")))
        .InShard(Shard.FromEnvironment())
        .Select(c => c.Key));

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Story_passes_in_every_browser(string key) =>
        workbench.Environment.VerifyAsync(Matrix.Resolve(ComponentStoryTests.Catalog, key), TestContext.Current.CancellationToken);
}

/// <summary>
/// The workbench's "Run interactions" button runs play functions inside the frame (the in-app driver). Every play script
/// must pass there too, under Server and WebAssembly: a third driver held to the same semantics as bUnit and Playwright.
/// </summary>
public sealed class InAppPlayTests(WorkbenchFixture workbench)
{
    public static readonly TestMatrix Matrix = new TestMatrix()
        .WithViewports(Viewport.Desktop)
        .WithThemes(ThemeDefinition.Light)
        .WithHostingModels(HostingModel.InteractiveServer, HostingModel.InteractiveWebAssembly)
        .WithBrowsers(Browsers.Chromium);

    // Stories that declare capabilities the in-app driver lacks (browser emulation) are reported as skipped there.
    public static TheoryData<string> Cases => new(Matrix
        .Expand(ComponentStoryTests.Catalog.WithPlay().Where(s => s.CanRunOn(InAppStoryDriver.CapabilitiesFor(s))))
        .Select(c => c.Key));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Play_runs_inside_the_frame(string key)
    {
        var cell = Matrix.Resolve(ComponentStoryTests.Catalog, key);
        await using var page = await workbench.Environment.Browser.OpenAsync(cell, new BlazorTestPlatform.Playwright.StoryPageOptions { AutoPlay = true });

        var result = await page.WaitForInAppPlayAsync();

        Assert.True(result.Status == "passed", $"{result.Status} ({result.Renderer}): {result.Error}{Environment.NewLine}{result.StepsJson}");
    }
}

public sealed class TransientFailureTests
{
    private static BlazorTestPlatform.Results.StoryResult Failed(string error, string? console = null) => new()
    {
        Key = "k", StoryId = "a--b", Title = "A", Name = "B", Driver = "chromium", Outcome = BlazorTestPlatform.Results.StoryOutcome.Failed, Error = error,
        Console = console is null ? [] : [new BlazorTestPlatform.Results.ConsoleEntry { Level = "error", Text = console }],
    };

    [Fact]
    public void Machine_level_network_errors_are_recognized_but_story_failures_are_not()
    {
        Assert.True(BlazorTestPlatform.Playwright.TransientFailures.IsInfrastructureFailure(Failed("2 console errors", "WebSocket connection failed: net::ERR_NO_BUFFER_SPACE")));
        Assert.False(BlazorTestPlatform.Playwright.TransientFailures.IsInfrastructureFailure(Failed("Expected role=button to be focused")));
        Assert.False(BlazorTestPlatform.Playwright.TransientFailures.IsInfrastructureFailure(Failed("Visual snapshot differs", "Failed to load resource: 500")));
    }
}

/// <summary>Real browsers report runtime metrics with every result, and stories can set budgets on them.</summary>
public sealed class BrowserMetricsTests(WorkbenchFixture workbench)
{
    [Theory]
    [InlineData(Browsers.Chromium)]
    [InlineData(Browsers.Firefox)]
    public async Task Results_carry_the_metrics_the_browser_reports(string browser)
    {
        var matrix = new TestMatrix()
            .WithViewports(Viewport.Desktop)
            .WithThemes(ThemeDefinition.Light)
            .WithHostingModels(HostingModel.InteractiveServer)
            .WithBrowsers(browser);
        var cell = matrix.Expand(ComponentStoryTests.Catalog.Where(s => s.Id == "testing-browser-emulation--geolocation")).Single();

        var result = await workbench.Environment.VerifyAsync(cell, TestContext.Current.CancellationToken);

        var metrics = Assert.IsType<BlazorTestPlatform.Performance.BrowserMetrics>(result.BrowserMetrics);
        Assert.InRange(metrics.DomNodes, 5, 20);
        if (browser == Browsers.Chromium)
        {
            // Chromium reports every metric; the budget on layout shift was checked, not skipped.
            Assert.NotNull(metrics.CumulativeLayoutShift);
            Assert.NotNull(metrics.LongTasks);
            Assert.NotNull(metrics.JSHeapBytes);
            Assert.DoesNotContain(result.Steps.SelectMany(s => s.Children.Prepend(s)), s => s.Title.StartsWith("not checked", StringComparison.Ordinal));
        }
        else
        {
            // Firefox has no layout-shift entries: the limit is reported as not checked instead of passing silently.
            Assert.Null(metrics.CumulativeLayoutShift);
            Assert.Contains(result.Steps.SelectMany(s => s.Children.Prepend(s)), s => s.Title.Contains("Cumulative layout shift", StringComparison.Ordinal));
        }
    }
}

/// <summary>Right-to-left and high-contrast themes reach the browser: document direction, media emulation and the story.</summary>
public sealed class ThemeEmulationTests(WorkbenchFixture workbench)
{
    [Theory]
    [InlineData(Browsers.Chromium)]
    [InlineData(Browsers.Firefox)]
    [InlineData(Browsers.WebKit)]
    public async Task Right_to_left_themes_mirror_the_story(string browser)
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("inputs-button--primary"), Viewport.Desktop, ThemeDefinition.RightToLeft, HostingModel.InteractiveServer, browser);
        await using var page = await workbench.Environment.Browser.OpenAsync(cell);

        Assert.Equal("rtl", await page.Page.EvaluateAsync<string>("() => document.documentElement.dir"));
        Assert.Equal("rtl", await page.Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('[data-btp-root] button')).direction"));
    }

    [Theory]
    [InlineData(Browsers.Chromium)]
    [InlineData(Browsers.Firefox)]
    [InlineData(Browsers.WebKit)]
    public async Task Culture_and_time_zone_modes_run_the_browser_in_them_and_keep_the_base_look(string browser)
    {
        var mode = ThemeDefinition.Dark.WithCulture("de-DE").WithTimeZone("Asia/Tokyo");
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("inputs-button--primary"), Viewport.Desktop, mode, HostingModel.InteractiveServer, browser);
        await using var page = await workbench.Environment.Browser.OpenAsync(cell);

        Assert.Equal("de-DE", await page.Page.EvaluateAsync<string>("() => navigator.language"));
        Assert.Equal("Asia/Tokyo", await page.Page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone"));
        Assert.Equal("dark", await page.Page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')"));
        Assert.Equal("inputs-button--primary|desktop|dark-de-de-asia-tokyo|server|" + browser, cell.Key);
    }

    [Fact]
    public async Task High_contrast_themes_emulate_forced_colors_and_more_contrast()
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("inputs-button--primary"), Viewport.Desktop, ThemeDefinition.HighContrast, HostingModel.InteractiveServer, Browsers.Chromium);
        await using var page = await workbench.Environment.Browser.OpenAsync(cell);

        Assert.True(await page.Page.EvaluateAsync<bool>("() => matchMedia('(forced-colors: active)').matches"));
        Assert.True(await page.Page.EvaluateAsync<bool>("() => matchMedia('(prefers-contrast: more)').matches"));
    }
}

/// <summary>
/// Prerender parity (<c>CheckPrerender()</c>): the prerendered accessibility tree must survive hydration. The runtime badge
/// changes from "Server" to "WebAssembly" when WebAssembly takes over, which the story declares as an expected failure;
/// under Server and Auto (a fresh browser starts on Server) nothing changes.
/// </summary>
public sealed class PrerenderParityTests(WorkbenchFixture workbench)
{
    [Theory]
    [InlineData(HostingModel.InteractiveServer)]
    [InlineData(HostingModel.InteractiveWebAssembly)]
    [InlineData(HostingModel.InteractiveAuto)]
    public async Task Hydration_keeps_or_reports_the_prerendered_tree(HostingModel model)
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("display-runtime-badge--hydration-parity"), Viewport.Desktop, ThemeDefinition.Light, model, Browsers.Chromium);

        var result = await workbench.Environment.VerifyAsync(cell, TestContext.Current.CancellationToken);

        Assert.Equal(BlazorTestPlatform.Results.StoryOutcome.Passed, result.Outcome);
        if (model == HostingModel.InteractiveWebAssembly)
        {
            Assert.NotNull(result.ExpectedFailure);
            Assert.Contains("Hydration changed what the prerendered story showed", result.Error, StringComparison.Ordinal);
            Assert.Contains("WebAssembly", result.Error, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(result.ExpectedFailure);
            Assert.Contains(result.Steps, s => s.Title == "prerender parity" && !s.Failed);
        }
    }

    [Fact]
    public async Task Design_system_stories_render_the_same_before_and_after_hydration()
    {
        // Every component story without play, with the check on for all of them, under Server.
        var runner = new BlazorTestPlatform.Playwright.BrowserStoryRunner(workbench.Environment.Browser, new BlazorTestPlatform.Playwright.BrowserRunOptions
        {
            PrerenderParity = true,
            Visual = false,
            Accessibility = false,
            WriteResults = false,
        });
        var stories = ComponentStoryTests.Catalog.Where(s => !s.HasPlay && !s.Title.StartsWith("Testing/", StringComparison.Ordinal) && s.Prerender && s.ExpectedFailure is null && s.SkipReason is null).Stories;
        var failures = new List<string>();
        foreach (var story in stories)
        {
            var result = await runner.RunAsync(new MatrixCase(story, Viewport.Desktop, ThemeDefinition.Light, HostingModel.InteractiveServer, Browsers.Chromium), TestContext.Current.CancellationToken);
            if (result.Outcome == BlazorTestPlatform.Results.StoryOutcome.Failed)
            {
                failures.Add($"{story.Id}: {result.Error}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }
}

/// <summary>
/// Circuit pause and resume (<c>ctx.PauseAndResumeCircuitAsync()</c>): under Interactive Server the [PersistentState] count
/// survives a paused circuit and the in-memory one starts over; render modes without a circuit skip the story.
/// </summary>
public sealed class CircuitPauseTests(WorkbenchFixture workbench)
{
    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public async Task Persistent_state_survives_a_paused_circuit(string browser)
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("testing-circuit--pause-and-resume"), Viewport.Desktop, ThemeDefinition.Light, HostingModel.InteractiveServer, browser);

        var result = await workbench.Environment.VerifyAsync(cell, TestContext.Current.CancellationToken);

        Assert.Equal(BlazorTestPlatform.Results.StoryOutcome.Passed, result.Outcome);
        Assert.Contains(result.Steps.SelectMany(s => s.Children.Prepend(s)), s => s.Title == "pause and resume the circuit" && !s.Failed);
    }

    [Theory]
    [InlineData(HostingModel.InteractiveWebAssembly)]
    [InlineData(HostingModel.InteractiveAuto)]
    public async Task Render_modes_without_a_circuit_to_control_skip_it(HostingModel model)
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("testing-circuit--pause-and-resume"), Viewport.Desktop, ThemeDefinition.Light, model, Browsers.Chromium);

        var result = await workbench.Environment.Runner.RunAsync(cell, TestContext.Current.CancellationToken);

        Assert.Equal(BlazorTestPlatform.Results.StoryOutcome.Skipped, result.Outcome);
        Assert.Contains("CircuitControl (pausing and resuming the circuit)", result.SkipReason, StringComparison.Ordinal);
    }
}

/// <summary>
/// Exploration in browsers: the stepper's bug is found by seeded random steps and the failing sequence is shrunk on fresh
/// pages to the two steps that cause it (the story declares the failure with <c>ExpectFailure</c>).
/// </summary>
public sealed class BrowserExplorationTests(WorkbenchFixture workbench)
{
    [Fact]
    public async Task A_failing_exploration_is_shrunk_on_fresh_pages()
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("testing-exploration--stepper"), Viewport.Desktop, ThemeDefinition.Light, HostingModel.InteractiveServer, Browsers.Chromium);

        var result = await workbench.Environment.VerifyAsync(cell, TestContext.Current.CancellationToken);

        Assert.Equal(BlazorTestPlatform.Results.StoryOutcome.Passed, result.Outcome);
        Assert.NotNull(result.ExpectedFailure);
        Assert.Contains("Exploration 'stepper': invariant 'count is never negative' broke after 2 step(s) (shrunk from", result.Error, StringComparison.Ordinal);
        Assert.Equal("add -> remove", System.Text.RegularExpressions.Regex.Match(result.Error!, "Steps: (.*)").Groups[1].Value.Trim());
        Assert.Contains(result.Steps, s => s.Title.StartsWith("shrink exploration stepper", StringComparison.Ordinal));
    }
}
