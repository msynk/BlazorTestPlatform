using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Workbench;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Playwright;

/// <summary>Browser launch options.</summary>
public sealed class BrowserLaunchSettings
{
    /// <summary>Run headless (default true; set <c>BTP_HEADED=1</c> to watch).</summary>
    public bool Headless { get; set; } = !string.Equals(Environment.GetEnvironmentVariable("BTP_HEADED"), "1", StringComparison.Ordinal);

    /// <summary>Slow every operation down by this many milliseconds (debugging).</summary>
    public float? SlowMo { get; set; }

    /// <summary>Browser channel for Chromium (e.g. <c>msedge</c>, <c>chrome</c>).</summary>
    public string? ChromiumChannel { get; set; }

    /// <summary>Extra launch arguments.</summary>
    public List<string> Args { get; } = [];

    /// <summary>Install missing browsers automatically (<c>BTP_INSTALL_BROWSERS=1</c>).</summary>
    public bool InstallMissingBrowsers { get; set; } = string.Equals(Environment.GetEnvironmentVariable("BTP_INSTALL_BROWSERS"), "1", StringComparison.Ordinal);

    /// <summary>Navigation/readiness timeout (WebAssembly downloads can be slow on cold CI agents). Default 60 s.</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Shared Playwright + browser instances for a test run, pointed at a running workbench host. Thread-safe: create one
/// per test assembly (e.g. in a fixture) and open an isolated <see cref="StoryPage"/> per matrix cell.
/// </summary>
public sealed class WorkbenchBrowser : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IBrowser> _browsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPlaywright _playwright;

    private WorkbenchBrowser(IPlaywright playwright, Uri baseAddress, BrowserLaunchSettings settings)
    {
        _playwright = playwright;
        BaseAddress = baseAddress;
        Settings = settings;
    }

    /// <summary>Workbench base address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Launch settings.</summary>
    public BrowserLaunchSettings Settings { get; }

    /// <summary>The Playwright instance.</summary>
    public IPlaywright Playwright => _playwright;

    /// <summary>Starts Playwright for a workbench base address.</summary>
    public static async Task<WorkbenchBrowser> LaunchAsync(Uri baseAddress, BrowserLaunchSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        playwright.Selectors.SetTestIdAttribute(By.TestIdAttribute);
        return new WorkbenchBrowser(playwright, baseAddress, settings ?? new BrowserLaunchSettings());
    }

