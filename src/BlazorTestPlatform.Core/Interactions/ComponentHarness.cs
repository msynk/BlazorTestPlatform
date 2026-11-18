using System.Text.RegularExpressions;

namespace BlazorTestPlatform;

/// <summary>
/// A typed test API for one component, as Angular CDK's component harnesses: a design system ships a harness next to each
/// component (<c>DialogHarness.ConfirmAsync()</c>, <c>DatePickerHarness.PickAsync(date)</c>) and every consumer's tests use
/// it instead of the component's markup, so markup can change without breaking them. Harnesses are built on locators, so
/// one harness works in bUnit, Playwright and the in-app driver.
/// </summary>
/// <example>
/// <code>
/// public sealed class DialogHarness : ComponentHarness
/// {
///     protected override By HostQuery => By.Role("dialog");
///     public Task ConfirmAsync() => Host.Locate(By.Css(".c-dialog__actions button")).Last.ClickAsync();
/// }
///
/// var dialog = ctx.Harness&lt;DialogHarness&gt;();
/// await dialog.ConfirmAsync();
/// </code>
/// </example>
public abstract class ComponentHarness
{
    private StoryContext? _context;
    private ElementLocator? _host;

    /// <summary>How an instance of the component is found: its root element (<c>By.Role("dialog")</c>, <c>By.Css(".c-tabs")</c>).</summary>
    protected internal abstract By HostQuery { get; }

    /// <summary>The component's root element.</summary>
    public ElementLocator Host => _host ?? throw new InvalidOperationException($"{GetType().Name} is not attached: create it with ctx.Harness<{GetType().Name}>().");

    /// <summary>The story the harness drives (for assertions: <c>Context.Expect(Host)...</c>).</summary>
    protected StoryContext Context => _context ?? throw new InvalidOperationException($"{GetType().Name} is not attached: create it with ctx.Harness<{GetType().Name}>().");

    /// <summary>Elements inside the component.</summary>
    protected ElementLocator Locate(By query) => Host.Locate(query);

    /// <summary>Elements inside the component by role and name.</summary>
    protected ElementLocator GetByRole(string role, string? name = null, bool exact = false) => Host.GetByRole(role, name, exact);

    /// <summary>Elements inside the component by text.</summary>
    protected ElementLocator GetByText(string text, bool exact = false) => Host.GetByText(text, exact);

    /// <summary>Elements inside the component by text pattern.</summary>
    protected ElementLocator GetByText(Regex pattern) => Host.GetByText(pattern);

    /// <summary>A harness of another component inside this one (harnesses compose: a form harness hands out field harnesses).</summary>
    protected T Harness<T>(int? nth = null)
        where T : ComponentHarness, new() => Context.Harness<T>(Host, nth);

    /// <summary>Whether the component is on screen now (no waiting).</summary>
    public async Task<bool> IsPresentAsync() => await Host.CountAsync().ConfigureAwait(false) > 0;

    internal void Attach(StoryContext context, ElementLocator host)
    {
        _context = context;
        _host = host;
    }
}

/// <summary>Finds component harnesses in a story.</summary>
public static class ComponentHarnessExtensions
{
    /// <summary>
    /// A harness for the component <typeparamref name="T"/> describes, inside <paramref name="within"/> (default: the
    /// whole story), the <paramref name="nth"/> one when there are several. Lookups are lazy, like locators: actions wait
    /// for the component and fail when the match is not unique.
    /// </summary>
    public static T Harness<T>(this StoryContext context, ElementLocator? within = null, int? nth = null)
        where T : ComponentHarness, new()
    {
        ArgumentNullException.ThrowIfNull(context);
        var harness = new T();
        var host = within is null ? context.Get(harness.HostQuery) : within.Locate(harness.HostQuery);
        harness.Attach(context, nth is { } index ? host.Nth(index) : host);
        return harness;
    }

    /// <summary>One harness per instance of the component on screen now (no waiting), in document order.</summary>
    public static async Task<IReadOnlyList<T>> HarnessesAsync<T>(this StoryContext context, ElementLocator? within = null)
        where T : ComponentHarness, new()
    {
        ArgumentNullException.ThrowIfNull(context);
        var probe = new T();
        var all = within is null ? context.Get(probe.HostQuery) : within.Locate(probe.HostQuery);
        var count = await all.CountAsync().ConfigureAwait(false);
        return [.. Enumerable.Range(0, count).Select(i => context.Harness<T>(within, i))];
    }
}
