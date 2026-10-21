using System.Diagnostics;
using System.Globalization;
using System.Text;
using BlazorTestPlatform.Drivers;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Playwright;

/// <summary>
/// <see cref="IStoryDriver"/> for real browsers. Locator chains resolve in the page with the workbench's locator engine,
/// the one the in-app driver uses, so roles, accessible names, text, strictness and the story scope match the bUnit
/// driver. Actions then go through Playwright on the resolved element, with real input events and Playwright's
/// actionability checks, and every interaction settles through the story frame's bridge so Server circuits and
/// WebAssembly runtimes have applied their render batches before the next step.
/// </summary>
public sealed class PlaywrightStoryDriver : IStoryDriver
{
    private const string RootSelector = "[data-btp-root]";

    // The workbench renders rejected arg overrides inside the story root; they are not part of the story.
    private const string StorySelector = ":not([data-btp-arg-errors], [data-btp-arg-errors] *)";

    // Playwright treats a timeout of 0 as "no timeout": an action resolved at the deadline still gets a short chance.
    private static readonly TimeSpan MinimumActionTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Creates a driver for a page showing a story frame.</summary>
    public PlaywrightStoryDriver(IPage page, string browserName, HostingModel hostingModel, bool fakeTime)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
        Name = browserName;
        HostingModel = hostingModel;
        Capabilities = DriverCapabilities.RealBrowser | DriverCapabilities.Layout | DriverCapabilities.Screenshots
            | DriverCapabilities.NativeFocus | DriverCapabilities.JavaScript | DriverCapabilities.Css | DriverCapabilities.Network
            | (fakeTime && hostingModel.IsInteractive() ? DriverCapabilities.FakeTime : DriverCapabilities.None);
    }

    /// <summary>The Playwright page.</summary>
    public IPage Page { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DriverCapabilities Capabilities { get; }

    /// <inheritdoc />
    public HostingModel HostingModel { get; }

    /// <inheritdoc />
    public IServiceProvider? Services => null;

    /// <summary>
    /// Converts a locator chain to a Playwright locator inside the story root, for Playwright APIs such as screenshots or
    /// web-first assertions. It uses Playwright's own role and text engines, which can differ from the driver's in edge
    /// cases: the driver resolves chains with the workbench's locator engine.
    /// </summary>
    public ILocator ToLocator(LocatorChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var root = Page.Locator(RootSelector);
        var story = Page.Locator(StorySelector);
        ILocator? current = null;
        foreach (var step in chain.Steps)
        {
            current = step.Kind switch
            {
                LocatorStepKind.Find => current is null ? Find(root, step.Query!).And(story) : Find(current, step.Query!),
                LocatorStepKind.Nth => (current ?? root.Locator("*").And(story)).Nth(step.Index),
                LocatorStepKind.HasText => (current ?? root.Locator("*").And(story)).Filter(new LocatorFilterOptions { HasText = step.Text }),
                LocatorStepKind.HasNotText => (current ?? root.Locator("*").And(story)).Filter(new LocatorFilterOptions { HasNotText = step.Text }),
                _ => throw new NotSupportedException(step.Kind.ToString()),
            };
        }

        return current ?? throw new ArgumentException("Empty locator chain.", nameof(chain));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken cancellationToken) =>
        DriverJson.Elements(await Page.EvaluateAsync<string?>(Scripts.Query, DriverJson.Steps(locator)).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(action);
        var steps = DriverJson.Steps(locator);
        var actionJson = DriverJson.Action(action);
        var clock = Stopwatch.StartNew();
        var (success, problem) = await Poll.UntilAsync(
            async _ =>
            {
                var result = await TryPerformAsync(locator, steps, actionJson, action, Remaining(timeout, clock)).ConfigureAwait(false);
                return (result is null, result);
            },
            timeout,
            cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            throw await NotActionableAsync(locator, action, problem, timeout, cancellationToken).ConfigureAwait(false);
        }

        await SettleAsync().ConfigureAwait(false);

        if (action.Kind is ElementActionKind.Check or ElementActionKind.Uncheck)
        {
            // The click happened once; Server circuits and async handlers update the state later. Wait without clicking again.
            var shouldBeChecked = action.Kind == ElementActionKind.Check;
            var (isChecked, _) = await Poll.UntilAsync(
                async _ => (await Page.EvaluateAsync<string?>(Scripts.TargetChecked, steps).ConfigureAwait(false) == (shouldBeChecked ? "true" : "false"), true),
                timeout,
                cancellationToken).ConfigureAwait(false);
            if (!isChecked)
            {
                throw new ElementNotActionableException($"Clicking did not change the checked state of {locator} to {shouldBeChecked}.");
            }
        }
    }

    /// <inheritdoc />
    public async Task KeyboardAsync(KeyboardAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.Kind)
        {
            case KeyboardActionKind.Press:
                await Page.Keyboard.PressAsync(ToPlaywrightKey(action.Value)).ConfigureAwait(false);
                break;
            case KeyboardActionKind.Type:
                await Page.Keyboard.TypeAsync(action.Value).ConfigureAwait(false);
                break;
            case KeyboardActionKind.Down:
                await Page.Keyboard.DownAsync(ToPlaywrightKey(action.Value)).ConfigureAwait(false);
                break;
            case KeyboardActionKind.Up:
                await Page.Keyboard.UpAsync(ToPlaywrightKey(action.Value)).ConfigureAwait(false);
                break;
        }

        await SettleAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ElementInfo?> GetFocusedElementAsync(CancellationToken cancellationToken) =>
        DriverJson.Element(await Page.EvaluateAsync<string?>(Scripts.Focused).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionRecord>> GetActionsAsync(CancellationToken cancellationToken)
    {
        var json = await Page.EvaluateAsync<string?>("async () => window.__btp ? await window.__btp.getActions() : '[]'").ConfigureAwait(false);
        return ActionLog.ParseJson(json);
    }

    /// <inheritdoc />
    public async Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        await SettleAsync().ConfigureAwait(false);
        try
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 2000 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Long-polling or streaming connections keep the network busy; idleness is best effort.
        }
    }

    /// <inheritdoc />
    public async Task AdvanceTimeAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        await Page.EvaluateAsync("async (ms) => { if (!window.__btp) throw new Error('Story bridge not ready'); await window.__btp.advanceTime(ms); }", delta.TotalMilliseconds).ConfigureAwait(false);
        await SettleAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> GetMarkupAsync(CancellationToken cancellationToken) => Page.Locator(RootSelector).InnerHTMLAsync();

    /// <inheritdoc />
    public async Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync(CancellationToken cancellationToken)
    {
        var json = await Page.EvaluateAsync<string?>("async () => window.__btp && window.__btp.getRequests ? await window.__btp.getRequests() : '[]'").ConfigureAwait(false);
        return HttpMock.ParseRequests(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JSCallRecord>> GetJSCallsAsync(CancellationToken cancellationToken)
    {
        var json = await Page.EvaluateAsync<string?>("async () => window.__btp && window.__btp.getJSCalls ? await window.__btp.getJSCalls() : '[]'").ConfigureAwait(false);
        return JSCallLog.Parse(json);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Round-trips through the story's renderer so queued render batches are applied.</summary>
    internal async Task SettleAsync()
    {
        if (!HostingModel.IsInteractive())
        {
            return;
        }

        try
        {
            await Page.EvaluateAsync("async () => { if (window.__btp) { await window.__btp.waitForIdle(); } await new Promise(r => requestAnimationFrame(() => r())); }").ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // Navigation or a closed circuit; the next assertion reports the real problem.
        }
    }

    /// <summary>One attempt: resolve the single target and act on it. Returns null when done, or the problem to retry on.</summary>
    private async Task<string?> TryPerformAsync(LocatorChain locator, string steps, string actionJson, ElementAction action, TimeSpan remaining)
    {
        IElementHandle? element = null;
        try
        {
            (element, var problem) = await ResolveAsync(steps, actionJson).ConfigureAwait(false);
            return element is null ? problem : await ActAsync(element, action, remaining).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            return CallLogProblem(ex);
        }
        catch (PlaywrightException ex) when (IsDetached(ex))
        {
            // A re-render replaced the element (or the frame navigated): resolve the locator again.
            return "element was detached from the DOM";
        }
        catch (PlaywrightException ex)
        {
            throw new ElementNotActionableException($"Cannot {action.Describe()} {locator}: {FirstLine(ex.Message)}", ex);
        }
        finally
        {
            if (element is not null)
            {
                await DisposeQuietlyAsync(element).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Resolves the single element an action targets with the workbench's locator engine.</summary>
    private async Task<(IElementHandle? Element, string? Problem)> ResolveAsync(string steps, string actionJson)
    {
        var handle = await Page.EvaluateHandleAsync(Scripts.ActionTarget, new[] { steps, actionJson }).ConfigureAwait(false);
        if (handle is IElementHandle element)
        {
            return (element, null);
        }

        try
        {
            return (null, await handle.JsonValueAsync<string?>().ConfigureAwait(false) ?? "no element matches");
        }
        finally
        {
            await DisposeQuietlyAsync(handle).ConfigureAwait(false);
        }
    }

    private async Task<string?> ActAsync(IElementHandle element, ElementAction action, TimeSpan remaining)
    {
        var ms = (float)remaining.TotalMilliseconds;
        switch (action.Kind)
        {
            case ElementActionKind.Click:
                await element.ClickAsync(new ElementHandleClickOptions { Timeout = ms, Force = action.Force, Button = Button(action.Button), Modifiers = Modifiers(action.Modifiers) }).ConfigureAwait(false);
                break;
            case ElementActionKind.DoubleClick:
                await element.DblClickAsync(new ElementHandleDblClickOptions { Timeout = ms, Force = action.Force, Button = Button(action.Button), Modifiers = Modifiers(action.Modifiers) }).ConfigureAwait(false);
                break;
            case ElementActionKind.Hover:
                await element.HoverAsync(new ElementHandleHoverOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                break;
            case ElementActionKind.Focus:
                await element.FocusAsync().ConfigureAwait(false);
                break;
            case ElementActionKind.Blur:
                await element.EvaluateAsync("element => element.blur()").ConfigureAwait(false);
                break;
            case ElementActionKind.Fill:
            case ElementActionKind.Clear:
                await element.FillAsync(action.Kind == ElementActionKind.Clear ? string.Empty : action.Text ?? string.Empty, new ElementHandleFillOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                break;
            case ElementActionKind.Type:
                await element.FocusAsync().ConfigureAwait(false);
                await Page.Keyboard.TypeAsync(action.Text ?? string.Empty).ConfigureAwait(false);
                break;
            case ElementActionKind.Press:
                await element.PressAsync(ToPlaywrightKey(action.Text!), new ElementHandlePressOptions { Timeout = ms }).ConfigureAwait(false);
                break;
            case ElementActionKind.Check:
            case ElementActionKind.Uncheck:
            {
                // Like the bUnit driver: click once when the state differs, then PerformAsync waits for the new state.
                var plan = await element.EvaluateAsync<string?>(Scripts.CheckPlan, action.Kind == ElementActionKind.Check).ConfigureAwait(false);
                if (plan != "click")
                {
                    return plan;
                }

                await element.ClickAsync(new ElementHandleClickOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                break;
            }

            case ElementActionKind.SelectOption:
                await element.SelectOptionAsync(action.Values ?? [], new ElementHandleSelectOptionOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                break;
            case ElementActionKind.PointerDown:
                await element.HoverAsync(new ElementHandleHoverOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                await Page.Mouse.DownAsync(new MouseDownOptions { Button = Button(action.Button) }).ConfigureAwait(false);
                break;
            case ElementActionKind.PointerUp:
                await element.HoverAsync(new ElementHandleHoverOptions { Timeout = ms, Force = true }).ConfigureAwait(false);
                await Page.Mouse.UpAsync(new MouseUpOptions { Button = Button(action.Button) }).ConfigureAwait(false);
                break;
            case ElementActionKind.DragTo:
            {
                // What Playwright's dragTo does: press on the source, move to the target, release.
                var (target, problem) = await ResolveAsync(DriverJson.Steps(action.Target!), DriverJson.Action(new ElementAction(ElementActionKind.Hover))).ConfigureAwait(false);
                if (target is null)
                {
                    return "drop target: " + problem;
                }

                try
                {
                    await element.HoverAsync(new ElementHandleHoverOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                    await Page.Mouse.DownAsync().ConfigureAwait(false);
                    await target.HoverAsync(new ElementHandleHoverOptions { Timeout = ms }).ConfigureAwait(false);
                    await Page.Mouse.UpAsync().ConfigureAwait(false);
                }
                finally
                {
                    await DisposeQuietlyAsync(target).ConfigureAwait(false);
                }

                break;
            }

            case ElementActionKind.ScrollIntoView:
                await element.ScrollIntoViewIfNeededAsync(new ElementHandleScrollIntoViewIfNeededOptions { Timeout = ms }).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Action {action.Kind} is not supported by the Playwright driver.");
        }

        return null;
    }

    /// <summary>The bUnit driver's failure message: the last problem, then the elements the locator matches.</summary>
    private async Task<ElementNotActionableException> NotActionableAsync(LocatorChain locator, ElementAction action, string? problem, TimeSpan timeout, CancellationToken cancellationToken)
    {
        IReadOnlyList<ElementInfo> matches;
        try
        {
            matches = await QueryAsync(locator, cancellationToken).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            matches = [];
        }

        var details = string.Join(Environment.NewLine, matches.Take(5).Select(m => "  " + m.Html));
        return new ElementNotActionableException(
            $"Cannot {action.Describe()} {locator}: {problem} (waited {timeout.TotalMilliseconds:0} ms).{(details.Length > 0 ? Environment.NewLine + details : string.Empty)}");
    }

    private static TimeSpan Remaining(TimeSpan timeout, Stopwatch clock)
    {
        var remaining = timeout - clock.Elapsed;
        return remaining < MinimumActionTimeout ? MinimumActionTimeout : remaining;
    }

    private static bool IsDetached(PlaywrightException ex) =>
        ex.Message.Contains("not attached to the DOM", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Cannot find context with specified id", StringComparison.OrdinalIgnoreCase);

    /// <summary>The most specific reason in a Playwright timeout's call log, e.g. "element is not stable".</summary>
    private static string CallLogProblem(TimeoutException ex)
    {
        var reason = ex.Message.Split('\n')
            .Select(line => line.Trim().TrimStart('-', ' ').Trim())
            .LastOrDefault(line => line.Contains("intercepts pointer events", StringComparison.Ordinal)
                || (line.StartsWith("element is ", StringComparison.Ordinal) && !line.StartsWith("element is visible, enabled and stable", StringComparison.Ordinal)));
        return reason ?? FirstLine(ex.Message);
    }

    private static async Task DisposeQuietlyAsync(IJSHandle handle)
    {
        try
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The page navigated or closed; the handle is gone already.
        }
    }

    private static ILocator Find(ILocator scope, By query) => query switch
    {
        CssQuery css => scope.Locator(css.Selector),
        RoleQuery role => scope.GetByRole(ParseRole(role.RoleName), new LocatorGetByRoleOptions { Name = role.Name, Exact = role.Exact }),
        TextQuery text => scope.GetByText(text.Value, new LocatorGetByTextOptions { Exact = text.Exact }),
        LabelQuery label => scope.GetByLabel(label.Value, new LocatorGetByLabelOptions { Exact = label.Exact }),
        PlaceholderQuery placeholder => scope.GetByPlaceholder(placeholder.Value, new LocatorGetByPlaceholderOptions { Exact = placeholder.Exact }),

        // A CSS attribute selector reads By.TestIdAttribute now, not when Playwright was launched.
        TestIdQuery testId => scope.Locator($"[{CssIdentifier(By.TestIdAttribute)}=\"{CssString(testId.Value)}\"]"),
        AltTextQuery alt => scope.GetByAltText(alt.Value, new LocatorGetByAltTextOptions { Exact = alt.Exact }),
        TitleQuery title => scope.GetByTitle(title.Value, new LocatorGetByTitleOptions { Exact = title.Exact }),
        _ => throw new NotSupportedException($"Query type '{query.GetType().Name}' is not supported."),
    };

    private static AriaRole ParseRole(string role) =>
        Enum.TryParse<AriaRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Playwright does not support role '{role}'.", nameof(role));

    private static string CssIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsAsciiLetter(c) || c is '_' || c > 0x7F || (i > 0 && (char.IsAsciiDigit(c) || c is '-')))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
        }

        return builder.ToString();
    }

    private static string CssString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\a ", StringComparison.Ordinal);

    private static string ToPlaywrightKey(string chord)
    {
        var (modifiers, key) = Keys.Parse(chord);
        var parts = new List<string>();
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            parts.Add("Control");
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            parts.Add("Meta");
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(key == BlazorTestPlatform.Keys.Space ? " " : key);
        return string.Join('+', parts);
    }

    private static Microsoft.Playwright.MouseButton? Button(Drivers.MouseButton button) => button switch
    {
        Drivers.MouseButton.Middle => Microsoft.Playwright.MouseButton.Middle,
        Drivers.MouseButton.Right => Microsoft.Playwright.MouseButton.Right,
        _ => Microsoft.Playwright.MouseButton.Left,
    };

    private static IEnumerable<KeyboardModifier>? Modifiers(KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.None)
        {
            return null;
        }

        var list = new List<KeyboardModifier>();
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            list.Add(KeyboardModifier.Alt);
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            list.Add(KeyboardModifier.Control);
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            list.Add(KeyboardModifier.Meta);
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            list.Add(KeyboardModifier.Shift);
        }

        return list;
    }

    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? message : message[..index].TrimEnd('\r');
    }
}
