using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Workbench.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;

namespace BlazorTestPlatform.Workbench;

/// <summary>
/// Runs play functions inside the workbench frame, in the story's own runtime (the Server circuit or the WebAssembly
/// app), driving the real DOM through JavaScript. Used by the workbench's "Run interactions" button, so interaction
/// scripts can be watched and debugged live in the browser — under the real render mode — without a test run.
/// </summary>
public sealed class InAppStoryDriver : IStoryDriver
{
    private readonly IJSObjectReference _module;
    private readonly StoryRenderContext _renderContext;
    private readonly StoryServiceScope _services;

    /// <summary>Creates the driver.</summary>
    public InAppStoryDriver(IJSObjectReference module, StoryRenderContext renderContext, StoryServiceScope services, string renderer)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _renderContext = renderContext ?? throw new ArgumentNullException(nameof(renderContext));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Name = $"in-app ({renderer})";
        Capabilities = DriverCapabilities.RealBrowser | DriverCapabilities.Layout | DriverCapabilities.Css | DriverCapabilities.JavaScript | DriverCapabilities.NativeFocus
            | (services.Overrides?.GetService<FakeTimeProvider>() is not null ? DriverCapabilities.FakeTime : DriverCapabilities.None);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DriverCapabilities Capabilities { get; }

    /// <inheritdoc />
    public HostingModel HostingModel => _renderContext.HostingModel;

    /// <inheritdoc />
    public IServiceProvider? Services => _renderContext.Services;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken cancellationToken) =>
        DriverJson.Elements(await _module.InvokeAsync<string>("queryJson", cancellationToken, DriverJson.Steps(locator)));

    /// <inheritdoc />
    public async Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var steps = DriverJson.Steps(locator);
        var actionJson = DriverJson.Action(action);
        var (success, problem) = await Poll.UntilAsync(
            async ct =>
            {
                var result = await _module.InvokeAsync<string?>("performJson", ct, steps, actionJson);
                return (result is null, result);
            },
            timeout,
            cancellationToken);

        if (!success)
        {
            throw await NotActionableAsync(locator, action, problem, timeout, steps, cancellationToken);
        }

        await SettleAsync(cancellationToken);

        if (action.Kind is ElementActionKind.Check or ElementActionKind.Uncheck)
        {
            // The click happened once; Server circuits and async handlers update the state later. Wait without clicking again.
            var shouldBeChecked = action.Kind == ElementActionKind.Check;
            var (isChecked, _) = await Poll.UntilAsync(
                async ct => (await _module.InvokeAsync<string?>("targetCheckedJson", ct, steps) == (shouldBeChecked ? "true" : "false"), true),
                timeout,
                cancellationToken);
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
        var kind = action.Kind switch
        {
            KeyboardActionKind.Press => "press",
            KeyboardActionKind.Type => "type",
            KeyboardActionKind.Down => "down",
            _ => "up",
        };
        await _module.InvokeVoidAsync("keyboard", cancellationToken, kind, action.Value);
        await SettleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ElementInfo?> GetFocusedElementAsync(CancellationToken cancellationToken) =>
        DriverJson.Element(await _module.InvokeAsync<string?>("focusedJson", cancellationToken));

    /// <inheritdoc />
    public Task<IReadOnlyList<ActionRecord>> GetActionsAsync(CancellationToken cancellationToken) => Task.FromResult(_renderContext.Actions.Records);

    /// <inheritdoc />
    public async Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        await SettleAsync(cancellationToken);
        await Task.Delay(30, cancellationToken);
        await SettleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AdvanceTimeAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        var time = _services.Overrides?.GetService<FakeTimeProvider>()
            ?? throw new InvalidOperationException("The story does not use fake time. Call UseFakeTime(...) on the story.");
        time.Advance(delta);
        await WaitForIdleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetMarkupAsync(CancellationToken cancellationToken) => await _module.InvokeAsync<string>("markup", cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_services.Overrides?.GetService<HttpMock>()?.Requests ?? (IReadOnlyList<HttpRequestRecord>)[]);

    /// <inheritdoc />
    public Task<IReadOnlyList<JSCallRecord>> GetJSCallsAsync(CancellationToken cancellationToken) => Task.FromResult(_services.JSCalls.Calls);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>The bUnit driver's failure message: the last problem, then the elements the locator matches.</summary>
    private async Task<ElementNotActionableException> NotActionableAsync(LocatorChain locator, ElementAction action, string? problem, TimeSpan timeout, string steps, CancellationToken cancellationToken)
    {
        var matches = DriverJson.Elements(await _module.InvokeAsync<string?>("queryJson", cancellationToken, steps));
        var details = string.Join(Environment.NewLine, matches.Take(5).Select(m => "  " + m.Html));
        return new ElementNotActionableException(
            $"Cannot {action.Describe()} {locator}: {problem} (waited {timeout.TotalMilliseconds:0} ms).{(details.Length > 0 ? Environment.NewLine + details : string.Empty)}");
    }

    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        // Let the renderer process the dispatched event and apply the resulting batch.
        await Task.Yield();
        await _module.InvokeAsync<bool>("settle", cancellationToken);
    }
}
