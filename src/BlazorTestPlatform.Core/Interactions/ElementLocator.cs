using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform;

/// <summary>
/// A lazy, immutable reference to elements. Actions auto-wait for a single, visible, enabled element;
/// queries (<see cref="CountAsync"/>...) read the current state without waiting.
/// </summary>
public sealed class ElementLocator
{
    private readonly StoryContext _context;

    internal ElementLocator(StoryContext context, LocatorChain chain)
    {
        _context = context;
        Chain = chain;
    }

    /// <summary>The underlying chain.</summary>
    public LocatorChain Chain { get; }

    /// <summary>Finds descendants matching a query.</summary>
    public ElementLocator Locate(By query) => new(_context, Chain.Append(new LocatorStep(LocatorStepKind.Find, query)));

    /// <summary>Finds descendants by CSS selector.</summary>
    public ElementLocator Locate(string cssSelector) => Locate(By.Css(cssSelector));

    /// <summary>Finds descendants by role.</summary>
    public ElementLocator GetByRole(string role, string? name = null, bool exact = false) => Locate(By.Role(role, name, exact));

    /// <summary>Finds descendants by text.</summary>
    public ElementLocator GetByText(string text, bool exact = false) => Locate(By.Text(text, exact));

    /// <summary>Finds descendants by label.</summary>
    public ElementLocator GetByLabel(string text, bool exact = false) => Locate(By.Label(text, exact));

    /// <summary>Finds descendants by test id.</summary>
    public ElementLocator GetByTestId(string testId) => Locate(By.TestId(testId));

    /// <summary>The n-th match (0-based; negative counts from the end).</summary>
    public ElementLocator Nth(int index) => new(_context, Chain.Append(new LocatorStep(LocatorStepKind.Nth, Index: index)));

    /// <summary>The first match.</summary>
    public ElementLocator First => Nth(0);

    /// <summary>The last match.</summary>
    public ElementLocator Last => Nth(-1);

    /// <summary>Keeps matches containing the text.</summary>
    public ElementLocator Filter(string hasText) => new(_context, Chain.Append(new LocatorStep(LocatorStepKind.HasText, Text: hasText)));

    /// <summary>Keeps matches not containing the text.</summary>
    public ElementLocator FilterNot(string hasNotText) => new(_context, Chain.Append(new LocatorStep(LocatorStepKind.HasNotText, Text: hasNotText)));

    /// <summary>Clicks the element. <paramref name="force"/> skips actionability checks (e.g. to prove a disabled control ignores clicks).</summary>
    public Task ClickAsync(KeyModifiers modifiers = KeyModifiers.None, MouseButton button = MouseButton.Left, TimeSpan? timeout = null, bool force = false) =>
        _context.PerformAsync(this, new ElementAction(ElementActionKind.Click) { Modifiers = modifiers, Button = button, Force = force }, timeout);

