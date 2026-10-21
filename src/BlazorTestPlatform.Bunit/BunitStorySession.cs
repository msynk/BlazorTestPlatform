using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Accessibility.Dom;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorTestPlatform.Bunit;

/// <summary>Options for running stories with bUnit.</summary>
public sealed class BunitStoryOptions
{
    /// <summary>Extra configuration of the bUnit context (JS interop setups, component stubs...), applied after story services.</summary>
    public Action<BunitContext>? ConfigureContext { get; set; }

    /// <summary>JS interop mode. Default <see cref="JSRuntimeMode.Loose"/> so components calling JS still render.</summary>
    public JSRuntimeMode JSInteropMode { get; set; } = JSRuntimeMode.Loose;

    /// <summary>Execute the story's play function. Default true.</summary>
    public bool RunPlay { get; set; } = true;

    /// <summary>Run static accessibility rules after play. Default true.</summary>
    public bool CheckAccessibility { get; set; } = true;

    /// <summary>Measure render performance when the story declares a budget. Default true.</summary>
    public bool MeasurePerformance { get; set; } = true;

    /// <summary>Write a JSON result into the results directory. Default true.</summary>
    public bool WriteResults { get; set; } = true;

    /// <summary>Accessibility policy (defaults to <see cref="BtpSettings.Accessibility"/>).</summary>
    public AccessibilityPolicy? AccessibilityPolicy { get; set; }

    /// <summary>Settings (defaults to <see cref="BtpSettings.Current"/>).</summary>
    public BtpSettings? Settings { get; set; }

    /// <summary>Overall timeout (defaults to the story timeout or 30 s).</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// CSS text whose <c>display</c>/<c>visibility</c> rules decide element visibility in memory (in addition to
    /// <see cref="BtpSettings.BunitStylesheets"/>). Load your design system's CSS or CSS-isolation bundle so components
    /// hidden by a class are hidden in bUnit too.
    /// </summary>
    public List<string> Stylesheets { get; } = [];

    /// <summary>Adds a stylesheet file.</summary>
    public BunitStoryOptions AddStylesheetFile(string path)
    {
        Stylesheets.Add(File.ReadAllText(path));
        return this;
    }

    /// <summary>
    /// Viewport the in-memory driver pretends to have: stylesheet <c>@media</c> queries are evaluated against it and
    /// it is exposed as <see cref="StoryRenderContext.Viewport"/>. Defaults to <see cref="BtpSettings.BunitViewport"/>
    /// (<c>btp.json</c> → <c>bunit.viewport</c>), then <see cref="Viewport.Desktop"/>.
    /// </summary>
    public Viewport? Viewport { get; set; }

    /// <summary>
    /// Theme applied to the story: its decorator wraps the story, and its color scheme drives
    /// <c>prefers-color-scheme</c> media queries. Default: none (light).
    /// </summary>
    public ThemeDefinition? Theme { get; set; }

