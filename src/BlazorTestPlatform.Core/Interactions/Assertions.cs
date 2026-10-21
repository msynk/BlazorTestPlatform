using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform;

/// <summary>
/// Auto-waiting assertions on a locator. Each assertion re-queries the driver until it passes or the timeout elapses,
/// which removes the most common source of flaky UI tests (asserting before async renders finish).
/// </summary>
public sealed class LocatorAssertions
{
    private readonly StoryContext _context;
    private readonly ElementLocator _locator;
    private readonly bool _negate;

    internal LocatorAssertions(StoryContext context, ElementLocator locator, bool negate)
    {
        _context = context;
        _locator = locator;
        _negate = negate;
    }

    /// <summary>Negates the next assertion.</summary>
    public LocatorAssertions Not => new(_context, _locator, !_negate);

    /// <summary>At least one element matches.</summary>
    public Task ToBeAttachedAsync(TimeSpan? timeout = null) =>
        CheckAsync("to be attached", all => (all.Count > 0, $"{all.Count} match(es)"), timeout);

    /// <summary>Exactly <paramref name="count"/> elements match.</summary>
    public Task ToHaveCountAsync(int count, TimeSpan? timeout = null) =>
        CheckAsync($"to have count {count}", all => (all.Count == count, $"{all.Count} match(es)"), timeout);

    /// <summary>The element is visible.</summary>
    public Task ToBeVisibleAsync(TimeSpan? timeout = null) =>
        CheckAsync("to be visible", all => (all.Count > 0 && all.All(e => e.IsVisible), all.Count == 0 ? "not found" : all.All(e => e.IsVisible) ? "visible" : "hidden"), timeout);

    /// <summary>The element is hidden or absent.</summary>
    public Task ToBeHiddenAsync(TimeSpan? timeout = null) =>
        CheckAsync("to be hidden", all => (all.Count == 0 || all.All(e => !e.IsVisible), all.Count == 0 ? "not found" : all.All(e => !e.IsVisible) ? "hidden" : "visible"), timeout);

    /// <summary>Whole text equals the expected text (whitespace-normalized).</summary>
    public Task ToHaveTextAsync(string expected, TimeSpan? timeout = null) =>
        Single($"to have text \"{expected}\"", e => (TextMatch.Matches(e.Text, expected, exact: true), Quote(e.Text)), timeout);

    /// <summary>Whole text matches the pattern.</summary>
    public Task ToHaveTextAsync(Regex pattern, TimeSpan? timeout = null) =>
        Single($"to have text matching /{pattern}/", e => (pattern.IsMatch(e.Text), Quote(e.Text)), timeout);

    /// <summary>Text contains the expected text (case-insensitive).</summary>
    public Task ToContainTextAsync(string expected, TimeSpan? timeout = null) =>
        Single($"to contain text \"{expected}\"", e => (TextMatch.Matches(e.Text, expected, exact: false), Quote(e.Text)), timeout);

    /// <summary>The control's value equals the expected value.</summary>
    public Task ToHaveValueAsync(string expected, TimeSpan? timeout = null) =>
        Single($"to have value \"{expected}\"", e => (string.Equals(e.Value ?? string.Empty, expected, StringComparison.Ordinal), Quote(e.Value)), timeout);

    /// <summary>The attribute exists (and equals <paramref name="value"/> when given).</summary>
    public Task ToHaveAttributeAsync(string name, string? value = null, TimeSpan? timeout = null) =>
        Single(value is null ? $"to have attribute {name}" : $"to have attribute {name}=\"{value}\"", e =>
        {
            var actual = e.GetAttribute(name);
            return (actual is not null && (value is null || string.Equals(actual, value, StringComparison.Ordinal)), actual is null ? "(absent)" : Quote(actual));
        }, timeout);

    /// <summary>The class attribute contains the token.</summary>
    public Task ToHaveClassAsync(string className, TimeSpan? timeout = null) =>
        Single($"to have class \"{className}\"", e => (e.HasClass(className), Quote(e.GetAttribute("class"))), timeout);

    /// <summary>The element has focus.</summary>
    public Task ToBeFocusedAsync(TimeSpan? timeout = null) =>
        Single("to be focused", e => (e.IsFocused, e.IsFocused ? "focused" : "not focused"), timeout, describeFocus: true);

    /// <summary>The element is enabled.</summary>
    public Task ToBeEnabledAsync(TimeSpan? timeout = null) =>
        Single("to be enabled", e => (e.IsEnabled, e.IsEnabled ? "enabled" : "disabled"), timeout);

    /// <summary>The element is disabled.</summary>
    public Task ToBeDisabledAsync(TimeSpan? timeout = null) =>
        Single("to be disabled", e => (!e.IsEnabled, e.IsEnabled ? "enabled" : "disabled"), timeout);