    /// <summary>Gets (launching on first use) a browser by name.</summary>
    public async Task<IBrowser> GetBrowserAsync(string browserName)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browsers.TryGetValue(browserName, out var existing))
            {
                return existing;
            }

            var type = browserName.ToLowerInvariant() switch
            {
                Browsers.Chromium => _playwright.Chromium,
                Browsers.Firefox => _playwright.Firefox,
                Browsers.WebKit => _playwright.Webkit,
                _ => throw new ArgumentException($"Unknown browser '{browserName}'. Use chromium, firefox or webkit.", nameof(browserName)),
            };

            var options = new BrowserTypeLaunchOptions
            {
                Headless = Settings.Headless,
                SlowMo = Settings.SlowMo,
                Args = Settings.Args,
                Channel = type == _playwright.Chromium ? Settings.ChromiumChannel : null,
            };

            IBrowser browser;
            try
            {
                browser = await type.LaunchAsync(options).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            {
                if (!Settings.InstallMissingBrowsers)
                {
                    throw new InvalidOperationException(
                        $"Playwright browser '{browserName}' is not installed. Run 'pwsh bin/Debug/net10.0/playwright.ps1 install {browserName}' or set BTP_INSTALL_BROWSERS=1.", ex);
                }

                var exitCode = Microsoft.Playwright.Program.Main(["install", browserName]);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"Installing Playwright browser '{browserName}' failed with exit code {exitCode}.", ex);
                }

                browser = await type.LaunchAsync(options).ConfigureAwait(false);
            }

            _browsers[browserName] = browser;
            return browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens a fresh, isolated browser context for a matrix cell, navigates to the story frame and waits until the story
    /// is rendered — and, for interactive modes, hydrated — before returning.
    /// </summary>
    public async Task<StoryPage> OpenAsync(MatrixCase cell, StoryPageOptions? options = null)
    {
        var page = await CreatePageAsync(cell, options).ConfigureAwait(false);
        try
        {
            await page.NavigateAsync().ConfigureAwait(false);
            return page;
        }
        catch (StoryAssertionException ex)
        {
            var errors = page.Diagnostics.Errors([]);
            await page.DisposeAsync().ConfigureAwait(false);
            if (errors.Count == 0)
            {
                throw;
            }

            throw new StoryAssertionException(ex.Message + Environment.NewLine + "Browser errors:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Take(5).Select(e => "  " + e.Text)), ex);
        }
        catch
        {
            await page.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens a fresh, isolated browser context and page for a matrix cell <b>without</b> navigating, so callers can attach
    /// to the page first and keep diagnostics when navigation fails. Call <see cref="StoryPage.NavigateAsync"/> next.
    /// </summary>
    public async Task<StoryPage> CreatePageAsync(MatrixCase cell, StoryPageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cell);
        options ??= new StoryPageOptions();
        var browser = await GetBrowserAsync(cell.Browser).ConfigureAwait(false);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = cell.Viewport.Width, Height = cell.Viewport.Height },
            DeviceScaleFactor = (float)cell.Viewport.DeviceScaleFactor,
            IsMobile = cell.Browser == Browsers.Firefox ? null : cell.Viewport.IsMobile,
            HasTouch = cell.Viewport.HasTouch,
            ColorScheme = cell.Theme.ColorScheme switch
            {
                ColorScheme.Dark => Microsoft.Playwright.ColorScheme.Dark,
                ColorScheme.Light => Microsoft.Playwright.ColorScheme.Light,
                _ => Microsoft.Playwright.ColorScheme.NoPreference,
            },
            ReducedMotion = ReducedMotion.Reduce,
            Locale = options.Locale,
            TimezoneId = options.TimezoneId,
        }).ConfigureAwait(false);

        try
        {
            if (options.Trace)
            {
                await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false, Title = cell.ToString() }).ConfigureAwait(false);
            }

            var page = await context.NewPageAsync().ConfigureAwait(false);
            var diagnostics = new DiagnosticsRecorder(page);
            if (options.ConfigurePageAsync is { } configurePage)
            {
                await configurePage(page, cell).ConfigureAwait(false);
            }

            return new StoryPage(cell, context, page, diagnostics, options, Settings.ReadyTimeout);
        }
        catch
        {
            await context.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var browser in _browsers.Values)
        {
            await browser.CloseAsync().ConfigureAwait(false);
        }

        _browsers.Clear();
        _playwright.Dispose();
        _gate.Dispose();
    }
}

/// <summary>Per-page options.</summary>
public sealed class StoryPageOptions
{
    /// <summary>Record a Playwright trace (screenshots + DOM snapshots per action).</summary>
    public bool Trace { get; set; } = BtpSettings.Current.Diagnostics != DiagnosticsMode.Off;

    /// <summary>Prerender interactive modes (default true, like production).</summary>
    public bool Prerender { get; set; } = true;

    /// <summary>Locale (deterministic default <c>en-US</c>).</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>Time zone (deterministic default <c>UTC</c>).</summary>
    public string TimezoneId { get; set; } = "UTC";

    /// <summary>Run the story's play function inside the frame (the workbench's in-app driver) instead of from the test.</summary>
    public bool AutoPlay { get; set; }

