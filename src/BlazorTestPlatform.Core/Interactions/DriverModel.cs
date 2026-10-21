using System.Text;

namespace BlazorTestPlatform.Drivers;

/// <summary>What a driver can do. Play functions can branch on or require capabilities.</summary>
[Flags]
public enum DriverCapabilities
{
    /// <summary>Nothing beyond DOM queries and event dispatch.</summary>
    None = 0,

    /// <summary>A real browser engine executes the page.</summary>
    RealBrowser = 1,

    /// <summary>Layout is computed (visibility, sizes, scrolling, hit testing).</summary>
    Layout = 2,

    /// <summary>Screenshots can be captured.</summary>
    Screenshots = 4,

    /// <summary>Focus is managed by the browser (focus traps, <c>FocusAsync</c> in components).</summary>
    NativeFocus = 8,

    /// <summary>JavaScript runs (JS interop is real, not mocked).</summary>
    JavaScript = 16,

    /// <summary><see cref="TimeProvider"/> can be advanced from the play function.</summary>
    FakeTime = 32,

    /// <summary>CSS is applied (computed styles, media queries).</summary>
    Css = 64,

    /// <summary>Network traffic can be observed.</summary>
    Network = 128,
}

/// <summary>Kind of a locator step.</summary>
public enum LocatorStepKind
{
    /// <summary>Find descendants (or roots) matching a query.</summary>
    Find,

    /// <summary>Pick the n-th match (negative counts from the end).</summary>
    Nth,

    /// <summary>Keep matches whose text contains a value.</summary>
    HasText,

    /// <summary>Keep matches whose text does not contain a value.</summary>
    HasNotText,
}

/// <summary>One step of a <see cref="LocatorChain"/>.</summary>
public sealed record LocatorStep(LocatorStepKind Kind, By? Query = null, int Index = 0, string? Text = null)
{
    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        LocatorStepKind.Find => Query!.Describe(),
        LocatorStepKind.Nth => $"nth={Index}",
        LocatorStepKind.HasText => $"has-text=\"{Text}\"",
        LocatorStepKind.HasNotText => $"has-not-text=\"{Text}\"",
        _ => Kind.ToString(),
    };
}

/// <summary>An immutable locator: a sequence of queries and filters resolved lazily by a driver.</summary>
public sealed class LocatorChain : IEquatable<LocatorChain>
{
    private LocatorChain(IReadOnlyList<LocatorStep> steps) => Steps = steps;

    /// <summary>The steps.</summary>
    public IReadOnlyList<LocatorStep> Steps { get; }

    /// <summary>Starts a chain with a query.</summary>
    public static LocatorChain Start(By query) => new([new LocatorStep(LocatorStepKind.Find, query)]);

    /// <summary>Appends a step.</summary>
    public LocatorChain Append(LocatorStep step) => new([.. Steps, step]);

    /// <inheritdoc />
    public bool Equals(LocatorChain? other) => other is not null && Steps.SequenceEqual(other.Steps);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocatorChain);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var step in Steps)
        {
            if (builder.Length > 0)
            {
                builder.Append(" >> ");
            }

            builder.Append(step);
        }

        return builder.ToString();
    }
}

/// <summary>A point-in-time snapshot of an element, produced by every driver in the same shape.</summary>
public sealed record ElementInfo
{
    /// <summary>Lower-case tag name.</summary>
    public required string TagName { get; init; }

    /// <summary>Computed ARIA role (explicit or implicit), if any.</summary>
    public string? Role { get; init; }

    /// <summary>Computed accessible name.</summary>
    public string AccessibleName { get; init; } = string.Empty;

    /// <summary>Whitespace-normalized text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Current value of form controls.</summary>
    public string? Value { get; init; }

    /// <summary>Attributes.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();

    /// <summary>Visible (rendered with a box). In-memory drivers approximate using <c>hidden</c>/inline styles.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>Not disabled.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Checked state for checkboxes, radios, switches and <c>aria-checked</c>/<c>aria-pressed</c>; null when not checkable.</summary>
    public bool? IsChecked { get; init; }

    /// <summary>Has focus.</summary>
    public bool IsFocused { get; init; }

    /// <summary>Accepts text input.</summary>
    public bool IsEditable { get; init; }

    /// <summary>Truncated outer HTML for diagnostics.</summary>
    public string Html { get; init; } = string.Empty;

    /// <summary>Gets an attribute value or null.</summary>
    public string? GetAttribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

    /// <summary>True when the class attribute contains the token.</summary>
    public bool HasClass(string className) =>
        GetAttribute("class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className, StringComparer.Ordinal) == true;

    /// <inheritdoc />
    public override string ToString() => Html.Length > 0 ? Html : $"<{TagName}>";
}

/// <summary>Keyboard modifier keys.</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt / Option.</summary>
    Alt = 1,

    /// <summary>Control.</summary>
    Control = 2,

    /// <summary>Meta / Command / Windows.</summary>
    Meta = 4,

    /// <summary>Shift.</summary>
    Shift = 8,
}

/// <summary>Mouse buttons.</summary>
public enum MouseButton
{
    /// <summary>Primary.</summary>
    Left,

    /// <summary>Middle / wheel.</summary>
    Middle,

    /// <summary>Secondary.</summary>
    Right,
}

/// <summary>Element interaction kinds.</summary>
public enum ElementActionKind
{
    /// <summary>Click.</summary>
    Click,

    /// <summary>Double click.</summary>
    DoubleClick,

    /// <summary>Move the pointer over the element.</summary>
    Hover,