    /// <summary>
    /// Arg overrides as control text (<c>["Variant"] = "Danger"</c>), converted exactly like the workbench's controls
    /// and <c>?arg.Name=value</c> links (see <see cref="StoryArgs"/>). Invalid values throw <see cref="ArgumentException"/>.
    /// </summary>
    public IDictionary<string, string?> Args { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Measures the initial render's allocations and the wall-clock time of render + play, excluding harness setup.
/// Allocations stop at the end of the first render because play-script assertions (DOM queries) would dwarf the
/// component's own allocations; the cost of interactions is tracked through render counts and time.
/// Allocations are counted on the rendering thread only: bUnit renders synchronously on the caller's thread, and a
/// process-wide counter would also count whatever runs in parallel (other tests, an in-process web host, browsers).
/// </summary>
internal sealed class RenderMeasurement
{
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private long _allocatedBefore;
    private long? _renderAllocations;
    private int _thread;

    public void Start()
    {
        _thread = Environment.CurrentManagedThreadId;
        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        _clock.Restart();
    }

    public void Rendered()
    {
        if (_renderAllocations is null)
        {
            if (Environment.CurrentManagedThreadId != _thread)
            {
                throw new InvalidOperationException("Render allocations must be measured on the thread that started the render.");
            }

            _renderAllocations = GC.GetAllocatedBytesForCurrentThread() - _allocatedBefore;
        }
    }

    public (double Milliseconds, long AllocatedBytes) Stop()
    {
        _clock.Stop();
        Rendered();
        return (_clock.Elapsed.TotalMilliseconds, _renderAllocations!.Value);
    }
}

/// <summary>
/// A story rendered with bUnit. Gives direct access to the bUnit context and rendered tree for custom assertions,
/// on top of the driver-agnostic <see cref="StoryContext"/>.
/// </summary>
public sealed class BunitStorySession : IAsyncDisposable
{
    private BunitStorySession(Story story, BunitContext context, IRenderedComponent<StoryRoot> root, StoryRenderContext renderContext, BunitStoryDriver driver, StoryContext storyContext)
    {
        Story = story;
        Context = context;
        Root = root;
        RenderContext = renderContext;
        Driver = driver;
        StoryContext = storyContext;
    }

    /// <summary>The story.</summary>
    public Story Story { get; }

    /// <summary>The bUnit context (services, JS interop...).</summary>
    public BunitContext Context { get; }

    /// <summary>The rendered story root.</summary>
    public IRenderedComponent<StoryRoot> Root { get; }

    /// <summary>The render context (actions, bound values...).</summary>
    public StoryRenderContext RenderContext { get; }

    /// <summary>The in-memory driver.</summary>
    public BunitStoryDriver Driver { get; }

    /// <summary>The play-function API (also usable directly from tests).</summary>
    public StoryContext StoryContext { get; }

    /// <summary>Current markup.</summary>
    public string Markup => Root.Markup;

    /// <summary>Recorded actions.</summary>
    public ActionLog Actions => RenderContext.Actions;

    /// <summary>Renders a story.</summary>
    public static Task<BunitStorySession> StartAsync(Story story, BunitStoryOptions? options = null, StepRecorder? recorder = null, CancellationToken cancellationToken = default) =>
        StartAsync(story, options, recorder, measurement: null, cancellationToken);

    internal static async Task<BunitStorySession> StartAsync(Story story, BunitStoryOptions? options, StepRecorder? recorder, RenderMeasurement? measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(story);
        options ??= new BunitStoryOptions();
        var settings = options.Settings ?? BtpSettings.Current;
        var context = new BunitContext();
        try
        {
            context.JSInterop.Mode = options.JSInteropMode;
            if (story.JSInterop is { } jsMocks)
            {
                JSInteropMockAdapter.Apply(context.JSInterop, jsMocks);

                // bUnit answers the mocks; the decorator records calls in order (with their module) for ExpectJSCall.
                var jsCalls = new JSCallLog();
                context.Services.AddSingleton(jsCalls);
                context.Services.AddSingleton(StoryJSRuntime.Create(context.JSInterop.JSRuntime, jsMocks, jsCalls, JSMockMode.AnsweredByInner));
            }

            var viewport = options.Viewport ?? settings.BunitViewport ?? Viewport.Desktop;
            var args = StoryArgs.Resolve(story, options.Args.AsReadOnly(), fromLink: false);
            if (args.Errors.Count > 0)
            {
                throw new ArgumentException($"Invalid args for story '{story.Id}': {string.Join(" ", args.Errors)}", nameof(options));
            }

            var renderContext = new StoryRenderContext(story, context.Services, HostingModel.InMemory) { Viewport = viewport, Theme = options.Theme, ArgOverrides = args.Values };
            var fixtureContext = new FixtureContext(story, HostingModel.InMemory);

            if (renderContext.Authentication is { } authentication)
            {
                // Replace bUnit's authorization placeholders with the real authorization services so roles,
                // claims and policies evaluate against the fake user's ClaimsPrincipal.
                context.Services.RemoveAll<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
                context.Services.RemoveAll<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();
                context.Services.RemoveAll<AuthenticationStateProvider>();
                context.Services.AddAuthorizationCore();
                context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
                context.Services.AddSingleton(authentication);
            }

            story.ConfigureServices(context.Services, fixtureContext);
            options.ConfigureContext?.Invoke(context);
            await story.InitializeFixturesAsync(context.Services, fixtureContext).ConfigureAwait(false);

            // Build the service provider and renderer up front so measurements only see the component's own work.
            _ = context.Renderer;
            IRenderedComponent<StoryRoot>? root = null;
            var recorderToUse = recorder ?? new StepRecorder();
            await recorderToUse.RunAsync($"render {story.Id}", StepKind.Render, () =>
            {
                measurement?.Start();
                root = context.Render<StoryRoot>(p => p.Add(r => r.Context, renderContext));
                measurement?.Rendered();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            var driver = new BunitStoryDriver(context, root!, renderContext, StylesheetCache.Resolve(options.Stylesheets, settings.BunitStylesheets, MediaEnvironment.For(viewport, options.Theme)));
            var storyContext = new StoryContext(story, driver, settings.DefaultTimeout, recorderToUse, cancellationToken);
            return new BunitStorySession(story, context, root!, renderContext, driver, storyContext);
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Runs the story's play function (no-op when it has none).</summary>
    public async Task PlayAsync()
    {
        if (Story.Play is { } play)
        {
            await StoryContext.Recorder.RunAsync("play", StepKind.Step, () => play(StoryContext)).ConfigureAwait(false);
            await Driver.WaitForIdleAsync(StoryContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs static accessibility rules on the current markup.</summary>
    public AccessibilityReport AnalyzeAccessibility(AccessibilityPolicy? policy = null) =>
        new MarkupAccessibilityAnalyzer().Analyze(Driver.Scope(), policy ?? BtpSettings.Current.Accessibility, Story.Accessibility);

    /// <summary>Throws <see cref="AccessibilityViolationException"/> when the policy fails.</summary>
    public void AssertAccessible(AccessibilityPolicy? policy = null)
    {
        policy ??= BtpSettings.Current.Accessibility;
        var evaluation = policy.Evaluate(AnalyzeAccessibility(policy), Story.Id, Story.Accessibility);
        if (!evaluation.Passed)
        {
            throw new AccessibilityViolationException(evaluation, Story.Id);
        }
    }

    /// <summary>Render counts of the current tree (no timing/allocation data).</summary>
    public RenderMetrics CaptureRenderCounts()
    {
        var components = Root.FindComponents<IComponent>();
        var byType = new Dictionary<string, int>(StringComparer.Ordinal);
        var rootRenders = 0;
        var total = 0;
        foreach (var component in components)
        {
            var type = component.Instance.GetType();
            total += component.RenderCount;
            byType[type.FullName ?? type.Name] = byType.GetValueOrDefault(type.FullName ?? type.Name) + component.RenderCount;
            if (rootRenders == 0 && type == Story.ComponentType)
            {
                rootRenders = component.RenderCount;
            }
        }

        return new RenderMetrics
        {
            RootRenderCount = rootRenders,
            TotalRenderCount = total,
            ComponentCount = components.Count,
            RendersByComponent = byType,
            Iterations = 1,
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Driver.DisposeAsync().ConfigureAwait(false);
        await Context.DisposeAsync().ConfigureAwait(false);
    }
}
