namespace BlazorTestPlatform;

/// <summary>Supported browser engines.</summary>
public static class Browsers
{
    /// <summary>Chromium (Chrome, Edge).</summary>
    public const string Chromium = "chromium";

    /// <summary>Firefox.</summary>
    public const string Firefox = "firefox";

    /// <summary>WebKit (Safari).</summary>
    public const string WebKit = "webkit";
}

/// <summary>
/// One cell of the cross-hosting matrix: a story rendered at a viewport, with a theme, under a hosting model, in a browser.
/// </summary>
public sealed record MatrixCase(Story Story, Viewport Viewport, ThemeDefinition Theme, HostingModel HostingModel, string Browser)
{
    /// <summary>
    /// Serializable key (<c>story-id|viewport|theme|mode|browser</c>) for test-framework data sources.
    /// Resolve it back with <see cref="TestMatrix.Resolve"/>.
    /// </summary>
    public string Key => string.Join('|', Story.Id, Viewport.Name, Theme.Name, HostingModel.ToToken(), Browser);

    /// <inheritdoc />
    public override string ToString() => $"{Story.Path} [{Viewport.Name}, {Theme.Name}, {HostingModel.ToToken()}, {Browser}]";
}

/// <summary>
/// The dimensions stories are multiplied across in browser runs: viewports × themes × hosting models × browsers.
/// Story-level <see cref="MatrixFilter"/>s prune cells.
/// </summary>
public sealed class TestMatrix
{
    /// <summary>Viewports (default: desktop, mobile).</summary>
    public List<Viewport> Viewports { get; } = [];

    /// <summary>Themes (default: light).</summary>
    public List<ThemeDefinition> Themes { get; } = [];

    /// <summary>Hosting models (default: interactive server).</summary>
    public List<HostingModel> HostingModels { get; } = [];

    /// <summary>Browsers (default: chromium).</summary>
    public List<string> Browsers { get; } = [];

    /// <summary>Desktop + mobile, light, interactive server, chromium.</summary>
    public static TestMatrix Default() => new TestMatrix()
        .WithViewports(Viewport.Desktop, Viewport.Mobile)
        .WithThemes(ThemeDefinition.Light)
        .WithHostingModels(HostingModel.InteractiveServer)
        .WithBrowsers(BlazorTestPlatform.Browsers.Chromium);

    /// <summary>Every hosting model on desktop/light/chromium: the cross-hosting regression matrix.</summary>
    public static TestMatrix CrossHosting() => new TestMatrix()
        .WithViewports(Viewport.Desktop)
        .WithThemes(ThemeDefinition.Light)
        .WithHostingModels(HostingModel.StaticServer, HostingModel.InteractiveServer, HostingModel.InteractiveWebAssembly, HostingModel.InteractiveAuto)
        .WithBrowsers(BlazorTestPlatform.Browsers.Chromium);

    /// <summary>Adds viewports.</summary>
    public TestMatrix WithViewports(params Viewport[] viewports)
    {
        Viewports.AddRange(viewports);
        return this;
    }

    /// <summary>Adds themes.</summary>
    public TestMatrix WithThemes(params ThemeDefinition[] themes)
    {
        Themes.AddRange(themes);
        return this;
    }

    /// <summary>Adds hosting models.</summary>
    public TestMatrix WithHostingModels(params HostingModel[] models)
    {
        HostingModels.AddRange(models);
        return this;
    }

    /// <summary>Adds browsers.</summary>
    public TestMatrix WithBrowsers(params string[] browsers)
    {
        Browsers.AddRange(browsers);
        return this;
    }

    /// <summary>Expands stories into cells, honouring each story's <see cref="MatrixFilter"/> and skipping in-memory hosting.</summary>
    public IEnumerable<MatrixCase> Expand(IEnumerable<Story> stories)
    {
        ArgumentNullException.ThrowIfNull(stories);
        var viewports = Viewports.Count > 0 ? Viewports : [Viewport.Desktop];
        var themes = Themes.Count > 0 ? Themes : [ThemeDefinition.Light];
        var models = HostingModels.Count > 0 ? HostingModels : [HostingModel.InteractiveServer];
        var browsers = Browsers.Count > 0 ? Browsers : [BlazorTestPlatform.Browsers.Chromium];

        foreach (var story in stories)
        {
            foreach (var model in models.Where(m => m != HostingModel.InMemory))
            {
                foreach (var browser in browsers)
                {
                    foreach (var viewport in viewports)
                    {
                        foreach (var theme in themes)
                        {
                            if (story.Matrix.Allows(viewport, theme, model, browser))
                            {
                                yield return new MatrixCase(story, viewport, theme, model, browser);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Expands a catalog.</summary>
    public IEnumerable<MatrixCase> Expand(StoryCatalog catalog) => Expand((catalog ?? throw new ArgumentNullException(nameof(catalog))).Stories);

    /// <summary>Resolves a <see cref="MatrixCase.Key"/> against a catalog and this matrix's themes/viewports (presets are always known).</summary>
    public MatrixCase Resolve(StoryCatalog catalog, string key)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrEmpty(key);
        var parts = key.Split('|');
        if (parts.Length != 5)
        {
            throw new FormatException($"Invalid matrix key '{key}'. Expected 'story|viewport|theme|mode|browser'.");
        }

        var story = catalog.Get(parts[0]);
        var viewport = Viewports.FirstOrDefault(v => string.Equals(v.Name, parts[1], StringComparison.OrdinalIgnoreCase))
            ?? Viewport.FindPreset(parts[1])
            ?? throw new KeyNotFoundException($"Unknown viewport '{parts[1]}'.");
        var theme = Themes.FirstOrDefault(t => string.Equals(t.Name, parts[2], StringComparison.OrdinalIgnoreCase))
            ?? (string.Equals(parts[2], ThemeDefinition.Dark.Name, StringComparison.OrdinalIgnoreCase) ? ThemeDefinition.Dark
                : string.Equals(parts[2], ThemeDefinition.Light.Name, StringComparison.OrdinalIgnoreCase) ? ThemeDefinition.Light
                : throw new KeyNotFoundException($"Unknown theme '{parts[2]}'."));
        return new MatrixCase(story, viewport, theme, HostingModelExtensions.Parse(parts[3]), parts[4]);
    }
}