    /// <summary>Focus.</summary>
    Focus,

    /// <summary>Blur.</summary>
    Blur,

    /// <summary>Replace the value and fire <c>input</c>.</summary>
    Fill,

    /// <summary>Clear the value.</summary>
    Clear,

    /// <summary>Type characters one by one (key events per character).</summary>
    Type,

    /// <summary>Press a key or chord (e.g. <c>Enter</c>, <c>Shift+Tab</c>) on the element.</summary>
    Press,

    /// <summary>Check a checkbox/radio/switch.</summary>
    Check,

    /// <summary>Uncheck a checkbox/switch.</summary>
    Uncheck,

    /// <summary>Select options of a <c>select</c> by value or label.</summary>
    SelectOption,

    /// <summary>Pointer down.</summary>
    PointerDown,

    /// <summary>Pointer up.</summary>
    PointerUp,

    /// <summary>Drag to another element.</summary>
    DragTo,

    /// <summary>Scroll into view.</summary>
    ScrollIntoView,
}

/// <summary>An interaction a driver performs on a resolved element.</summary>
public sealed record ElementAction(ElementActionKind Kind)
{
    /// <summary>Text, key or chord.</summary>
    public string? Text { get; init; }

    /// <summary>Option values for <see cref="ElementActionKind.SelectOption"/>.</summary>
    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>Drop target for <see cref="ElementActionKind.DragTo"/>.</summary>
    public LocatorChain? Target { get; init; }

    /// <summary>Modifiers held during the action.</summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>Mouse button.</summary>
    public MouseButton Button { get; init; }

    /// <summary>Skip actionability checks (visible, enabled).</summary>
    public bool Force { get; init; }

    /// <summary>Human description for timelines.</summary>
    public string Describe() => Kind switch
    {
        ElementActionKind.Fill => $"fill \"{Text}\"",
        ElementActionKind.Type => $"type \"{Text}\"",
        ElementActionKind.Press => $"press {Text}",
        ElementActionKind.SelectOption => $"select {string.Join(", ", Values ?? [])}",
        ElementActionKind.DragTo => $"drag to {Target}",
        _ => Kind.ToString().ToLowerInvariant(),
    } + (Modifiers != KeyModifiers.None ? $" [{Modifiers}]" : string.Empty);
}

/// <summary>A keyboard interaction on the focused element.</summary>
/// <param name="Kind">Kind.</param>
/// <param name="Value">Key/chord for press/down/up, text for type.</param>
public sealed record KeyboardAction(KeyboardActionKind Kind, string Value);

/// <summary>Keyboard interaction kinds.</summary>
public enum KeyboardActionKind
{
    /// <summary>Press (down + up) a key or chord like <c>Shift+Tab</c>.</summary>
    Press,

    /// <summary>Type text character by character.</summary>
    Type,

    /// <summary>Key down only.</summary>
    Down,

    /// <summary>Key up only.</summary>
    Up,
}

/// <summary>
/// Executes story interactions. Implemented by the bUnit driver (in-memory) and the Playwright driver (real browsers).
/// Drivers implement primitives; locator semantics, auto-waiting assertions and timelines live in the core so
/// behaviour is identical everywhere.
/// </summary>
public interface IStoryDriver : IAsyncDisposable
{
    /// <summary>Driver name, e.g. <c>bunit</c> or <c>chromium</c>.</summary>
    string Name { get; }

    /// <summary>Capabilities.</summary>
    DriverCapabilities Capabilities { get; }

    /// <summary>Hosting model the story is rendered under.</summary>
    HostingModel HostingModel { get; }

    /// <summary>In-process services when available (bUnit), otherwise null.</summary>
    IServiceProvider? Services { get; }

    /// <summary>Resolves the locator now (no waiting). Returns every match.</summary>
    Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken cancellationToken);

    /// <summary>
    /// Performs an interaction. The driver waits (up to <paramref name="timeout"/>) for the locator to resolve to exactly
    /// one actionable element.
    /// </summary>
    Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Performs a keyboard interaction on the focused element.</summary>
    Task KeyboardAsync(KeyboardAction action, CancellationToken cancellationToken);

    /// <summary>Snapshot of the focused element, or null when the document body has focus.</summary>
    Task<ElementInfo?> GetFocusedElementAsync(CancellationToken cancellationToken);

    /// <summary>Callback invocations recorded so far.</summary>
    Task<IReadOnlyList<ActionRecord>> GetActionsAsync(CancellationToken cancellationToken);

    /// <summary>Waits until pending renders and in-flight work settle.</summary>
    Task WaitForIdleAsync(CancellationToken cancellationToken);

    /// <summary>Advances the story's fake <see cref="TimeProvider"/>. Requires <see cref="DriverCapabilities.FakeTime"/>.</summary>
    Task AdvanceTimeAsync(TimeSpan delta, CancellationToken cancellationToken);

    /// <summary>Current markup of the story root.</summary>
    Task<string> GetMarkupAsync(CancellationToken cancellationToken);

    /// <summary>Requests answered by the story's <see cref="HttpMock"/> (empty when the story mocks no HTTP).</summary>
    /// <remarks>Default implementation keeps existing drivers source compatible.</remarks>
    Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HttpRequestRecord>>([]);

    /// <summary>JavaScript-interop calls made by the story (recorded for stories that declare JS mocks).</summary>
    /// <remarks>Default implementation keeps existing drivers source compatible.</remarks>
    Task<IReadOnlyList<JSCallRecord>> GetJSCallsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JSCallRecord>>([]);
}
