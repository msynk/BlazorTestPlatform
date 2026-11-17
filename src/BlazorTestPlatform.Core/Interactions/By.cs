using System.Text.RegularExpressions;

namespace BlazorTestPlatform;

/// <summary>
/// A driver-agnostic element query. Prefer user-facing queries (<see cref="Role"/>, <see cref="Label(string, bool)"/>,
/// <see cref="Text(string, bool)"/>) over CSS: they mirror how people and assistive technology find elements, and they
/// resolve identically in bUnit and in real browsers.
/// </summary>
public abstract record By
{
    private protected By()
    {
    }

    /// <summary>
    /// A regular expression that replaces the query's text (<see cref="Text(Regex)"/>, the <c>namePattern</c> of <see cref="Role"/>...):
    /// it is tested against the normalized text. Browser drivers run it as a JavaScript <c>RegExp</c> (with the
    /// <c>i</c>, <c>m</c> and <c>s</c> flags from <see cref="RegexOptions"/>), so keep to syntax both engines share.
    /// </summary>
    public Regex? Pattern { get; init; }

    /// <summary>The attribute used by <see cref="TestId"/>. Default <c>data-testid</c>.</summary>
    public static string TestIdAttribute { get; set; } = "data-testid";

    /// <summary>A CSS selector.</summary>
    public static By Css(string selector) => new CssQuery(selector);

    /// <summary>An ARIA role (explicit or implicit) with an optional accessible name.</summary>
    /// <param name="role">ARIA role, e.g. <c>button</c>, <c>textbox</c>, <c>tab</c>, <c>dialog</c>.</param>
    /// <param name="name">Accessible name to match (substring, case-insensitive unless <paramref name="exact"/>).</param>
    /// <param name="exact">Require a whole, case-sensitive name match.</param>
    /// <param name="namePattern">Accessible name pattern (instead of <paramref name="name"/>).</param>
    /// <param name="isChecked">Only elements in this checked state (checkboxes, radios, switches, checkable menu items, options, tree items; not <c>mixed</c>).</param>
    /// <param name="pressed">Only toggle buttons in this pressed state (<c>aria-pressed</c>).</param>
    /// <param name="expanded">Only elements in this expanded state (<c>aria-expanded</c>).</param>
    /// <param name="selected">Only elements in this selected state (<c>aria-selected</c>, or <c>selected</c> on options).</param>
    /// <param name="disabled">Only elements that are (or are not) disabled.</param>
    /// <param name="level">Only headings of this level (or other roles with this <c>aria-level</c>).</param>
    /// <param name="includeHidden">Also match elements excluded from the accessibility tree.</param>
    /// <example><c>By.Role("button", "Bold", pressed: true)</c>, <c>By.Role("heading", level: 2)</c>, <c>By.Role("row", namePattern: new Regex("^Order \\d+"))</c>.</example>
    public static By Role(string role, string? name = null, bool exact = false, System.Text.RegularExpressions.Regex? namePattern = null, bool? isChecked = null, bool? pressed = null, bool? expanded = null, bool? selected = null, bool? disabled = null, int? level = null, bool includeHidden = false)
    {
        if (name is not null && namePattern is not null)
        {
            throw new ArgumentException("Pass a name or a name pattern, not both.", nameof(namePattern));
        }

        return new RoleQuery(role, namePattern?.ToString() ?? name, exact)
        {
            Pattern = namePattern,
            Checked = isChecked,
            Pressed = pressed,
            Expanded = expanded,
            Selected = selected,
            Disabled = disabled,
            Level = level,
            IncludeHidden = includeHidden,
        };
    }

    /// <summary>Elements whose own text matches <paramref name="pattern"/> (innermost match).</summary>
    public static By Text(Regex pattern) => new TextQuery(Source(pattern), false) { Pattern = pattern };

    /// <summary>Form controls whose label matches <paramref name="pattern"/>.</summary>
    public static By Label(Regex pattern) => new LabelQuery(Source(pattern), false) { Pattern = pattern };

    /// <summary>Inputs whose placeholder matches <paramref name="pattern"/>.</summary>
    public static By Placeholder(Regex pattern) => new PlaceholderQuery(Source(pattern), false) { Pattern = pattern };

    /// <summary>Images (and areas/inputs) whose alt text matches <paramref name="pattern"/>.</summary>
    public static By AltText(Regex pattern) => new AltTextQuery(Source(pattern), false) { Pattern = pattern };

    /// <summary>Elements whose title attribute matches <paramref name="pattern"/>.</summary>
    public static By Title(Regex pattern) => new TitleQuery(Source(pattern), false) { Pattern = pattern };

    /// <summary>Elements whose own text matches (innermost match).</summary>
    public static By Text(string text, bool exact = false) => new TextQuery(text, exact);

    /// <summary>Form controls labelled by the text (<c>label</c>, <c>aria-label</c>, <c>aria-labelledby</c>).</summary>
    public static By Label(string text, bool exact = false) => new LabelQuery(text, exact);

    /// <summary>Inputs by placeholder.</summary>
    public static By Placeholder(string text, bool exact = false) => new PlaceholderQuery(text, exact);

    /// <summary>Elements by test id attribute (<see cref="TestIdAttribute"/>).</summary>
    public static By TestId(string testId) => new TestIdQuery(testId);

    /// <summary>Images (and areas/inputs) by alt text.</summary>
    public static By AltText(string text, bool exact = false) => new AltTextQuery(text, exact);

