namespace BlazorTestPlatform;

/// <summary>
/// A driver-agnostic element query. Prefer user-facing queries (<see cref="Role"/>, <see cref="Label"/>,
/// <see cref="Text"/>) over CSS: they mirror how people and assistive technology find elements, and they
/// resolve identically in bUnit and in real browsers.
/// </summary>
public abstract record By
{
    private protected By()
    {
    }

    /// <summary>The attribute used by <see cref="TestId"/>. Default <c>data-testid</c>.</summary>
    public static string TestIdAttribute { get; set; } = "data-testid";

    /// <summary>A CSS selector.</summary>
    public static By Css(string selector) => new CssQuery(selector);

    /// <summary>An ARIA role (explicit or implicit) with an optional accessible name.</summary>
    /// <param name="role">ARIA role, e.g. <c>button</c>, <c>textbox</c>, <c>tab</c>, <c>dialog</c>.</param>
    /// <param name="name">Accessible name to match (substring, case-insensitive unless <paramref name="exact"/>).</param>
    /// <param name="exact">Require a whole, case-sensitive name match.</param>
    public static By Role(string role, string? name = null, bool exact = false) => new RoleQuery(role, name, exact);

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
    /// <inheritdoc />
    public override string Describe() => Name is null ? $"role={RoleName}" : $"role={RoleName}[name=\"{Name}\"{(Exact ? "s" : "i")}]";
}

/// <summary>Text query.</summary>
public sealed record TextQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => Exact ? $"text=\"{Value}\"" : $"text={Value}";
}

/// <summary>Label query.</summary>
public sealed record LabelQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"label={(Exact ? $"\"{Value}\"" : Value)}";
}

/// <summary>Placeholder query.</summary>
public sealed record PlaceholderQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"placeholder={(Exact ? $"\"{Value}\"" : Value)}";
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
    public override string Describe() => $"alt={(Exact ? $"\"{Value}\"" : Value)}";
}

/// <summary>Title-attribute query.</summary>
public sealed record TitleQuery(string Value, bool Exact) : By
{
    /// <inheritdoc />
    public override string Describe() => $"title={(Exact ? $"\"{Value}\"" : Value)}";
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
    public static bool Matches(string? actual, string expected, bool exact)
    {
        var a = Normalize(actual);
        var e = Normalize(expected);
        return exact ? string.Equals(a, e, StringComparison.Ordinal) : a.Contains(e, StringComparison.OrdinalIgnoreCase);
    }
}