    /// <summary>
    /// Arg overrides as control text (<c>["Variant"] = "Danger"</c>), exactly as the workbench's controls and
    /// <c>?arg.Name=value</c> links pass them. Rejected values are listed in the frame (<c>[data-btp-arg-errors]</c>).
    /// </summary>
    public IDictionary<string, string?> Args { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs before navigation, e.g. to route third-party scripts or static assets with <c>page.RouteAsync</c>.
    /// Mock application HTTP calls with the story's <c>MockHttp</c> instead: it works under Server too.
    /// </summary>
    public Func<IPage, MatrixCase, Task>? ConfigurePageAsync { get; set; }
}

/// <summary>A story frame open in a browser, with its driver, play context and diagnostics.</summary>
public sealed class StoryPage : IAsyncDisposable
{
    private readonly bool _tracing;
    private readonly StoryPageOptions _options;
    private readonly TimeSpan _readyTimeout;
    private bool _traceStopped;

    internal StoryPage(MatrixCase cell, IBrowserContext context, IPage page, DiagnosticsRecorder diagnostics, StoryPageOptions options, TimeSpan readyTimeout)
    {
        Cell = cell;
        Context = context;
        Page = page;
        Diagnostics = diagnostics;
        _options = options;
        _readyTimeout = readyTimeout;
        _tracing = options.Trace;
        Driver = new PlaywrightStoryDriver(page, cell.Browser, cell.HostingModel, cell.Story.FakeTimeStart is not null);
    }

    /// <summary>The matrix cell.</summary>
    public MatrixCase Cell { get; }

    /// <summary>The browser context (isolated cookies/storage).</summary>
    public IBrowserContext Context { get; }

    /// <summary>The page.</summary>
    public IPage Page { get; }

    /// <summary>The driver.</summary>
    public PlaywrightStoryDriver Driver { get; }

    /// <summary>Console/network diagnostics.</summary>
    public DiagnosticsRecorder Diagnostics { get; }

    /// <summary>
    /// Service types the story registers that cannot apply per story in browsers (the frame uses the application's),
    /// comma separated; null when every override applies. Known after <see cref="NavigateAsync"/>.
    /// </summary>
    public string? UnsupportedOverrides { get; private set; }

    /// <summary>Root element of the story.</summary>
    public ILocator Root => Page.Locator("[data-btp-root]");

    /// <summary>Creates the play-function context.</summary>
    public StoryContext CreateStoryContext(Results.StepRecorder? recorder = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        new(Cell.Story, Driver, timeout ?? BtpSettings.Current.DefaultTimeout, recorder, cancellationToken);

    /// <summary>Stops tracing and saves the trace (no-op when tracing is off).</summary>
    public async Task<string?> SaveTraceAsync(string? path)
    {
        if (!_tracing || _traceStopped)
        {
            return null;
        }

        _traceStopped = true;
        await Context.Tracing.StopAsync(new TracingStopOptions { Path = path }).ConfigureAwait(false);
        return path;
    }

    /// <summary>Waits for the in-frame play function (see <see cref="StoryPageOptions.AutoPlay"/>) and returns its outcome.</summary>
    public async Task<InAppPlayResult> WaitForInAppPlayAsync(TimeSpan? timeout = null)
    {
        await Page.WaitForSelectorAsync("[data-btp-root][data-btp-play-status]", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = (float)(timeout ?? TimeSpan.FromSeconds(60)).TotalMilliseconds,
        }).ConfigureAwait(false);
        var json = await Page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.__btpPlayResult").ConfigureAwait(false);
        return new InAppPlayResult(
            json.GetProperty("status").GetString()!,
            json.TryGetProperty("error", out var error) && error.ValueKind == System.Text.Json.JsonValueKind.String ? error.GetString() : null,
            json.TryGetProperty("renderer", out var renderer) ? renderer.GetString() : null,
            json.TryGetProperty("steps", out var steps) ? steps.GetRawText() : "[]");
    }

