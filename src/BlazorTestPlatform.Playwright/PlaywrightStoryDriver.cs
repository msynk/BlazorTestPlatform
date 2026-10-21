using System.Text.Json;
using BlazorTestPlatform.Drivers;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Playwright;

/// <summary>
/// <see cref="IStoryDriver"/> for real browsers. Locators map 1:1 to Playwright locators (so Playwright's actionability
/// checks apply), and every interaction settles through the story frame's bridge so Server circuits and WebAssembly
/// runtimes have applied their render batches before the next step.
/// </summary>
public sealed class PlaywrightStoryDriver : IStoryDriver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    /// <summary>Converts a locator chain to a Playwright locator.</summary>
    public ILocator ToLocator(LocatorChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ILocator? current = null;
        foreach (var step in chain.Steps)
        {
            current = step.Kind switch
            {
                LocatorStepKind.Find => Find(current, step.Query!),
                LocatorStepKind.Nth => (current ?? Page.Locator("*")).Nth(step.Index),
                LocatorStepKind.HasText => (current ?? Page.Locator("*")).Filter(new LocatorFilterOptions { HasText = step.Text }),
                LocatorStepKind.HasNotText => (current ?? Page.Locator("*")).Filter(new LocatorFilterOptions { HasNotText = step.Text }),
                _ => throw new NotSupportedException(step.Kind.ToString()),
            };
        }

        return current ?? throw new ArgumentException("Empty locator chain.", nameof(chain));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken cancellationToken)
    {
        var json = await ToLocator(locator).EvaluateAllAsync<JsonElement>(Scripts.DescribeAll).ConfigureAwait(false);
        return json.Deserialize<List<ElementInfo>>(JsonOptions) ?? [];
    }

    /// <inheritdoc />
    public async Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var target = ToLocator(locator);
        var ms = (float)timeout.TotalMilliseconds;
        try
        {
            switch (action.Kind)
            {
                case ElementActionKind.Click:
                    await target.ClickAsync(new LocatorClickOptions { Timeout = ms, Force = action.Force, Button = Button(action.Button), Modifiers = Modifiers(action.Modifiers) }).ConfigureAwait(false);
                    break;
                case ElementActionKind.DoubleClick:
                    await target.DblClickAsync(new LocatorDblClickOptions { Timeout = ms, Force = action.Force, Modifiers = Modifiers(action.Modifiers) }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Hover:
                    await target.HoverAsync(new LocatorHoverOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Focus:
                    await target.FocusAsync(new LocatorFocusOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Blur:
                    await target.BlurAsync(new LocatorBlurOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Fill:
                    await target.FillAsync(action.Text ?? string.Empty, new LocatorFillOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Clear:
                    await target.ClearAsync(new LocatorClearOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Type:
                    await target.PressSequentiallyAsync(action.Text ?? string.Empty, new LocatorPressSequentiallyOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Press:
                    await target.PressAsync(ToPlaywrightKey(action.Text!), new LocatorPressOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Check:
                    await target.CheckAsync(new LocatorCheckOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                    break;
                case ElementActionKind.Uncheck:
                    await target.UncheckAsync(new LocatorUncheckOptions { Timeout = ms, Force = action.Force }).ConfigureAwait(false);
                    break;
                case ElementActionKind.SelectOption:
                    await target.SelectOptionAsync(action.Values ?? [], new LocatorSelectOptionOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.PointerDown:
                    await target.HoverAsync(new LocatorHoverOptions { Timeout = ms }).ConfigureAwait(false);
                    await Page.Mouse.DownAsync(new MouseDownOptions { Button = Button(action.Button) }).ConfigureAwait(false);
                    break;
                case ElementActionKind.PointerUp:
                    await target.HoverAsync(new LocatorHoverOptions { Timeout = ms, Force = true }).ConfigureAwait(false);
                    await Page.Mouse.UpAsync(new MouseUpOptions { Button = Button(action.Button) }).ConfigureAwait(false);
                    break;
                case ElementActionKind.DragTo:
                    await target.DragToAsync(ToLocator(action.Target!), new LocatorDragToOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                case ElementActionKind.ScrollIntoView:
                    await target.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = ms }).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException($"Action {action.Kind} is not supported by the Playwright driver.");
            }
        }
        catch (TimeoutException ex)
        {
            throw new ElementNotActionableException($"Cannot {action.Describe()} {locator}: {FirstLine(ex.Message)}", ex);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("strict mode violation", StringComparison.OrdinalIgnoreCase))
        {
            throw new ElementNotActionableException($"Cannot {action.Describe()} {locator}: {FirstLine(ex.Message)}", ex);
        }

        await SettleAsync().ConfigureAwait(false);
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
    public async Task<ElementInfo?> GetFocusedElementAsync(CancellationToken cancellationToken)
    {
        var json = await Page.EvaluateAsync<JsonElement?>(Scripts.DescribeFocused).ConfigureAwait(false);
        return json is { ValueKind: JsonValueKind.Object } element ? element.Deserialize<ElementInfo>(JsonOptions) : null;
    }

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
    public Task<string> GetMarkupAsync(CancellationToken cancellationToken) => Page.Locator("[data-btp-root]").InnerHTMLAsync();

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

    private ILocator Find(ILocator? scope, By query) => query switch
    {
        CssQuery css => scope is null ? Page.Locator(css.Selector) : scope.Locator(css.Selector),
        RoleQuery role => scope is null
            ? Page.GetByRole(ParseRole(role.RoleName), new PageGetByRoleOptions { Name = role.Name, Exact = role.Exact })
            : scope.GetByRole(ParseRole(role.RoleName), new LocatorGetByRoleOptions { Name = role.Name, Exact = role.Exact }),
        TextQuery text => scope is null ? Page.GetByText(text.Value, new PageGetByTextOptions { Exact = text.Exact }) : scope.GetByText(text.Value, new LocatorGetByTextOptions { Exact = text.Exact }),
        LabelQuery label => scope is null ? Page.GetByLabel(label.Value, new PageGetByLabelOptions { Exact = label.Exact }) : scope.GetByLabel(label.Value, new LocatorGetByLabelOptions { Exact = label.Exact }),
        PlaceholderQuery placeholder => scope is null
            ? Page.GetByPlaceholder(placeholder.Value, new PageGetByPlaceholderOptions { Exact = placeholder.Exact })
            : scope.GetByPlaceholder(placeholder.Value, new LocatorGetByPlaceholderOptions { Exact = placeholder.Exact }),
        TestIdQuery testId => scope is null ? Page.GetByTestId(testId.Value) : scope.GetByTestId(testId.Value),
        AltTextQuery alt => scope is null ? Page.GetByAltText(alt.Value, new PageGetByAltTextOptions { Exact = alt.Exact }) : scope.GetByAltText(alt.Value, new LocatorGetByAltTextOptions { Exact = alt.Exact }),
        TitleQuery title => scope is null ? Page.GetByTitle(title.Value, new PageGetByTitleOptions { Exact = title.Exact }) : scope.GetByTitle(title.Value, new LocatorGetByTitleOptions { Exact = title.Exact }),
        _ => throw new NotSupportedException($"Query type '{query.GetType().Name}' is not supported."),
    };

    private static AriaRole ParseRole(string role) =>
        Enum.TryParse<AriaRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Playwright does not support role '{role}'.", nameof(role));

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
