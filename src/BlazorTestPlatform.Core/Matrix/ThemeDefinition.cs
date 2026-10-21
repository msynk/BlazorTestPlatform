namespace BlazorTestPlatform;

/// <summary>Preferred color scheme reported to the page (<c>prefers-color-scheme</c>).</summary>
public enum ColorScheme
{
    /// <summary>Light scheme.</summary>
    Light,

    /// <summary>Dark scheme.</summary>
    Dark,

    /// <summary>No preference.</summary>
    NoPreference,
}

/// <summary>
/// Describes how a theme is applied to a story. A theme can emulate <c>prefers-color-scheme</c>,
/// set attributes/classes on the document root and wrap the story in a provider component
/// (for example a design-system theme provider) through <see cref="Decorator"/>.
/// </summary>
public sealed record ThemeDefinition(string Name)
{
    /// <summary>Human-friendly name shown in the workbench.</summary>
    public string DisplayName { get; init; } = Name;

    /// <summary>Color scheme emulated by the browser.</summary>
    public ColorScheme ColorScheme { get; init; } = ColorScheme.Light;

    /// <summary>Attributes set on the <c>&lt;html&gt;</c> element (e.g. <c>data-theme=dark</c>).</summary>
    public IReadOnlyDictionary<string, string> DocumentAttributes { get; init; } = new Dictionary<string, string>();

    /// <summary>CSS class added to the story root element.</summary>
    public string? CssClass { get; init; }

    /// <summary>Optional decorator that wraps every story rendered with this theme.</summary>
    public StoryDecorator? Decorator { get; init; }

    /// <summary>Light theme: <c>prefers-color-scheme: light</c> and <c>data-theme="light"</c>.</summary>
    public static ThemeDefinition Light { get; } = new("light")
    {
        DisplayName = "Light",
        ColorScheme = ColorScheme.Light,
        DocumentAttributes = new Dictionary<string, string> { ["data-theme"] = "light" },
    };

    /// <summary>Dark theme: <c>prefers-color-scheme: dark</c> and <c>data-theme="dark"</c>.</summary>
    public static ThemeDefinition Dark { get; } = new("dark")
    {
        DisplayName = "Dark",
        ColorScheme = ColorScheme.Dark,
        DocumentAttributes = new Dictionary<string, string> { ["data-theme"] = "dark" },
    };

    /// <inheritdoc />
    public override string ToString() => Name;
}