    /// <summary>Navigates to the story frame and waits until it is rendered (and hydrated for interactive modes).</summary>
    public async Task NavigateAsync()
    {
        var timeout = _readyTimeout;
        var prerender = _options.Prerender;
        var url = WorkbenchUrls.Frame(Cell.Story.Id, Cell.HostingModel, Cell.Theme.Name, Cell.Viewport.Name, prerender, _options.AutoPlay, _options.Args).TrimStart('/');
        var ms = (float)timeout.TotalMilliseconds;
        // Commit (not Load): streamed responses of stories with never-completing work stay open by design.
        await Page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = ms }).ConfigureAwait(false);

        try
        {
            await Page.WaitForSelectorAsync("[data-btp-root][data-btp-status=ready], [data-btp-root][data-btp-status=error], [data-btp-error]", new PageWaitForSelectorOptions { Timeout = ms, State = WaitForSelectorState.Attached }).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new StoryAssertionException($"Story frame {url} did not render within {timeout.TotalSeconds:0} s. Is the workbench registered (AddBlazorTestWorkbench + AddAdditionalAssemblies)?", ex);
        }

        var error = await Page.Locator("[data-btp-error]").AllInnerTextsAsync().ConfigureAwait(false);
        if (error.Count > 0)
        {
            throw new StoryAssertionException($"Story '{Cell.Story.Id}' failed to render under {Cell.HostingModel.ToToken()}: {string.Join(Environment.NewLine, error)}");
        }

        UnsupportedOverrides = await Page.Locator("[data-btp-root]").First.GetAttributeAsync("data-btp-unsupported-overrides").ConfigureAwait(false);

        if (Cell.HostingModel.IsInteractive())
        {
            try
            {
                await Page.WaitForSelectorAsync("[data-btp-root][data-btp-interactive=true]", new PageWaitForSelectorOptions { Timeout = ms, State = WaitForSelectorState.Attached }).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                var renderer = await Root.GetAttributeAsync("data-btp-renderer").ConfigureAwait(false);
                throw new StoryAssertionException(
                    $"Story '{Cell.Story.Id}' never became interactive under {Cell.HostingModel.ToToken()} (renderer: {renderer ?? "unknown"}) within {timeout.TotalSeconds:0} s. " +
                    (string.Equals(renderer, "Static", StringComparison.OrdinalIgnoreCase) && prerender
                        ? "Prerendering never completed, so Blazor could not hydrate: if the story's initial work never finishes (pending load, authorizing user), call .Prerender(false) on it. "
                        : string.Empty) +
                    "Check that the render mode is enabled on the host (AddInteractiveServerRenderMode / AddInteractiveWebAssemblyRenderMode) and that the stories assembly is loaded by the client.",
                    ex);
            }

            var actualRenderer = await Root.GetAttributeAsync("data-btp-renderer").ConfigureAwait(false);
            var expected = Cell.HostingModel switch
            {
                HostingModel.InteractiveServer => "Server",
                HostingModel.InteractiveWebAssembly => "WebAssembly",
                _ => null,
            };

            if (expected is not null && !string.Equals(actualRenderer, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new StoryAssertionException($"Story '{Cell.Story.Id}' requested {Cell.HostingModel.ToToken()} but rendered with '{actualRenderer}'.");
            }
        }

        await Page.EvaluateAsync(Scripts.WaitForAssets).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_tracing && !_traceStopped)
        {
            _traceStopped = true;
            await Context.Tracing.StopAsync().ConfigureAwait(false);
        }

        await Context.CloseAsync().ConfigureAwait(false);
    }
}

/// <summary>Outcome of a play function run inside the frame by the workbench's in-app driver.</summary>
/// <param name="Status"><c>passed</c>, <c>failed</c> or <c>skipped</c>.</param>
/// <param name="Error">Failure or skip reason.</param>
/// <param name="Renderer">Renderer that ran it (<c>Server</c>, <c>WebAssembly</c>).</param>
/// <param name="StepsJson">Timeline as JSON.</param>
public sealed record InAppPlayResult(string Status, string? Error, string? Renderer, string StepsJson);
