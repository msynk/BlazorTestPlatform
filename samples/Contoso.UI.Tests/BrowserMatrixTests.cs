using BlazorTestPlatform.Workbench;
using Contoso.UI.Stories;

namespace Contoso.UI.Tests;

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
/// HTML report and accept intended changes with <c>btp approve</c>.
/// </summary>
public sealed class VisualTests(WorkbenchFixture workbench)
{
    public static readonly TestMatrix Matrix = new TestMatrix()
        .WithViewports(Viewport.Desktop, Viewport.Mobile)
        .WithThemes(ThemeDefinition.Light, ThemeDefinition.Dark)
        .WithHostingModels(HostingModel.InteractiveServer)
        .WithBrowsers(Browsers.Chromium);

    public static TheoryData<string> Cases => new(Matrix
        .Expand(ComponentStoryTests.Catalog.Where(s => !s.Visual.Skip && (!s.HasTag("visual-matrix") || s.Name.Contains("Primary", StringComparison.Ordinal))))
        .Where(c => c.Viewport == Viewport.Desktop || c.Theme == ThemeDefinition.Light)
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
/// Playwright container as Chromium's.
/// </summary>
public sealed class CrossBrowserTests(WorkbenchFixture workbench)
{
    public static readonly TestMatrix Matrix = new TestMatrix()
        .WithViewports(Viewport.Desktop)
        .WithThemes(ThemeDefinition.Light)
        .WithHostingModels(HostingModel.InteractiveServer)
        .WithBrowsers(Browsers.Firefox, Browsers.WebKit);

    public static TheoryData<string> Cases => new(Matrix
        .Expand(ComponentStoryTests.Catalog.Where(s => !s.HasTag("visual-matrix") || s.Name.Contains("Primary", StringComparison.Ordinal)))
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