    /// <summary>Elements by title attribute.</summary>
    public static By Title(string text, bool exact = false) => new TitleQuery(text, exact);

    /// <summary>Playwright-style description, e.g. <c>role=button[name="Save"]</c>.</summary>
    public abstract string Describe();

    /// <summary>How the query's text reads in descriptions: <c>/pattern/i</c>, <c>"exact"</c> or the plain substring.</summary>
    private protected string Shown(string value, bool exact) =>
        Pattern is not null ? $"/{Pattern}/{TextMatch.Flags(Pattern)}" : exact ? $"\"{value}\"" : value;

    private static string Source(Regex pattern) => (pattern ?? throw new ArgumentNullException(nameof(pattern))).ToString();

    /// <inheritdoc />
    public sealed override string ToString() => Describe();
}

/// <summary>CSS query.</summary>
public sealed record CssQuery(string Selector) : By
{
    /// <inheritdoc />
    public override string Describe() => $"css={Selector}";
}

/// <summary>Role query.</summary>
public sealed record RoleQuery(string RoleName, string? Name, bool Exact) : By
{
    /// <summary>Only elements whose checked state (<c>aria-checked</c> or native) is this.</summary>
    public bool? Checked { get; init; }

    /// <summary>Only elements whose <c>aria-pressed</c> is this.</summary>
    public bool? Pressed { get; init; }

    /// <summary>Only elements whose <c>aria-expanded</c> is this.</summary>
    public bool? Expanded { get; init; }

    /// <summary>Only elements whose <c>aria-selected</c> (or <c>option[selected]</c>) is this.</summary>
    public bool? Selected { get; init; }

    /// <summary>Only elements that are (or are not) disabled.</summary>
    public bool? Disabled { get; init; }

    /// <summary>Only headings of this level (or elements with this <c>aria-level</c>).</summary>
    public int? Level { get; init; }

    /// <summary>Also match elements excluded from the accessibility tree (hidden).</summary>
    public bool IncludeHidden { get; init; }

    /// <inheritdoc />
    public override string Describe()
    {
        var filters = new List<string>();
        if (Pattern is not null)
        {
            filters.Add($"name={Shown(Name!, Exact)}");
        }
        else if (Name is not null)
        {
            filters.Add($"name=\"{Name}\"{(Exact ? "s" : "i")}");
        }

        void State(string name, bool? value)
        {
            if (value is { } set)
            {
                filters.Add($"{name}={(set ? "true" : "false")}");
            }
        }

        State("checked", Checked);
        State("pressed", Pressed);
        State("expanded", Expanded);
        State("selected", Selected);
        State("disabled", Disabled);
        if (Level is { } level)
        {
            filters.Add($"level={level}");
        }

        if (IncludeHidden)
        {
            filters.Add("include-hidden");
        }

        return $"role={RoleName}{string.Concat(filters.Select(f => $"[{f}]"))}";
    }
}


/// <summary>Text query.</summary>
public sealed record TextQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"text={Shown(Value, Exact)}";
}

/// <summary>Label query.</summary>
public sealed record LabelQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"label={Shown(Value, Exact)}";
}

/// <summary>Placeholder query.</summary>
public sealed record PlaceholderQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"placeholder={Shown(Value, Exact)}";
}

/// <summary>Test-id query.</summary>
public sealed record TestIdQuery(string Value) : By
{
    /// <inheritdoc />
    public override string Describe() => $"testid={Value}";
}

/// <summary>Alt-text query.</summary>
public sealed record AltTextQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"alt={Shown(Value, Exact)}";
}

/// <summary>Title-attribute query.</summary>
public sealed record TitleQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"title={Shown(Value, Exact)}";
}

/// <summary>Text-matching semantics shared by every driver (mirrors Playwright).</summary>
public static class TextMatch
{
    /// <summary>Collapses whitespace runs to a single space and trims.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == '​')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Non-exact: case-insensitive substring after whitespace normalization.
    /// Exact: case-sensitive full match after whitespace normalization.
    /// </summary>
    /// <summary>
    /// Matches <paramref name="actual"/> against <paramref name="pattern"/> (on the normalized text) when it is set, else
    /// against <paramref name="expected"/> as <see cref="Matches(string?, string, bool)"/>.
    /// </summary>
    public static bool Matches(string? actual, string expected, bool exact, Regex? pattern) =>
        pattern is null ? Matches(actual, expected, exact) : pattern.IsMatch(Normalize(actual));

    /// <summary>The JavaScript flags equivalent to <paramref name="pattern"/>'s options (<c>i</c>, <c>m</c>, <c>s</c>).</summary>
    public static string Flags(Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var options = pattern.Options;
        return (options.HasFlag(RegexOptions.IgnoreCase) ? "i" : string.Empty)
            + (options.HasFlag(RegexOptions.Multiline) ? "m" : string.Empty)
            + (options.HasFlag(RegexOptions.Singleline) ? "s" : string.Empty);
    }

    /// <summary>Text match (<paramref name="exact"/>: whole and case-sensitive; else case-insensitive substring), on normalized text.</summary>
    public static bool Matches(string? actual, string expected, bool exact)
    {
        var a = Normalize(actual);
        var e = Normalize(expected);
        return exact ? string.Equals(a, e, StringComparison.Ordinal) : a.Contains(e, StringComparison.OrdinalIgnoreCase);
    }
}
