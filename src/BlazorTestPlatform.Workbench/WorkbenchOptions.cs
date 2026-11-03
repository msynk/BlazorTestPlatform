using System.Reflection;

namespace BlazorTestPlatform.Workbench;

/// <summary>Configures the component workbench. Use the same configuration in the server and the WebAssembly client.</summary>
public sealed class WorkbenchOptions
{
    /// <summary>Route of the workbench UI.</summary>
    public const string RoutePrefix = "/_workbench";

    /// <summary>Route template of the story frame.</summary>
    public const string FrameRoute = RoutePrefix + "/frame";

    /// <summary>Title shown in the workbench header.</summary>
    public string Title { get; set; } = "Component workbench";

    /// <summary>Assemblies scanned for story collections.</summary>
    public List<Assembly> StoryAssemblies { get; } = [];

    /// <summary>Explicit collections (no reflection; trimming friendly).</summary>
    public List<StoryCollection> Collections { get; } = [];

    /// <summary>Themes offered in the toolbar. Default light and dark.</summary>
    public List<ThemeDefinition> Themes { get; } = [ThemeDefinition.Light, ThemeDefinition.Dark];

    /// <summary>Viewports offered in the toolbar. Default: the built-in presets.</summary>
    public List<Viewport> Viewports { get; } = [.. Viewport.Presets];

    /// <summary>Hosting models offered in the toolbar.</summary>
    public List<HostingModel> HostingModels { get; } = [HostingModel.InteractiveServer, HostingModel.InteractiveWebAssembly, HostingModel.InteractiveAuto, HostingModel.StaticServer];

    /// <summary>Hosting model used when the URL does not specify one.</summary>
    public HostingModel DefaultHostingModel { get; set; } = HostingModel.InteractiveServer;

    /// <summary>Directory of committed manual accessibility reviews (server only), shown next to each component.</summary>
    public string? ReviewsDirectory { get; set; }

    /// <summary>
    /// Whether the workbench pages (<c>/_workbench</c> and its story frames) respond. Null (the default) enables them
    /// everywhere except in a host whose environment is <c>Production</c>, unless <c>BTP_WORKBENCH_ENABLED</c> says
    /// otherwise. Story frames run play scripts, sign fake users in and replace services, so a production deployment
    /// must opt in explicitly (<c>Enabled = true</c>), preferably together with <see cref="AuthorizationPolicy"/>.
    /// Disabled pages answer 404.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Authorization policy a user must satisfy to open workbench pages (evaluated on the server against the request's
    /// user). Null: no authorization. Users that fail it get 404.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>Whether the pages respond in this host: <see cref="Enabled"/> resolved by <c>AddBlazorTestWorkbench</c>.</summary>
    public bool IsEnabled { get; internal set; } = true;

    /// <summary>
    /// Service types that stories register but that cannot apply per story in browsers, because the application consumes
    /// them once at start-up (hosted services, logger providers, startup filters, circuit handlers, HTTP handler-builder
    /// filters) or because the application registers no <c>IJSRuntime</c> to decorate. Populated by
    /// <c>AddBlazorTestWorkbench</c>; the workbench flags affected stories.
    /// </summary>
    public HashSet<Type> UnwrappableOverrides { get; } = [];

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<Type>> _unsupportedByStory = new(StringComparer.Ordinal);

    /// <summary>
    /// The service types <paramref name="story"/> registers that do not apply in browsers (see <see cref="UnwrappableOverrides"/>).
    /// Computed once per story: probing runs the story's service configuration.
    /// </summary>
    public IReadOnlyList<Type> UnsupportedOverridesOf(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (UnwrappableOverrides.Count == 0)
        {
            return [];
        }

        return _unsupportedByStory.GetOrAdd(story.Id, _ =>
        {
            var found = new List<Type>();
            if (story.JSInterop is not null && UnwrappableOverrides.Contains(typeof(Microsoft.JSInterop.IJSRuntime)))
            {
                found.Add(typeof(Microsoft.JSInterop.IJSRuntime));
            }

            if (story.HasServiceOverrides)
            {
                foreach (var hostingModel in Microsoft.Extensions.DependencyInjection.WorkbenchServiceCollectionExtensions.ProbedHostingModels)
                {
                    var probe = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                    story.ConfigureServices(probe, new FixtureContext(story, hostingModel));
                    found.AddRange(probe.Select(d => d.ServiceType).Where(UnwrappableOverrides.Contains));
                }
            }

            return found.Distinct().ToArray();
        });
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, BlazorTestPlatform.Accessibility.ComponentReview? Review)> _reviews = new(StringComparer.Ordinal);

