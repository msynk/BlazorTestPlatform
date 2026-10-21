using BlazorTestPlatform.Playwright.Hosting;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Playwright;

/// <summary>
/// Everything a browser test suite needs, started once: the workbench host, the browsers and a runner.
/// Share it across tests (xUnit assembly/collection fixture, NUnit <c>[OneTimeSetUp]</c>, MSTest <c>[AssemblyInitialize]</c>).
/// </summary>
/// <example>
/// <code>
/// public sealed class Workbench : IAsyncLifetime
/// {
///     public WorkbenchTestEnvironment Environment { get; private set; } = default!;
///     public async ValueTask InitializeAsync() =>
///         Environment = await WorkbenchTestEnvironment.StartAsync(new WorkbenchApplicationFactory&lt;Program&gt;());
///     public ValueTask DisposeAsync() => Environment.DisposeAsync();
/// }
/// </code>
/// </example>
public sealed class WorkbenchTestEnvironment : IAsyncDisposable
{
    private WorkbenchTestEnvironment(IWorkbenchHost host, WorkbenchBrowser browser, BrowserStoryRunner runner)
    {
        Host = host;
        Browser = browser;
        Runner = runner;
    }

    /// <summary>The workbench host.</summary>
    public IWorkbenchHost Host { get; }

    /// <summary>The browsers.</summary>
    public WorkbenchBrowser Browser { get; }

    /// <summary>The runner.</summary>
    public BrowserStoryRunner Runner { get; }

    /// <summary>Starts the host and Playwright.</summary>
    public static async Task<WorkbenchTestEnvironment> StartAsync(IWorkbenchHost host, BrowserRunOptions? runOptions = null, BrowserLaunchSettings? launchSettings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        var browser = await WorkbenchBrowser.LaunchAsync(host.BaseAddress, launchSettings).ConfigureAwait(false);
        return new WorkbenchTestEnvironment(host, browser, new BrowserStoryRunner(browser, runOptions));
    }

    /// <summary>Runs a cell and throws on failure.</summary>
    public Task<StoryResult> VerifyAsync(MatrixCase cell, CancellationToken cancellationToken = default) => Runner.VerifyAsync(cell, cancellationToken);

    /// <summary>Opens a story page for hand-written browser tests (dispose it when done).</summary>
    public Task<StoryPage> OpenAsync(MatrixCase cell, StoryPageOptions? options = null) => Browser.OpenAsync(cell, options);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync().ConfigureAwait(false);
        await Host.DisposeAsync().ConfigureAwait(false);
    }
}
