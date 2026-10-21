using AngleSharp.Dom;
using BlazorTestPlatform.Accessibility.Dom;
using BlazorTestPlatform.Drivers;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BlazorTestPlatform.Bunit;

/// <summary>
/// In-memory <see cref="IStoryDriver"/> on top of bUnit. It emulates what a browser does around raw events —
/// focus movement, Tab order, change-on-blur, implicit form submission, Enter/Space activation, label forwarding,
/// checkbox toggling and programmatic <c>FocusAsync</c> — so play functions behave like they do in Playwright.
/// </summary>
public sealed class BunitStoryDriver : IStoryDriver
{
    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    // Rounds of FocusAsync calls made by focus/blur handlers themselves; caps focus ping-pong between two handlers.
    private const int MaxFocusRounds = 16;

    private readonly BunitContext _context;
    private readonly IRenderedComponent<StoryRoot> _root;
    private readonly StoryRenderContext _renderContext;
    private string? _focusedPath;
    private string? _valueAtFocus;
    private string? _hoveredPath;
    private int _processedFocusCalls;
    private bool _syncingFocus;
    private int _renderVersion;
    private readonly Dictionary<string, string> _referencePaths = new(StringComparer.Ordinal);
    private readonly CssVisibility? _styles;
    private readonly Lock _referenceGate = new();

    /// <summary>Creates a driver for a rendered story.</summary>
    public BunitStoryDriver(BunitContext context, IRenderedComponent<StoryRoot> root, StoryRenderContext renderContext, CssVisibility? styles = null)
    {
        _styles = styles;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _renderContext = renderContext ?? throw new ArgumentNullException(nameof(renderContext));
        CaptureElementReferences();
        _root.OnMarkupUpdated += (_, _) =>
        {
            Interlocked.Increment(ref _renderVersion);
            CaptureElementReferences();
        };
        Capabilities = renderContext.Story.FakeTimeStart is not null ? DriverCapabilities.FakeTime : DriverCapabilities.None;
    }

    /// <inheritdoc />
    public string Name => "bunit";

    /// <inheritdoc />
    public DriverCapabilities Capabilities { get; }

    /// <inheritdoc />
    public HostingModel HostingModel => HostingModel.InMemory;

    /// <inheritdoc />
    public IServiceProvider? Services => _context.Services;