    /// <summary>Double-clicks the element.</summary>
    public Task DoubleClickAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.DoubleClick), timeout);

    /// <summary>Hovers the element.</summary>
    public Task HoverAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Hover), timeout);

    /// <summary>Focuses the element.</summary>
    public Task FocusAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Focus) { Force = true }, timeout);

    /// <summary>Removes focus from the element.</summary>
    public Task BlurAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Blur) { Force = true }, timeout);

    /// <summary>Replaces the value of an input/textarea/contenteditable and fires <c>input</c>.</summary>
    public Task FillAsync(string value, TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Fill) { Text = value }, timeout);

    /// <summary>Clears the value.</summary>
    public Task ClearAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Clear), timeout);

    /// <summary>Focuses and types text key by key.</summary>
    public Task TypeAsync(string text, TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Type) { Text = text }, timeout);

    /// <summary>Focuses and presses a key or chord.</summary>
    public Task PressAsync(string key, TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Press) { Text = key }, timeout);

    /// <summary>Checks a checkbox, radio or switch.</summary>
    public Task CheckAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Check), timeout);

    /// <summary>Unchecks a checkbox or switch.</summary>
    public Task UncheckAsync(TimeSpan? timeout = null) => _context.PerformAsync(this, new ElementAction(ElementActionKind.Uncheck), timeout);

    /// <summary>Selects options of a <c>select</c> by value or label.</summary>
    public Task SelectOptionAsync(params string[] values) => _context.PerformAsync(this, new ElementAction(ElementActionKind.SelectOption) { Values = values }, null);

    /// <summary>Pointer down on the element.</summary>
    public Task PointerDownAsync(MouseButton button = MouseButton.Left) => _context.PerformAsync(this, new ElementAction(ElementActionKind.PointerDown) { Button = button }, null);

    /// <summary>Pointer up on the element.</summary>
    public Task PointerUpAsync(MouseButton button = MouseButton.Left) => _context.PerformAsync(this, new ElementAction(ElementActionKind.PointerUp) { Button = button }, null);

    /// <summary>Drags the element onto another.</summary>
    public Task DragToAsync(ElementLocator target) =>
        _context.PerformAsync(this, new ElementAction(ElementActionKind.DragTo) { Target = (target ?? throw new ArgumentNullException(nameof(target))).Chain }, null);

    /// <summary>Scrolls the element into view.</summary>
    public Task ScrollIntoViewAsync() => _context.PerformAsync(this, new ElementAction(ElementActionKind.ScrollIntoView) { Force = true }, null);

    /// <summary>Current number of matches (no waiting).</summary>
    public async Task<int> CountAsync() => (await AllAsync().ConfigureAwait(false)).Count;

    /// <summary>Snapshots of all current matches (no waiting).</summary>
    public Task<IReadOnlyList<ElementInfo>> AllAsync() => _context.Driver.QueryAsync(Chain, _context.CancellationToken);

    /// <summary>Waits for exactly one match and returns its snapshot.</summary>
    public async Task<ElementInfo> InfoAsync(TimeSpan? timeout = null)
    {
        var (success, matches) = await Poll.UntilAsync(
            async ct =>
            {
                var all = await _context.Driver.QueryAsync(Chain, ct).ConfigureAwait(false);
                return (all.Count == 1, all);
            },
            timeout ?? _context.DefaultTimeout,
            _context.CancellationToken).ConfigureAwait(false);

        return success
            ? matches[0]
            : throw new ElementNotActionableException(matches.Count == 0
                ? $"No element matches {this} (waited {(timeout ?? _context.DefaultTimeout).TotalMilliseconds:0} ms)."
                : $"Strict mode violation: {this} resolved to {matches.Count} elements:{Environment.NewLine}{string.Join(Environment.NewLine, matches.Take(5).Select(m => "  " + m.Html))}");
    }

    /// <summary>Waits for one match and returns its normalized text.</summary>
    public async Task<string> TextAsync() => (await InfoAsync().ConfigureAwait(false)).Text;

    /// <summary>Waits for one match and returns an attribute.</summary>
    public async Task<string?> GetAttributeAsync(string name) => (await InfoAsync().ConfigureAwait(false)).GetAttribute(name);

    /// <summary>Waits for one match and returns its value.</summary>
    public async Task<string?> InputValueAsync() => (await InfoAsync().ConfigureAwait(false)).Value;

    /// <summary>True when exactly one match is visible now.</summary>
    public async Task<bool> IsVisibleAsync()
    {
        var all = await AllAsync().ConfigureAwait(false);
        return all.Count > 0 && all.All(e => e.IsVisible);
    }

    /// <summary>True when the single match is enabled.</summary>
    public async Task<bool> IsEnabledAsync() => (await InfoAsync().ConfigureAwait(false)).IsEnabled;

    /// <summary>True when the single match is checked.</summary>
    public async Task<bool> IsCheckedAsync() => (await InfoAsync().ConfigureAwait(false)).IsChecked == true;

    /// <summary>True when the single match has focus.</summary>
    public async Task<bool> IsFocusedAsync() => (await InfoAsync().ConfigureAwait(false)).IsFocused;

    /// <inheritdoc />
    public override string ToString() => Chain.ToString();
}
