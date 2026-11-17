using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform;

/// <summary>A navigation a story's component asked for (<c>NavigationManager.NavigateTo</c> or an intercepted link click).</summary>
/// <param name="Sequence">Order of the navigation within the story (1-based).</param>
/// <param name="Uri">Absolute target.</param>
/// <param name="RelativeUri">Target relative to the app's base URI (no leading slash), as patterns match it.</param>
public sealed record NavigationRecord(int Sequence, string Uri, string RelativeUri)
{
    /// <summary>Whether the navigation replaces the history entry (known in bUnit; null when the driver cannot tell).</summary>
    public bool? ReplaceHistoryEntry { get; init; }

    /// <summary>Whether the navigation forces a full page load (known in bUnit; null when the driver cannot tell).</summary>
    public bool? ForceLoad { get; init; }

    /// <summary>True for a link click Blazor intercepted, false for <c>NavigateTo</c> (null when the driver cannot tell).</summary>
    public bool? IsLinkClick { get; init; }

    /// <inheritdoc />
    public override string ToString() => "/" + RelativeUri;
}

/// <summary>
/// The navigations of a story, as story hosts record them. In browsers the workbench records every navigation and stops
/// those that would leave the story frame (another page), so a component under test cannot navigate its story away;
/// changes of the query string or fragment still happen.
/// </summary>
public sealed class NavigationLog
{
    private readonly List<NavigationRecord> _records = [];
    private readonly Lock _gate = new();

    /// <summary>Recorded navigations, in order.</summary>
    public IReadOnlyList<NavigationRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    /// <summary>Records a navigation to <paramref name="uri"/> (absolute) given the app's <paramref name="baseUri"/>.</summary>
    public NavigationRecord Add(string uri, string baseUri, bool? replaceHistoryEntry = null, bool? forceLoad = null, bool? isLinkClick = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(baseUri);
        lock (_gate)
        {
            var record = new NavigationRecord(_records.Count + 1, uri, Relative(uri, baseUri))
            {
                ReplaceHistoryEntry = replaceHistoryEntry,
                ForceLoad = forceLoad,
                IsLinkClick = isLinkClick,
            };
            _records.Add(record);
            return record;
        }
    }

    /// <summary>The target relative to the base URI, without a leading slash (absolute when it is on another origin).</summary>
    public static string Relative(string uri, string baseUri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(baseUri);
        return uri.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase)
            ? uri[baseUri.Length..].TrimStart('/')
            : uri.StartsWith('/') ? uri.TrimStart('/') : uri;
    }

    /// <summary>
    /// True when <paramref name="record"/> goes where <paramref name="pattern"/> says: a path relative to the app
    /// (<c>/orders/42</c>, with <c>*</c> wildcards and the query compared only when the pattern has one), or an absolute
    /// URL (the same rules as <see cref="HttpMock"/> request patterns).
    /// </summary>
    public static bool Matches(NavigationRecord record, string pattern)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var fragment = record.RelativeUri.IndexOf('#', StringComparison.Ordinal);
        var relative = pattern.Contains('#', StringComparison.Ordinal) || fragment < 0 ? record.RelativeUri : record.RelativeUri[..fragment];
        return HttpMock.UrlMatches(record.Uri, relative, pattern);
    }

    /// <summary>Serializes records for the story frame bridge (reflection-free).</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var record in Records)
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequence", record.Sequence);
                writer.WriteString("uri", record.Uri);
                writer.WriteString("relativeUri", record.RelativeUri);
                Optional(writer, "replaceHistoryEntry", record.ReplaceHistoryEntry);
                Optional(writer, "forceLoad", record.ForceLoad);
                Optional(writer, "isLinkClick", record.IsLinkClick);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        static void Optional(Utf8JsonWriter writer, string name, bool? value)
        {
            if (value is { } set)
            {
                writer.WriteBoolean(name, set);
            }
        }
    }

    /// <summary>Parses <see cref="ToJson"/>.</summary>
    public static IReadOnlyList<NavigationRecord> ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(item => new NavigationRecord(
            item.GetProperty("sequence").GetInt32(),
            item.GetProperty("uri").GetString() ?? string.Empty,
            item.GetProperty("relativeUri").GetString() ?? string.Empty)
        {
            ReplaceHistoryEntry = Optional(item, "replaceHistoryEntry"),
            ForceLoad = Optional(item, "forceLoad"),
            IsLinkClick = Optional(item, "isLinkClick"),
        }).ToArray();

        static bool? Optional(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }
}

/// <summary>Auto-waiting assertions on the navigations a story asked for (<see cref="StoryContext.ExpectNavigation(string)"/>).</summary>
public sealed class NavigationAssertions
{
    private readonly StoryContext _context;
    private readonly string _description;
    private readonly Func<NavigationRecord, bool> _matches;

    internal NavigationAssertions(StoryContext context, string description, Func<NavigationRecord, bool> matches)
    {
        _context = context;
        _description = description;
        _matches = matches;
    }

    /// <summary>
    /// Waits until a matching navigation was requested (exactly <paramref name="times"/> when given; 0 waits for the
    /// story to settle and then requires none).
    /// </summary>
    public Task ToHaveNavigatedAsync(int? times = null, TimeSpan? timeout = null)
    {
        if (times is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(times), times, "Cannot be negative.");
        }

        return times == 0
            ? NotToHaveNavigatedAsync()
            : CheckAsync(times is null ? "to have been requested" : $"to have been requested {times} time(s)", matching => times is null ? matching.Count > 0 : matching.Count == times, timeout);
    }

    /// <summary>Waits for the story to settle, then requires no matching navigation.</summary>
    public async Task NotToHaveNavigatedAsync()
    {
        await _context.Driver.WaitForIdleAsync(_context.CancellationToken).ConfigureAwait(false);
        await CheckAsync("not to have been requested", matching => matching.Count == 0, TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <summary>Requires the story's last navigation to be a matching one.</summary>
    public Task ToBeTheLastNavigationAsync(TimeSpan? timeout = null) =>
        CheckAsync("to be the last navigation", (_, all) => all.Count > 0 && _matches(all[^1]), timeout);

    private Task CheckAsync(string description, Func<IReadOnlyList<NavigationRecord>, bool> evaluate, TimeSpan? timeout) =>
        CheckAsync(description, (matching, _) => evaluate(matching), timeout);

    private Task CheckAsync(string description, Func<IReadOnlyList<NavigationRecord>, IReadOnlyList<NavigationRecord>, bool> evaluate, TimeSpan? timeout) =>
        _context.Recorder.RunAsync($"expect navigation to {_description} {description}", StepKind.Assertion, async () =>
        {
            var (success, all) = await Poll.UntilAsync(
                async ct =>
                {
                    var records = await _context.Driver.GetNavigationsAsync(ct).ConfigureAwait(false);
                    return (evaluate(records.Where(_matches).ToArray(), records), records);
                },
                _context.AssertionTimeout(timeout),
                _context.CancellationToken).ConfigureAwait(false);

            if (!success)
            {
                var seen = all.Count == 0 ? "no navigations" : string.Join(", ", all.Select(r => r.ToString()));
                throw new StoryAssertionException($"Expected navigation to {_description} {description}.{Environment.NewLine}  Seen: {seen}");
            }
        });

    internal static NavigationAssertions For(StoryContext context, string pattern) =>
        new(context, pattern, record => NavigationLog.Matches(record, pattern));

    internal static NavigationAssertions For(StoryContext context, Regex pattern) =>
        new(context, $"/{pattern}/", record => pattern.IsMatch("/" + record.RelativeUri));
}