    /// <summary>The committed manual review of a component, re-read only when its file changes (server only).</summary>
    internal BlazorTestPlatform.Accessibility.ComponentReview? LoadReview(string component)
    {
        if (ReviewsDirectory is null || OperatingSystem.IsBrowser())
        {
            return null;
        }

        var store = new BlazorTestPlatform.Accessibility.ManualReviewStore(ReviewsDirectory);
        var path = store.PathFor(component);
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_reviews.TryGetValue(path, out var cached) && cached.Stamp == stamp)
        {
            return cached.Review;
        }

        var review = stamp == DateTime.MinValue ? null : store.Load(component);
        _reviews[path] = (stamp, review);
        return review;
    }

    /// <summary>Adds the assembly declaring <typeparamref name="T"/>.</summary>
    public WorkbenchOptions AddStoriesFromAssemblyOf<T>()
    {
        if (!StoryAssemblies.Contains(typeof(T).Assembly))
        {
            StoryAssemblies.Add(typeof(T).Assembly);
        }

        return this;
    }

    /// <summary>Finds a theme by name (falls back to the first theme).</summary>
    public ThemeDefinition ResolveTheme(string? name) =>
        Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes.FirstOrDefault() ?? ThemeDefinition.Light;

    /// <summary>Finds a viewport by name (falls back to desktop).</summary>
    public Viewport ResolveViewport(string? name) =>
        Viewports.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Viewport.FindPreset(name ?? string.Empty) ?? Viewport.Desktop;

    internal StoryCatalog BuildCatalog()
    {
        var stories = new List<Story>();
        if (StoryAssemblies.Count > 0)
        {
            stories.AddRange(StoryCatalog.FromAssemblies([.. StoryAssemblies]).Stories);
        }

        if (Collections.Count > 0)
        {
            stories.AddRange(StoryCatalog.FromCollections(Collections).Stories);
        }

        return new StoryCatalog(stories);
    }
}

/// <summary>Builds workbench URLs (used by the UI and by browser test runners).</summary>
public static class WorkbenchUrls
{
    /// <summary>URL of a story frame.</summary>
    public static string Frame(string storyId, HostingModel hostingModel, string? theme = null, string? viewport = null, bool prerender = true, bool autoPlay = false) =>
        Frame(storyId, hostingModel, theme, viewport, prerender, autoPlay, args: null);

    /// <summary>URL of a story frame with arg overrides (<c>arg.Name=value</c>, see <see cref="StoryArgs"/>).</summary>
    public static string Frame(string storyId, HostingModel hostingModel, string? theme, string? viewport, bool prerender, bool autoPlay, IEnumerable<KeyValuePair<string, string?>>? args)
    {
        ArgumentException.ThrowIfNullOrEmpty(storyId);
        var query = new List<string> { "mode=" + hostingModel.ToToken() };
        if (theme is not null)
        {
            query.Add("theme=" + Uri.EscapeDataString(theme));
        }

        if (viewport is not null)
        {
            query.Add("viewport=" + Uri.EscapeDataString(viewport));
        }

        if (!prerender)
        {
            query.Add("prerender=false");
        }

        if (autoPlay)
        {
            query.Add("play=1");
        }

        AddArgs(query, args);
        return $"{WorkbenchOptions.FrameRoute}/{Uri.EscapeDataString(storyId)}?{string.Join('&', query)}";
    }

    /// <summary>URL of the workbench UI for a story.</summary>
    public static string Shell(string? storyId = null, string? theme = null, string? viewport = null, HostingModel? hostingModel = null, bool autoPlay = false) =>
        Shell(storyId, theme, viewport, hostingModel, autoPlay, args: null);

    /// <summary>URL of the workbench UI for a story with arg overrides.</summary>
    public static string Shell(string? storyId, string? theme, string? viewport, HostingModel? hostingModel, bool autoPlay, IEnumerable<KeyValuePair<string, string?>>? args)
    {
        var query = new List<string>();
        if (storyId is not null)
        {
            query.Add("story=" + Uri.EscapeDataString(storyId));
        }

        if (theme is not null)
        {
            query.Add("theme=" + Uri.EscapeDataString(theme));
        }

        if (viewport is not null)
        {
            query.Add("viewport=" + Uri.EscapeDataString(viewport));
        }

        if (hostingModel is { } model)
        {
            query.Add("mode=" + model.ToToken());
        }

        if (autoPlay)
        {
            query.Add("play=1");
        }

        AddArgs(query, args);
        return WorkbenchOptions.RoutePrefix + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
    }

    private static void AddArgs(List<string> query, IEnumerable<KeyValuePair<string, string?>>? args)
    {
        if (args is not null && StoryArgs.ToQuery(args) is { Length: > 0 } encoded)
        {
            query.Add(encoded);
        }
    }
}