    /// <summary>The element is checked (checkbox, radio, switch, <c>aria-checked</c>, <c>aria-pressed</c>).</summary>
    public Task ToBeCheckedAsync(TimeSpan? timeout = null) =>
        Single("to be checked", e => (e.IsChecked == true, e.IsChecked switch { true => "checked", false => "unchecked", null => "not checkable" }), timeout);

    /// <summary>The element accepts text input.</summary>
    public Task ToBeEditableAsync(TimeSpan? timeout = null) =>
        Single("to be editable", e => (e.IsEditable, e.IsEditable ? "editable" : "read-only"), timeout);

    /// <summary>The computed accessible name equals the expected name.</summary>
    public Task ToHaveAccessibleNameAsync(string expected, TimeSpan? timeout = null) =>
        Single($"to have accessible name \"{expected}\"", e => (TextMatch.Matches(e.AccessibleName, expected, exact: true), Quote(e.AccessibleName)), timeout);

    /// <summary>The computed ARIA role equals the expected role.</summary>
    public Task ToHaveRoleAsync(string role, TimeSpan? timeout = null) =>
        Single($"to have role \"{role}\"", e => (string.Equals(e.Role, role, StringComparison.OrdinalIgnoreCase), Quote(e.Role)), timeout);

    private Task Single(string description, Func<ElementInfo, (bool Pass, string Actual)> evaluate, TimeSpan? timeout, bool describeFocus = false) =>
        CheckAsync(description, all => all.Count switch
        {
            0 => (false, "element not found"),
            1 => evaluate(all[0]),
            _ => (false, $"strict mode violation: {all.Count} elements match"),
        }, timeout, describeFocus, single: true);

    private Task CheckAsync(string description, Func<IReadOnlyList<ElementInfo>, (bool Pass, string Actual)> evaluate, TimeSpan? timeout, bool describeFocus = false, bool single = false)
    {
        var effectiveTimeout = timeout ?? _context.DefaultTimeout;
        var title = $"expect {_locator} {(_negate ? "not " : string.Empty)}{description}";
        return _context.Recorder.RunAsync(title, StepKind.Assertion, async () =>
        {
            var (success, result) = await Poll.UntilAsync(
                async ct =>
                {
                    var all = await _context.Driver.QueryAsync(_locator.Chain, ct).ConfigureAwait(false);
                    var (pass, actual) = evaluate(all);
                    var ok = pass != _negate;

                    // A negated single-element assertion must not pass merely because the element is missing or ambiguous.
                    if (_negate && single && all.Count != 1 && description.StartsWith("to have", StringComparison.Ordinal))
                    {
                        ok = false;
                    }

                    return (ok, (all, actual));
                },
                effectiveTimeout,
                _context.CancellationToken).ConfigureAwait(false);

            if (success)
            {
                return;
            }

            var message = new StringBuilder()
                .Append(CultureInfo.InvariantCulture, $"Expected {_locator} {(_negate ? "not " : string.Empty)}{description}")
                .AppendLine()
                .Append(CultureInfo.InvariantCulture, $"  Actual:   {result.actual}")
                .AppendLine()
                .Append(CultureInfo.InvariantCulture, $"  Driver:   {_context.DriverName} ({_context.HostingModel.ToToken()}), waited {effectiveTimeout.TotalMilliseconds:0} ms");

            foreach (var element in result.all.Take(3))
            {
                message.AppendLine().Append("  Element:  ").Append(element.Html);
            }

            if (describeFocus)
            {
                var focused = await _context.Driver.GetFocusedElementAsync(_context.CancellationToken).ConfigureAwait(false);
                message.AppendLine().Append("  Focused:  ").Append(focused?.Html ?? "(document body)");
            }

            throw new StoryAssertionException(message.ToString());
        });
    }

    private static string Quote(string? value) => value is null ? "(null)" : $"\"{value}\"";
}

/// <summary>Auto-waiting assertions on recorded component callbacks.</summary>
public sealed class ActionAssertions
{
    private readonly StoryContext _context;
    private readonly string _name;

    internal ActionAssertions(StoryContext context, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _context = context;
        _name = name;
    }

    /// <summary>The callback was invoked (exactly <paramref name="times"/> times when given).</summary>
    public Task ToHaveBeenCalledAsync(int? times = null, TimeSpan? timeout = null) =>
        CheckAsync(times is null ? "to have been called" : $"to have been called {times} time(s)", calls => times is null ? calls.Count > 0 : calls.Count == times, timeout);

