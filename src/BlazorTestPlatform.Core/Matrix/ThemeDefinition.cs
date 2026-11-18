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

/// <summary>Writing direction of a theme.</summary>
public enum TextDirection
{
    /// <summary>Left to right (<c>dir="ltr"</c>).</summary>
    LeftToRight,

    /// <summary>Right to left (<c>dir="rtl"</c>), for Arabic, Hebrew, Persian and other RTL languages.</summary>
    RightToLeft,
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

    /// <summary>
    /// Writing direction. Right-to-left themes render the story inside <c>&lt;div dir="rtl"&gt;</c> in every driver (and set
    /// <c>dir="rtl"</c> on the story frame's document), so layout mirroring and logical CSS properties are tested.
    /// </summary>
    public TextDirection Direction { get; init; } = TextDirection.LeftToRight;

    /// <summary>
    /// Emulates forced colors (Windows High Contrast): <c>forced-colors: active</c> in browsers, and in bUnit's media
    /// queries. Combine with <see cref="MoreContrast"/> to test high-contrast themes.
    /// </summary>
    public bool ForcedColors { get; init; }

    /// <summary>Emulates <c>prefers-contrast: more</c> in browsers and in bUnit's media queries.</summary>
    public bool MoreContrast { get; init; }

    /// <summary>
    /// The theme a workbench frame renders (its name there): <see cref="Name"/>, except for modes derived with
    /// <see cref="WithCulture"/> and <see cref="WithTimeZone"/>, which look like the theme they come from.
    /// </summary>
    public string FrameTheme { get => field ?? Name; init; }

    /// <summary>
    /// The culture stories render in under this theme (a matrix "mode", as Chromatic's modes): bUnit's current culture and
    /// the browser locale. A story's own <c>Culture(...)</c> wins. Build one with <see cref="WithCulture"/>.
    /// </summary>
    public string? Culture { get; init; }

    /// <summary>
    /// The IANA time zone stories render in under this theme: the browser's zone (JavaScript and WebAssembly's
    /// <c>TimeZoneInfo.Local</c> follow it) and bUnit's <see cref="System.TimeProvider"/>. A story's own <c>TimeZone(...)</c>
    /// wins, and only the story's reaches the <c>TimeProvider</c> of Server-rendered frames. Build one with <see cref="WithTimeZone"/>.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// This theme in a culture, named <c>{name}-{culture}</c> (<c>light-de-de</c>) unless <paramref name="name"/> says
    /// otherwise: a matrix axis for cultures without a new key format (the theme name is part of result keys and baseline
    /// names). <c>new TestMatrix().WithThemes(ThemeDefinition.Light, ThemeDefinition.Light.WithCulture("de-DE"))</c>.
    /// </summary>
    public ThemeDefinition WithCulture(string culture, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        var normalized = System.Globalization.CultureInfo.GetCultureInfo(culture, predefinedOnly: true).Name;
        return this with
        {
            Name = name ?? StoryId.Slugify($"{Name}-{normalized}"),
            DisplayName = $"{DisplayName} ({normalized})",
            FrameTheme = FrameTheme,
            Culture = normalized,
        };
    }

    /// <summary>This theme in a time zone, named <c>{name}-{zone}</c> (<c>light-asia-tokyo</c>) unless <paramref name="name"/> says otherwise.</summary>
    public ThemeDefinition WithTimeZone(string timeZone, string? name = null)
    {
        var normalized = StoryTimeZone.Normalize(timeZone);
        return this with
        {
            Name = name ?? StoryId.Slugify($"{Name}-{normalized}"),
            DisplayName = $"{DisplayName} ({normalized})",
            FrameTheme = FrameTheme,
            TimeZone = normalized,
        };
    }

    /// <summary>The light theme, right to left (<c>rtl</c>).</summary>
    public static ThemeDefinition RightToLeft { get; } = new("rtl")
    {
        DisplayName = "Right to left",
        ColorScheme = ColorScheme.Light,
        Direction = TextDirection.RightToLeft,
        DocumentAttributes = new Dictionary<string, string> { ["data-theme"] = "light", ["dir"] = "rtl" },
    };

    /// <summary>Forced colors with more contrast (<c>high-contrast</c>), as Windows High Contrast users see the story.</summary>
    public static ThemeDefinition HighContrast { get; } = new("high-contrast")
    {
        DisplayName = "High contrast",
        ColorScheme = ColorScheme.Light,
        ForcedColors = true,
        MoreContrast = true,
        DocumentAttributes = new Dictionary<string, string> { ["data-theme"] = "light" },
    };

    /// <inheritdoc />
    public override string ToString() => Name;
}
