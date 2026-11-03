namespace BlazorTestPlatform;

/// <summary>How the workbench lays a story out inside its frame.</summary>
public enum StoryLayout
{
    /// <summary>Padded block (default): the component at its natural width with 16px padding.</summary>
    Padded,

    /// <summary>Edge to edge (pages, layouts, app bars).</summary>
    Fullscreen,

    /// <summary>Centered in the viewport (buttons, badges, dialogs).</summary>
    Centered,
}

/// <summary>
/// Options that become read-only once their story is built: catalogs are cached and shared by every test in the
/// process, so a test changing a story's options would change them for all the others (in parallel, too).
/// </summary>
public abstract class StoryOptionsBase
{
    private bool _frozen;

    /// <summary>True once the story that owns the options is built.</summary>
    public bool IsReadOnly => _frozen;

    internal void Freeze() => _frozen = true;

    /// <summary>Throws when the options belong to a built story.</summary>
    protected void ThrowIfReadOnly()
    {
        if (_frozen)
        {
            throw new InvalidOperationException(
                "Story options are read-only once the story is built (catalogs are shared by every test). Configure them in the story builder, or pass run options (for example BunitStoryOptions or BrowserRunOptions) instead.");
        }
    }
}

/// <summary>Visual-regression settings for a story.</summary>
public sealed class VisualOptions : StoryOptionsBase
{
    /// <summary>When true, no screenshot is taken for this story.</summary>
    public bool Skip { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Per-pixel color-distance threshold in <c>[0, 1]</c>. Default 0.1 (same as pixelmatch).</summary>
    public double? Threshold { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Maximum number of differing pixels tolerated.</summary>
    public int? MaxDiffPixels { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Maximum ratio (0..1) of differing pixels tolerated.</summary>
    public double? MaxDiffRatio { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>CSS selectors whose boxes are masked before capture (timestamps, avatars, ads...).</summary>
    public List<string> Masks { get; } = [];

    /// <summary>Capture the full page instead of the story root element.</summary>
    public bool FullPage { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>CSS selector that must be visible before the snapshot is taken.</summary>
    public string? WaitForSelector { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Extra settle delay before the capture (prefer <see cref="WaitForSelector"/>).</summary>
    public TimeSpan Delay { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Take the snapshot after the play function ran (default) or before it.</summary>
    public bool CaptureAfterPlay { get; set { ThrowIfReadOnly(); field = value; } } = true;

    /// <summary>Extra CSS injected only for the snapshot (e.g. hide a blinking caret).</summary>
    public string? Css { get; set { ThrowIfReadOnly(); field = value; } }

    internal VisualOptions Clone()
    {
        var clone = new VisualOptions
        {
            Skip = Skip,
            Threshold = Threshold,
            MaxDiffPixels = MaxDiffPixels,
            MaxDiffRatio = MaxDiffRatio,
            FullPage = FullPage,
            WaitForSelector = WaitForSelector,
            Delay = Delay,
            CaptureAfterPlay = CaptureAfterPlay,
            Css = Css,
        };
        clone.Masks.AddRange(Masks);
        return clone;
    }
}

/// <summary>Accessibility settings for a story.</summary>
public sealed class StoryAccessibilityOptions : StoryOptionsBase
{
    /// <summary>Skips automated accessibility checks for this story.</summary>
    public bool Skip { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>Rule identifiers disabled for this story (with a reason recorded in <see cref="Justifications"/>).</summary>
    public HashSet<string> DisabledRules { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Why rules were disabled. Surfaced in reports so suppressions stay reviewable.</summary>
    public Dictionary<string, string> Justifications { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CSS selector limiting the scan (defaults to the story root).</summary>
    public string? Include { get; set { ThrowIfReadOnly(); field = value; } }

    /// <summary>CSS selectors excluded from the scan.</summary>
    public List<string> Exclude { get; } = [];

    internal StoryAccessibilityOptions Clone()
    {
        var clone = new StoryAccessibilityOptions { Skip = Skip, Include = Include };
        clone.DisabledRules.UnionWith(DisabledRules);
        foreach (var pair in Justifications)
        {
            clone.Justifications[pair.Key] = pair.Value;
        }

        clone.Exclude.AddRange(Exclude);
        return clone;
    }
}

/// <summary>Restricts which matrix cells a story runs in.</summary>
public sealed class MatrixFilter
{
    /// <summary>Only these viewport names (empty = all).</summary>
    public HashSet<string> Viewports { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Only these theme names (empty = all).</summary>
    public HashSet<string> Themes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Only these hosting models (empty = all).</summary>
    public HashSet<HostingModel> HostingModels { get; } = [];

    /// <summary>Hosting models this story must not run under (e.g. static SSR for an interactive-only widget).</summary>
    public HashSet<HostingModel> ExcludedHostingModels { get; } = [];

    /// <summary>Only these browsers (empty = all).</summary>
    public HashSet<string> Browsers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the cell is allowed by this filter.</summary>
    public bool Allows(Viewport viewport, ThemeDefinition theme, HostingModel hostingModel, string browser) =>
        (Viewports.Count == 0 || Viewports.Contains(viewport.Name))
        && (Themes.Count == 0 || Themes.Contains(theme.Name))
        && (HostingModels.Count == 0 || HostingModels.Contains(hostingModel))
        && !ExcludedHostingModels.Contains(hostingModel)
        && (Browsers.Count == 0 || Browsers.Contains(browser));

    /// <summary>True when the hosting model is allowed.</summary>
    public bool Allows(HostingModel hostingModel) =>
        (HostingModels.Count == 0 || HostingModels.Contains(hostingModel)) && !ExcludedHostingModels.Contains(hostingModel);

    internal MatrixFilter Clone()
    {
        var clone = new MatrixFilter();
        clone.Viewports.UnionWith(Viewports);
        clone.Themes.UnionWith(Themes);
        clone.HostingModels.UnionWith(HostingModels);
        clone.ExcludedHostingModels.UnionWith(ExcludedHostingModels);
        clone.Browsers.UnionWith(Browsers);
        return clone;
    }
}