    /// <summary>The callback was never invoked (checked after a short settle period).</summary>
    public async Task NotToHaveBeenCalledAsync()
    {
        await _context.Driver.WaitForIdleAsync(_context.CancellationToken).ConfigureAwait(false);
        await CheckAsync("not to have been called", calls => calls.Count == 0, TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <summary>The last invocation's argument satisfies the predicate.</summary>
    public Task ToHaveBeenLastCalledWithAsync<T>(Func<T?, bool> predicate, string? description = null, TimeSpan? timeout = null) =>
        CheckAsync($"to have been last called with {description ?? "matching argument"}", calls => calls.Count > 0 && predicate(calls[^1].ArgumentAs<T>()), timeout);

    /// <summary>The last invocation's argument equals <paramref name="expected"/> (JSON equality).</summary>
    public Task ToHaveBeenLastCalledWithAsync<T>(T expected, TimeSpan? timeout = null)
    {
        var expectedJson = JsonSerializer.Serialize(expected, ActionLog.SerializerOptions.TypeInfo<T>());
        return CheckAsync($"to have been last called with {expectedJson}", calls => calls.Count > 0 && calls[^1].ArgumentJson == expectedJson, timeout);
    }

    private Task CheckAsync(string description, Func<IReadOnlyList<ActionRecord>, bool> evaluate, TimeSpan? timeout)
    {
        var effectiveTimeout = timeout ?? _context.DefaultTimeout;
        return _context.Recorder.RunAsync($"expect action {_name} {description}", StepKind.Assertion, async () =>
        {
            var (success, calls) = await Poll.UntilAsync(
                async ct =>
                {
                    var all = await _context.Driver.GetActionsAsync(ct).ConfigureAwait(false);
                    var matching = all.Where(r => string.Equals(r.Name, _name, StringComparison.Ordinal)).ToArray();
                    return (evaluate(matching), (IReadOnlyList<ActionRecord>)matching);
                },
                effectiveTimeout,
                _context.CancellationToken).ConfigureAwait(false);

            if (!success)
            {
                var detail = calls.Count == 0
                    ? "never called"
                    : string.Join(", ", calls.Select(c => c.ArgumentJson ?? "()"));
                throw new StoryAssertionException($"Expected action '{_name}' {description}.{Environment.NewLine}  Actual: {calls.Count} call(s): {detail}");
            }
        });
    }
}


/// <summary>Auto-waiting assertions on requests answered by the story's <see cref="HttpMock"/>.</summary>
public sealed class HttpRequestAssertions
{
    private readonly StoryContext _context;
    private readonly string? _method;
    private readonly string _pattern;

    internal HttpRequestAssertions(StoryContext context, string? method, string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        _context = context;
        _method = method;
        _pattern = pattern;
    }

    /// <summary>A matching request was made (exactly <paramref name="times"/> times when given).</summary>
    public Task ToHaveBeenMadeAsync(int? times = null, TimeSpan? timeout = null) =>
        CheckAsync(times is null ? "to have been made" : $"to have been made {times} time(s)", requests => times is null ? requests.Count > 0 : requests.Count == times, timeout);

    /// <summary>No matching request was made (checked after the story settles).</summary>
    public async Task NotToHaveBeenMadeAsync()
    {
        await _context.Driver.WaitForIdleAsync(_context.CancellationToken).ConfigureAwait(false);
        await CheckAsync("not to have been made", requests => requests.Count == 0, TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <summary>The last matching request satisfies the predicate (body, headers...).</summary>
    public Task ToHaveBeenLastMadeWithAsync(Func<HttpRequestRecord, bool> predicate, string? description = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return CheckAsync($"to have been last made with {description ?? "a matching request"}", requests => requests.Count > 0 && predicate(requests[^1]), timeout);
    }

    private Task CheckAsync(string description, Func<IReadOnlyList<HttpRequestRecord>, bool> evaluate, TimeSpan? timeout)
    {
        var target = $"{_method ?? "*"} {_pattern}";
        return _context.Recorder.RunAsync($"expect request {target} {description}", StepKind.Assertion, async () =>
        {
            var (success, observed) = await Poll.UntilAsync(
                async ct =>
                {
                    var all = await _context.Driver.GetHttpRequestsAsync(ct).ConfigureAwait(false);
                    var matching = all.Where(r => HttpMock.Matches(r, _method, _pattern)).ToArray();
                    return (evaluate(matching), (Matching: matching, All: all));
                },
                timeout ?? _context.DefaultTimeout,
                _context.CancellationToken).ConfigureAwait(false);

            if (!success)
            {
                var seen = observed.All.Count == 0 ? "no requests" : string.Join(", ", observed.All.Select(r => r.ToString()));
                throw new StoryAssertionException($"Expected request {target} {description}.{Environment.NewLine}  Matching: {observed.Matching.Length}{Environment.NewLine}  Seen:     {seen}");
            }
        });
    }
}