    /// <summary>Number of completed renders observed (useful to detect idleness).</summary>
    public int RenderVersion => Volatile.Read(ref _renderVersion);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken cancellationToken)
    {
        await SyncProgrammaticFocusAsync().ConfigureAwait(false);
        var scope = Scope();
        var focused = ResolvePath(scope, _focusedPath);
        return DomQueries.Resolve(scope, locator)
            .Select(e => DomQueries.Describe(e, scope, ReferenceEquals(e, focused)))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(action);

        // Focus calls made since the last step happened before this action; apply them first so they can't undo it.
        await SyncProgrammaticFocusAsync().ConfigureAwait(false);
        var path = await WaitForActionableAsync(locator, action, timeout, cancellationToken).ConfigureAwait(false);

        if (action.Kind is ElementActionKind.Click or ElementActionKind.DoubleClick or ElementActionKind.Hover or ElementActionKind.PointerDown or ElementActionKind.PointerUp)
        {
            // Only forced actions reach inert content, and the pointer passes through it.
            if (HitTargetPath(path) is not { } hit)
            {
                await SettleAsync().ConfigureAwait(false);
                return;
            }

            path = hit;
        }

        switch (action.Kind)
        {
            case ElementActionKind.Click:
                await ClickAsync(path, action.Modifiers, action.Button, clickCount: 1).ConfigureAwait(false);
                break;
            case ElementActionKind.DoubleClick:
                await ClickAsync(path, action.Modifiers, action.Button, clickCount: 1).ConfigureAwait(false);
                await ClickAsync(path, action.Modifiers, action.Button, clickCount: 2).ConfigureAwait(false);
                await DispatchAsync(path, "ondblclick", Mouse("dblclick", action.Modifiers, action.Button, 2)).ConfigureAwait(false);
                break;
            case ElementActionKind.Hover:
                await HoverAsync(path).ConfigureAwait(false);
                break;
            case ElementActionKind.Focus:
                await FocusIfFocusableAsync(path).ConfigureAwait(false);
                break;
            case ElementActionKind.Blur:
                if (_focusedPath == path)
                {
                    await SetFocusAsync(null).ConfigureAwait(false);
                }

                break;
            case ElementActionKind.Fill:
            case ElementActionKind.Clear:
                await FillAsync(path, action.Kind == ElementActionKind.Clear ? string.Empty : action.Text ?? string.Empty).ConfigureAwait(false);
                break;
            case ElementActionKind.Type:
                await FocusIfFocusableAsync(path).ConfigureAwait(false);
                foreach (var ch in action.Text ?? string.Empty)
                {
                    await PressOnFocusedAsync(ch.ToString(), KeyModifiers.None).ConfigureAwait(false);
                }

                break;
            case ElementActionKind.Press:
            {
                await FocusIfFocusableAsync(path).ConfigureAwait(false);
                var (modifiers, key) = Keys.Parse(action.Text ?? throw new ArgumentException("Press requires a key."));
                await PressOnFocusedAsync(key, modifiers).ConfigureAwait(false);
                break;
            }

            case ElementActionKind.Check:
            case ElementActionKind.Uncheck:
                await SetCheckedAsync(path, action.Kind == ElementActionKind.Check, timeout, cancellationToken).ConfigureAwait(false);
                break;
            case ElementActionKind.SelectOption:
                await SelectOptionAsync(path, action.Values ?? []).ConfigureAwait(false);
                break;
            case ElementActionKind.PointerDown:
                await DispatchAsync(path, "onpointerdown", Pointer("pointerdown", action.Modifiers, action.Button)).ConfigureAwait(false);
                await DispatchAsync(path, "onmousedown", Mouse("mousedown", action.Modifiers, action.Button, 1)).ConfigureAwait(false);
                break;
            case ElementActionKind.PointerUp:
                await DispatchAsync(path, "onpointerup", Pointer("pointerup", action.Modifiers, action.Button)).ConfigureAwait(false);
                await DispatchAsync(path, "onmouseup", Mouse("mouseup", action.Modifiers, action.Button, 1)).ConfigureAwait(false);
                break;
            case ElementActionKind.DragTo:
                await DragAsync(path, action.Target ?? throw new ArgumentException("DragTo requires a target."), timeout, cancellationToken).ConfigureAwait(false);
                break;
            case ElementActionKind.ScrollIntoView:
                break;
            default:
                throw new NotSupportedException($"Action {action.Kind} is not supported by the bUnit driver.");
        }

        await SettleAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task KeyboardAsync(KeyboardAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        await SyncProgrammaticFocusAsync().ConfigureAwait(false);
        switch (action.Kind)
        {
            case KeyboardActionKind.Press:
            {
                var (modifiers, key) = Keys.Parse(action.Value);
                await PressOnFocusedAsync(key, modifiers).ConfigureAwait(false);
                break;
            }

            case KeyboardActionKind.Type:
                foreach (var ch in action.Value)
                {
                    await PressOnFocusedAsync(ch.ToString(), KeyModifiers.None).ConfigureAwait(false);
                }

                break;
            case KeyboardActionKind.Down:
            {
                var (modifiers, key) = Keys.Parse(action.Value);
                await DispatchToFocusedAsync("onkeydown", Keyboard("keydown", key, modifiers)).ConfigureAwait(false);
                break;
            }

            case KeyboardActionKind.Up:
            {
                var (modifiers, key) = Keys.Parse(action.Value);
                await DispatchToFocusedAsync("onkeyup", Keyboard("keyup", key, modifiers)).ConfigureAwait(false);
                break;
            }
        }

        await SettleAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ElementInfo?> GetFocusedElementAsync(CancellationToken cancellationToken)
    {
        await SyncProgrammaticFocusAsync().ConfigureAwait(false);
        var scope = Scope();
        var focused = ResolvePath(scope, _focusedPath);
        return focused is null ? null : DomQueries.Describe(focused, scope, isFocused: true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ActionRecord>> GetActionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_renderContext.Actions.Records);

    /// <inheritdoc />
    public async Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        // bUnit renders synchronously; async component work completes on the thread pool. Consider the story idle
        // once no render happened for a short quiet period (bounded to keep tests fast).
        var deadline = DateTime.UtcNow.AddSeconds(2);
        var version = RenderVersion;
        while (DateTime.UtcNow < deadline)
        {
            await _root.InvokeAsync(() => { }).ConfigureAwait(false);
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            var current = RenderVersion;
            if (current == version)
            {
                return;
            }

            version = current;
        }
    }

    /// <inheritdoc />
    public async Task AdvanceTimeAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        var time = _context.Services.GetService<FakeTimeProvider>()
            ?? throw new InvalidOperationException("The story does not use fake time. Call UseFakeTime(...) on the story.");
        await _root.InvokeAsync(() => time.Advance(delta)).ConfigureAwait(false);
        await WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> GetMarkupAsync(CancellationToken cancellationToken) => Task.FromResult(_root.Markup);

    /// <inheritdoc />
    public Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_context.Services.GetService<HttpMock>()?.Requests ?? (IReadOnlyList<HttpRequestRecord>)[]);

    /// <inheritdoc />
    public Task<IReadOnlyList<JSCallRecord>> GetJSCallsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_context.Services.GetService<JSCallLog>()?.Calls ?? (IReadOnlyList<JSCallRecord>)[]);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal DomScope Scope() => new(_root.Nodes) { Styles = _styles };

    private static IElement? ResolvePath(DomScope scope, string? path) =>
        path is null ? null : scope.QuerySelectorAll(path).FirstOrDefault();

    private async Task<string> WaitForActionableAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var requiresEnabled = action.Kind is ElementActionKind.Click or ElementActionKind.DoubleClick or ElementActionKind.Fill or ElementActionKind.Clear
            or ElementActionKind.Type or ElementActionKind.Check or ElementActionKind.Uncheck or ElementActionKind.SelectOption;
        var requiresEditable = action.Kind is ElementActionKind.Fill or ElementActionKind.Clear;

        // Inert content takes neither the pointer nor focus; only focus/blur/scroll make sense on it (as no-ops).
        var requiresInteractive = action.Kind is not (ElementActionKind.Focus or ElementActionKind.Blur or ElementActionKind.ScrollIntoView);

        var (success, state) = await Poll.UntilAsync(
            ct =>
            {
                var scope = Scope();
                var matches = DomQueries.Resolve(scope, locator);
                if (matches.Count != 1)
                {
                    return Task.FromResult((false, (Path: (string?)null, Problem: (string?)(matches.Count == 0 ? "no element matches" : $"strict mode violation: {matches.Count} elements match"), Matches: matches)));
                }

                var element = matches[0];
                string? problem = null;
                if (!action.Force && DomFacts.IsHidden(element, scope))
                {
                    problem = "element is not visible";
                }
                else if (!action.Force && requiresInteractive && DomFacts.IsInert(element))
                {
                    problem = "element is inert";
                }
                else if (!action.Force && requiresEnabled && DomFacts.IsDisabled(element))
                {
                    problem = "element is disabled";
                }
                else if (!action.Force && requiresEditable && !DomFacts.IsEditable(element) && element.LocalName != "input")
                {
                    problem = "element is not editable";
                }

                return Task.FromResult((problem is null, (Path: (string?)DomFacts.CssPath(element, scope), Problem: problem, Matches: matches)));
            },
            timeout,
            cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            var details = string.Join(Environment.NewLine, state.Matches.Take(5).Select(m => "  " + DomFacts.Snippet(m)));
            throw new ElementNotActionableException(
                $"Cannot {action.Describe()} {locator}: {state.Problem} (waited {timeout.TotalMilliseconds:0} ms).{(details.Length > 0 ? Environment.NewLine + details : string.Empty)}");
        }

        return state.Path!;
    }

    private async Task ClickAsync(string path, KeyModifiers modifiers, MouseButton button, int clickCount)
    {
        await HoverAsync(path).ConfigureAwait(false);
        await DispatchAsync(path, "onpointerdown", Pointer("pointerdown", modifiers, button)).ConfigureAwait(false);
        await DispatchAsync(path, "onmousedown", Mouse("mousedown", modifiers, button, clickCount)).ConfigureAwait(false);

        // mousedown moves focus to the nearest focusable ancestor (or the body).
        var scope = Scope();
        var target = ResolvePath(scope, path);
        if (target is null)
        {
            return;
        }

        var focusTarget = FocusableAncestor(target, scope);
        await SetFocusAsync(focusTarget is null ? null : DomFacts.CssPath(focusTarget, Scope())).ConfigureAwait(false);

        await DispatchAsync(path, "onpointerup", Pointer("pointerup", modifiers, button)).ConfigureAwait(false);
        await DispatchAsync(path, "onmouseup", Mouse("mouseup", modifiers, button, clickCount)).ConfigureAwait(false);

        if (button == MouseButton.Right)
        {
            await DispatchAsync(path, "oncontextmenu", Mouse("contextmenu", modifiers, button, clickCount)).ConfigureAwait(false);
            return;
        }

        if (button != MouseButton.Left)
        {
            await DispatchAsync(path, "onauxclick", Mouse("auxclick", modifiers, button, clickCount)).ConfigureAwait(false);
            return;
        }

        await ActivateAsync(path, Mouse("click", modifiers, button, clickCount)).ConfigureAwait(false);
    }

    /// <summary>Fires click plus the browser's default activation behaviour.</summary>
    private async Task ActivateAsync(string path, MouseEventArgs click)
    {
        var scope = Scope();
        var element = ResolvePath(scope, path);

        // Disabled form controls never get click; aria-disabled is semantics only, so browsers still click those.
        if (element is null || DomFacts.IsNativelyDisabled(element))
        {
            return;
        }

        // Checkboxes and radios toggle before click handlers observe them.
        var toggled = TryToggle(element);
        await DispatchAsync(path, "onclick", click).ConfigureAwait(false);

        if (toggled is { } isChecked)
        {
            var value = element.GetAttribute("type")?.Equals("radio", StringComparison.OrdinalIgnoreCase) == true
                ? (object?)(element.GetAttribute("value") ?? "on")
                : isChecked;
            await DispatchAsync(path, "oninput", new ChangeEventArgs { Value = value }).ConfigureAwait(false);
            await DispatchAsync(path, "onchange", new ChangeEventArgs { Value = value }).ConfigureAwait(false);
            return;
        }

        element = ResolvePath(Scope(), path);
        if (element is null)
        {
            return;
        }

        // Label activation forwards to the labelled control.
        if ((element.LocalName == "label" ? element : element.Closest("label")) is { } label)
        {
            if (!IsInteractive(element))
            {
                var labelScope = Scope();
                var control = LabelledControl(label, labelScope);
                if (control is not null && !DomFacts.IsNativelyDisabled(control))
                {
                    var controlPath = DomFacts.CssPath(control, labelScope);
                    await FocusIfFocusableAsync(controlPath).ConfigureAwait(false);
                    await ActivateAsync(controlPath, click).ConfigureAwait(false);
                    return;
                }
            }
        }

        // Submit buttons submitting their form is handled by bUnit's click dispatch.
    }

    private async Task HoverAsync(string path)
    {
        if (_hoveredPath == path)
        {
            return;
        }

        if (_hoveredPath is not null)
        {
            var previous = _hoveredPath;
            await DispatchAsync(previous, "onpointerout", Pointer("pointerout", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
            await DispatchAsync(previous, "onpointerleave", Pointer("pointerleave", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
            await DispatchAsync(previous, "onmouseout", Mouse("mouseout", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
            await DispatchAsync(previous, "onmouseleave", Mouse("mouseleave", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
        }

        _hoveredPath = path;
        await DispatchAsync(path, "onpointerover", Pointer("pointerover", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
        await DispatchAsync(path, "onpointerenter", Pointer("pointerenter", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
        await DispatchAsync(path, "onmouseover", Mouse("mouseover", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
        await DispatchAsync(path, "onmouseenter", Mouse("mouseenter", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
        await DispatchAsync(path, "onpointermove", Pointer("pointermove", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
        await DispatchAsync(path, "onmousemove", Mouse("mousemove", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
    }

    private async Task SetFocusAsync(string? path)
    {
        if (_focusedPath == path)
        {
            return;
        }

        var previous = _focusedPath;
        if (previous is not null)
        {
            var element = ResolvePath(Scope(), previous);
            if (element is not null)
            {
                var current = DomQueries.ValueOf(element);
                if (_valueAtFocus is not null && current is not null && current != _valueAtFocus && IsTextLike(element))
                {
                    await DispatchAsync(previous, "onchange", new ChangeEventArgs { Value = current }).ConfigureAwait(false);
                }

                await DispatchAsync(previous, "onblur", new FocusEventArgs { Type = "blur" }).ConfigureAwait(false);
                await DispatchAsync(previous, "onfocusout", new FocusEventArgs { Type = "focusout" }).ConfigureAwait(false);
            }
        }

        _focusedPath = path;
        _valueAtFocus = null;
        if (path is null)
        {
            return;
        }

        var target = ResolvePath(Scope(), path);
        _valueAtFocus = target is null ? null : DomQueries.ValueOf(target);
        await DispatchAsync(path, "onfocus", new FocusEventArgs { Type = "focus" }).ConfigureAwait(false);
        await DispatchAsync(path, "onfocusin", new FocusEventArgs { Type = "focusin" }).ConfigureAwait(false);
    }

    /// <summary><c>element.focus()</c>: moves focus only when the element can take it (not hidden, disabled or inert).</summary>
    private async Task FocusIfFocusableAsync(string path)
    {
        var scope = Scope();
        if (ResolvePath(scope, path) is { } element && DomFacts.IsFocusable(element, scope))
        {
            await SetFocusAsync(DomFacts.CssPath(element, scope)).ConfigureAwait(false);
        }
    }

    private async Task FillAsync(string path, string value)
    {
        var element = ResolvePath(Scope(), path) ?? throw new ElementNotActionableException($"Element {path} disappeared.");
        var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        if (element.LocalName == "select" || (element.LocalName == "input" && type is "checkbox" or "radio" or "button" or "submit" or "reset" or "file" or "image"))
        {
            throw new ElementNotActionableException($"Cannot fill <{element.LocalName} type=\"{type}\">; use CheckAsync or SelectOptionAsync.");
        }

        if (!DomFacts.IsEditable(element) && !(element.LocalName == "input" && type is "color" or "range"))
        {
            throw new ElementNotActionableException($"Element is not editable: {DomFacts.Snippet(element)}");
        }

        await SetFocusAsync(path).ConfigureAwait(false);
        element = ResolvePath(Scope(), path);
        element?.SetAttribute("value", value);
        await DispatchAsync(path, "oninput", new ChangeEventArgs { Value = value }).ConfigureAwait(false);
    }

    private async Task PressOnFocusedAsync(string key, KeyModifiers modifiers)
    {
        var pressedPath = _focusedPath;
        var keydown = Keyboard("keydown", key, modifiers);
        var prevented = pressedPath is not null && HasPreventDefault(ResolvePath(Scope(), pressedPath), "onkeydown");
        await DispatchToFocusedAsync("onkeydown", keydown).ConfigureAwait(false);

        // A keydown handler that moves focus (roving tabindex, dialogs) redirects the rest of the key press, as in a browser.
        await SyncProgrammaticFocusAsync().ConfigureAwait(false);
        var path = _focusedPath;
        var element = path is null ? null : ResolvePath(Scope(), path);
        var printable = Keys.IsPrintable(key) && (modifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0;

        if (printable)
        {
            await DispatchToFocusedAsync("onkeypress", Keyboard("keypress", key, modifiers)).ConfigureAwait(false);
        }

        if (!prevented)
        {
            await DefaultKeyActionAsync(path, element, key, modifiers, printable).ConfigureAwait(false);
        }

        await DispatchToFocusedAsync("onkeyup", Keyboard("keyup", key, modifiers)).ConfigureAwait(false);

        // Space activates buttons and checks checkboxes and radios on keyup, if the pressed element kept focus. Not links.
        if (!prevented && key is Keys.Space or " " && pressedPath is not null && _focusedPath == pressedPath
            && ResolvePath(Scope(), pressedPath) is { } pressed && (IsKeyboardClickable(pressed) || IsCheckable(pressed)))
        {
            await ActivateAsync(pressedPath, Mouse("click", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
        }
    }

    private async Task DefaultKeyActionAsync(string? path, IElement? element, string key, KeyModifiers modifiers, bool printable)
    {
        if (key == Keys.Tab)
        {
            await MoveFocusAsync(backwards: (modifiers & KeyModifiers.Shift) != 0).ConfigureAwait(false);
            return;
        }

        if (path is null || element is null)
        {
            return;
        }

        if (key == Keys.Enter)
        {
            // Enter clicks buttons and follows links; in any other input (checkboxes and radios included: they don't
            // toggle on Enter) it submits the form implicitly.
            if (IsKeyboardClickable(element) || (element.LocalName is "a" or "area" && element.HasAttribute("href")))
            {
                await ActivateAsync(path, Mouse("click", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
            }
            else if (element.LocalName == "input")
            {
                await SubmitImplicitlyAsync(path, element).ConfigureAwait(false);
            }

            return;
        }

        if (!DomFacts.IsEditable(element))
        {
            return;
        }

        var value = DomQueries.ValueOf(element) ?? string.Empty;
        string? next = null;
        if (printable)
        {
            next = value + Keys.ToDomKey(key);
        }
        else if (key == Keys.Backspace && value.Length > 0)
        {
            next = value[..^1];
        }

        if (next is not null)
        {
            element.SetAttribute("value", next);
            await DispatchAsync(path, "oninput", new ChangeEventArgs { Value = next }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Enter in a form field. The edit is committed (change) first; then the form's default button (its first submit
    /// button) is clicked unless it is disabled. A form without one submits only when the field is its single text-like field.
    /// </summary>
    private async Task SubmitImplicitlyAsync(string path, IElement field)
    {
        var current = DomQueries.ValueOf(field);
        if (IsTextLike(field) && _valueAtFocus is not null && current != _valueAtFocus)
        {
            await DispatchAsync(path, "onchange", new ChangeEventArgs { Value = current }).ConfigureAwait(false);
            _valueAtFocus = current;
        }

        var scope = Scope();
        if (ResolvePath(scope, path) is not { } input || FormOwner(input, scope) is not { } form)
        {
            return;
        }

        if (scope.Elements().FirstOrDefault(e => IsSubmitButton(e) && ReferenceEquals(FormOwner(e, scope), form)) is { } defaultButton)
        {
            if (!DomFacts.IsNativelyDisabled(defaultButton))
            {
                await ActivateAsync(DomFacts.CssPath(defaultButton, scope), Mouse("click", KeyModifiers.None, MouseButton.Left, 0)).ConfigureAwait(false);
            }

            return;
        }

        if (BlocksImplicitSubmission(input) && scope.Elements().Count(e => BlocksImplicitSubmission(e) && ReferenceEquals(FormOwner(e, scope), form)) == 1)
        {
            await DispatchAsync(DomFacts.CssPath(form, scope), "onsubmit", EventArgs.Empty).ConfigureAwait(false);
        }
    }

    private async Task MoveFocusAsync(bool backwards)
    {
        var scope = Scope();
        var order = DomFacts.TabOrder(scope);
        if (order.Count == 0)
        {
            await SetFocusAsync(null).ConfigureAwait(false);
            return;
        }

        var current = ResolvePath(scope, _focusedPath);
        var index = current is null ? -1 : order.ToList().FindIndex(e => ReferenceEquals(e, current));
        int next;
        if (index < 0)
        {
            // Focus is outside the tab sequence (body or a tabindex=-1 element): start from its position in the document.
            if (current is null)
            {
                next = backwards ? order.Count - 1 : 0;
            }
            else
            {
                var all = scope.Elements().ToList();
                var position = all.IndexOf(current);
                var after = order.Select((e, i) => (e, i)).Where(t => all.IndexOf(t.e) > position).Select(t => t.i).DefaultIfEmpty(-1).First();
                next = backwards ? (after < 0 ? order.Count - 1 : after - 1) : after;
            }
        }
        else
        {
            next = backwards ? index - 1 : index + 1;
        }

        if (next < 0 || next >= order.Count)
        {
            // Leaving the story: focus goes to the browser chrome / document.
            await SetFocusAsync(null).ConfigureAwait(false);
            return;
        }

        await SetFocusAsync(DomFacts.CssPath(order[next], scope)).ConfigureAwait(false);
    }

    private async Task SetCheckedAsync(string path, bool shouldBeChecked, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var element = ResolvePath(Scope(), path) ?? throw new ElementNotActionableException($"Element {path} disappeared.");
        var state = DomQueries.CheckedState(element);
        if (state is null)
        {
            throw new ElementNotActionableException($"Element is not a checkbox, radio or switch: {DomFacts.Snippet(element)}");
        }

        if (state == shouldBeChecked)
        {
            return;
        }

        if (!shouldBeChecked && string.Equals(element.GetAttribute("type"), "radio", StringComparison.OrdinalIgnoreCase))
        {
            throw new ElementNotActionableException("Cannot uncheck a radio button.");
        }

        if (HitTargetPath(path) is { } hit)
        {
            await ClickAsync(hit, KeyModifiers.None, MouseButton.Left, 1).ConfigureAwait(false);
        }

        await SettleAsync().ConfigureAwait(false);

        var (ok, _) = await Poll.UntilAsync(
            _ =>
            {
                var current = ResolvePath(Scope(), path);
                return Task.FromResult((current is not null && DomQueries.CheckedState(current) == shouldBeChecked, true));
            },
            timeout,
            cancellationToken).ConfigureAwait(false);

        if (!ok)
        {
            throw new ElementNotActionableException($"Clicking did not change the checked state of {path} to {shouldBeChecked}.");
        }
    }

    private async Task SelectOptionAsync(string path, IReadOnlyList<string> values)
    {
        var element = ResolvePath(Scope(), path) ?? throw new ElementNotActionableException($"Element {path} disappeared.");
        if (element.LocalName != "select")
        {
            throw new ElementNotActionableException($"Element is not a <select>: {DomFacts.Snippet(element)}");
        }

        var options = element.QuerySelectorAll("option").ToArray();
        var selected = new List<string>();
        foreach (var wanted in values)
        {
            var option = options.FirstOrDefault(o => (o.GetAttribute("value") ?? DomFacts.TextOf(o)) == wanted)
                ?? options.FirstOrDefault(o => DomFacts.TextOf(o) == wanted)
                ?? throw new ElementNotActionableException($"<select> has no option with value or label \"{wanted}\".");
            selected.Add(option.GetAttribute("value") ?? DomFacts.TextOf(option));
        }

        await SetFocusAsync(path).ConfigureAwait(false);
        var multiple = element.HasAttribute("multiple");
        object value = multiple ? selected.ToArray() : selected.FirstOrDefault() ?? string.Empty;
        ResolvePath(Scope(), path)?.SetAttribute("value", multiple ? string.Join(",", selected) : (string)value);
        await DispatchAsync(path, "oninput", new ChangeEventArgs { Value = value }).ConfigureAwait(false);
        await DispatchAsync(path, "onchange", new ChangeEventArgs { Value = value }).ConfigureAwait(false);
        _valueAtFocus = DomQueries.ValueOf(ResolvePath(Scope(), path)!);
    }

    private async Task DragAsync(string sourcePath, LocatorChain target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var targetPath = await WaitForActionableAsync(target, new ElementAction(ElementActionKind.Hover), timeout, cancellationToken).ConfigureAwait(false);
        var data = new DataTransfer { DropEffect = "move", EffectAllowed = "all", Items = [], Types = [], Files = [] };
        await DispatchAsync(sourcePath, "onpointerdown", Pointer("pointerdown", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
        await DispatchAsync(sourcePath, "ondragstart", new DragEventArgs { Type = "dragstart", DataTransfer = data }).ConfigureAwait(false);
        await DispatchAsync(targetPath, "ondragenter", new DragEventArgs { Type = "dragenter", DataTransfer = data }).ConfigureAwait(false);
        await DispatchAsync(targetPath, "ondragover", new DragEventArgs { Type = "dragover", DataTransfer = data }).ConfigureAwait(false);
        await DispatchAsync(targetPath, "ondrop", new DragEventArgs { Type = "drop", DataTransfer = data }).ConfigureAwait(false);
        await DispatchAsync(sourcePath, "ondragend", new DragEventArgs { Type = "dragend", DataTransfer = data }).ConfigureAwait(false);
        await DispatchAsync(targetPath, "onpointerup", Pointer("pointerup", KeyModifiers.None, MouseButton.Left)).ConfigureAwait(false);
    }

    private Task DispatchToFocusedAsync(string eventName, EventArgs args) =>
        _focusedPath is { } path ? DispatchAsync(path, eventName, args) : Task.CompletedTask;

    /// <summary>Dispatches an event to the element at <paramref name="path"/>; bubbling is handled by bUnit.</summary>
    private async Task<bool> DispatchAsync(string path, string eventName, EventArgs args)
    {
        var element = ResolvePath(Scope(), path);
        if (element is null)
        {
            return false;
        }

        try
        {
            await element.TriggerEventAsync(eventName, args).ConfigureAwait(false);
            return true;
        }
        catch (MissingEventHandlerException)
        {
            return false;
        }
    }

    private async Task SettleAsync()
    {
        await _root.InvokeAsync(() => { }).ConfigureAwait(false);
        await SyncProgrammaticFocusAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Applies <c>ElementReference.FocusAsync()</c> calls captured from JS interop, in call order, through the same path as
    /// user focus movement: change (for an edited field), blur and focusout on the old element, focus and focusin on the
    /// new one. Calls made by those handlers are applied in the next round; the rounds are capped so handlers that keep
    /// moving focus back and forth cannot loop forever.
    /// </summary>
    private async Task SyncProgrammaticFocusAsync()
    {
        if (_syncingFocus)
        {
            return;
        }

        _syncingFocus = true;
        try
        {
            for (var round = 0; round < MaxFocusRounds; round++)
            {
                var invocations = _context.JSInterop.Invocations[FocusIdentifier];
                var count = invocations.Count;
                if (count <= _processedFocusCalls)
                {
                    return;
                }

                var first = _processedFocusCalls;
                _processedFocusCalls = count;
                for (var i = first; i < count; i++)
                {
                    if (invocations[i].Arguments.Count > 0 && invocations[i].Arguments[0] is ElementReference reference)
                    {
                        string? path;
                        lock (_referenceGate)
                        {
                            path = _referencePaths.GetValueOrDefault(reference.Id);
                        }

                        if (path is not null)
                        {
                            await FocusIfFocusableAsync(path).ConfigureAwait(false);
                        }
                    }
                }
            }

            // Still moving after the last round: drop the remaining calls rather than loop forever.
            _processedFocusCalls = _context.JSInterop.Invocations[FocusIdentifier].Count;
        }
        finally
        {
            _syncingFocus = false;
        }
    }

    /// <summary>
    /// Where a pointer aimed at <paramref name="path"/> lands. Hit testing skips inert content, so it reaches the nearest
    /// ancestor outside the inert subtree, or nothing in the story.
    /// </summary>
    private string? HitTargetPath(string path)
    {
        var scope = Scope();
        var element = ResolvePath(scope, path);
        if (element is null || !DomFacts.IsInert(element))
        {
            return path;
        }

        var target = element.ParentElement;
        while (target is not null && DomFacts.IsInert(target))
        {
            target = target.ParentElement;
        }

        return target is null || target.LocalName is "body" or "html" ? null : DomFacts.CssPath(target, scope);
    }

    /// <summary>
    /// Records where each <c>@ref</c> element lives. Blazor only exposes a capture id on the render that creates the
    /// element (bUnit prints it as <c>blazor:elementReference</c>), so ids are remembered as they appear.
    /// </summary>
    private void CaptureElementReferences()
    {
        var scope = Scope();
        foreach (var element in scope.Elements())
        {
            foreach (var attribute in element.Attributes)
            {
                if (attribute.Name.Equals("blazor:elementreference", StringComparison.OrdinalIgnoreCase) && attribute.Value.Length > 0)
                {
                    var path = DomFacts.CssPath(element, scope);
                    lock (_referenceGate)
                    {
                        _referencePaths[attribute.Value] = path;
                    }
                }
            }
        }
    }

    private static bool? TryToggle(IElement element)
    {
        if (element.LocalName != "input")
        {
            return null;
        }

        var type = (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant();
        if (type == "checkbox")
        {
            var isChecked = !element.HasAttribute("checked");
            if (isChecked)
            {
                element.SetAttribute("checked", string.Empty);
            }
            else
            {
                element.RemoveAttribute("checked");
            }

            return isChecked;
        }

        if (type == "radio")
        {
            if (element.HasAttribute("checked"))
            {
                return null;
            }

            element.SetAttribute("checked", string.Empty);
            return true;
        }

        return null;
    }

    private static IElement? FocusableAncestor(IElement element, DomScope scope)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (DomFacts.IsFocusable(current, scope))
            {
                return current;
            }
        }

        return null;
    }

    private static IElement? LabelledControl(IElement label, DomScope scope)
    {
        if (label.GetAttribute("for") is { Length: > 0 } id)
        {
            return scope.GetElementById(id);
        }

        return label.QuerySelector("input, select, textarea, button");
    }

    private static bool IsInteractive(IElement element) =>
        element.LocalName is "input" or "select" or "textarea" or "button" || (element.LocalName == "a" && element.HasAttribute("href"));

    /// <summary>Elements both Enter and Space click: buttons, button-like inputs and the summary of a details element.</summary>
    private static bool IsKeyboardClickable(IElement element) =>
        element.LocalName == "button"
        || (element.LocalName == "input" && (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant() is "button" or "submit" or "reset" or "image" or "file" or "color")
        || DomFacts.IsDetailsSummary(element);

    /// <summary>Checkboxes and radios: Space toggles them, Enter does not.</summary>
    private static bool IsCheckable(IElement element) =>
        element.LocalName == "input" && (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant() is "checkbox" or "radio";

    private static bool IsSubmitButton(IElement element) => element.LocalName switch
    {
        "button" => (element.GetAttribute("type") ?? "submit").ToLowerInvariant() is not ("button" or "reset"),
        "input" => (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant() is "submit" or "image",
        _ => false,
    };

    /// <summary>Text-like inputs: a form with one of them and no submit button submits on Enter.</summary>
    private static bool BlocksImplicitSubmission(IElement element) =>
        element.LocalName == "input"
        && (element.GetAttribute("type") ?? "text").ToLowerInvariant() is not ("hidden" or "checkbox" or "radio" or "range" or "color" or "file" or "button" or "submit" or "reset" or "image");

    /// <summary>The form a control belongs to: the one its <c>form</c> attribute names, otherwise its form ancestor.</summary>
    private static IElement? FormOwner(IElement element, DomScope scope) =>
        element.GetAttribute("form") is { } formId
            ? scope.GetElementById(formId) is { LocalName: "form" } owner ? owner : null
            : element.Closest("form");

    private static bool IsTextLike(IElement element) =>
        element.LocalName == "textarea" || DomFacts.IsContentEditable(element)
        || (element.LocalName == "input" && (element.GetAttribute("type") ?? "text").ToLowerInvariant() is not ("checkbox" or "radio" or "button" or "submit" or "reset" or "file" or "image"));

    private static bool HasPreventDefault(IElement? element, string eventName)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.Attributes.Any(a => a.Name.Equals($"blazor:{eventName}:preventdefault", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static MouseEventArgs Mouse(string type, KeyModifiers modifiers, MouseButton button, long detail) => new()
    {
        Type = type,
        Detail = detail,
        Button = (long)button,
        Buttons = type is "mousedown" ? 1 << (int)button : 0,
        ShiftKey = (modifiers & KeyModifiers.Shift) != 0,
        CtrlKey = (modifiers & KeyModifiers.Control) != 0,
        AltKey = (modifiers & KeyModifiers.Alt) != 0,
        MetaKey = (modifiers & KeyModifiers.Meta) != 0,
    };

    private static PointerEventArgs Pointer(string type, KeyModifiers modifiers, MouseButton button) => new()
    {
        Type = type,
        PointerId = 1,
        PointerType = "mouse",
        IsPrimary = true,
        Button = (long)button,
        Buttons = type is "pointerdown" ? 1 << (int)button : 0,
        Width = 1,
        Height = 1,
        Pressure = type is "pointerdown" ? 0.5f : 0,
        ShiftKey = (modifiers & KeyModifiers.Shift) != 0,
        CtrlKey = (modifiers & KeyModifiers.Control) != 0,
        AltKey = (modifiers & KeyModifiers.Alt) != 0,
        MetaKey = (modifiers & KeyModifiers.Meta) != 0,
    };

    private static KeyboardEventArgs Keyboard(string type, string key, KeyModifiers modifiers) => new()
    {
        Type = type,
        Key = Keys.ToDomKey(key),
        Code = Keys.ToDomCode(key),
        ShiftKey = (modifiers & KeyModifiers.Shift) != 0,
        CtrlKey = (modifiers & KeyModifiers.Control) != 0,
        AltKey = (modifiers & KeyModifiers.Alt) != 0,
        MetaKey = (modifiers & KeyModifiers.Meta) != 0,
    };
}
