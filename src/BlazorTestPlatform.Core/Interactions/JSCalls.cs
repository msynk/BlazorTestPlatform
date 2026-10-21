using System.Globalization;
using System.Text;
using System.Text.Json;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform;

/// <summary>A JavaScript-interop call made by the story's components.</summary>
public sealed record JSCallRecord
{
    /// <summary>Sequence number within the story.</summary>
    public int Sequence { get; init; }

    /// <summary>Function identifier (<c>localStorage.getItem</c>, or <c>import</c> for module imports).</summary>
    public required string Identifier { get; init; }

    /// <summary>The module the function was called on (its import path), or null for global functions.</summary>
    public string? Module { get; init; }

    /// <summary>Arguments, formatted with <see cref="JSCallLog.Format"/>.</summary>
    public IReadOnlyList<string?> Arguments { get; init; } = [];

    /// <summary>True when a story setup answered the call (always false for calls that reached real JavaScript).</summary>
    public bool Mocked { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        (Module is null ? string.Empty : Module + ":") + Identifier + "(" + string.Join(", ", Arguments.Select(a => a ?? "null")) + ")" + (Mocked ? " [mocked]" : string.Empty);
}

/// <summary>Thread-safe log of a story's JavaScript-interop calls, transportable across the story frame bridge.</summary>
public sealed class JSCallLog
{
    private readonly List<JSCallRecord> _calls = [];
    private readonly Lock _gate = new();

    /// <summary>Calls in order.</summary>
    public IReadOnlyList<JSCallRecord> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>Records a call.</summary>
    public JSCallRecord Add(string identifier, string? module, IReadOnlyList<object?>? arguments, bool mocked)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        lock (_gate)
        {
            var record = new JSCallRecord
            {
                Sequence = _calls.Count + 1,
                Identifier = identifier,
                Module = module,
                Arguments = (arguments ?? []).Select(Format).ToArray(),
                Mocked = mocked,
            };
            _calls.Add(record);
            return record;
        }
    }

    /// <summary>
    /// Formats an argument the same way under every driver: strings as-is, numbers and dates invariant, booleans in
    /// lower case, null as null and anything else through <see cref="object.ToString"/>.
    /// </summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Serializes the calls (reflection-free).</summary>
    public string ToJson() => ToJson(Calls);

    /// <summary>Serializes calls (reflection-free).</summary>
    public static string ToJson(IEnumerable<JSCallRecord> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var call in calls)
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequence", call.Sequence);
                writer.WriteString("identifier", call.Identifier);
                if (call.Module is not null)
                {
                    writer.WriteString("module", call.Module);
                }

                writer.WriteStartArray("arguments");
                foreach (var argument in call.Arguments)
                {
                    if (argument is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        writer.WriteStringValue(argument);
                    }
                }

                writer.WriteEndArray();
                writer.WriteBoolean("mocked", call.Mocked);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Parses <see cref="ToJson()"/> output.</summary>
    public static IReadOnlyList<JSCallRecord> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var list = new List<JSCallRecord>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var arguments = new List<string?>();
            if (item.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Array)
            {
                arguments.AddRange(a.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.GetString()));
            }

            list.Add(new JSCallRecord
            {
                Sequence = item.TryGetProperty("sequence", out var sequence) ? sequence.GetInt32() : list.Count + 1,
                Identifier = item.GetProperty("identifier").GetString() ?? string.Empty,
                Module = item.TryGetProperty("module", out var module) ? module.GetString() : null,
                Arguments = arguments,
                Mocked = item.TryGetProperty("mocked", out var mocked) && mocked.GetBoolean(),
            });
        }

        return list;
    }
}

/// <summary>Auto-waiting assertions on the story's JavaScript-interop calls.</summary>
public sealed class JSCallAssertions
{
    private readonly StoryContext _context;
    private readonly string _identifier;
    private readonly string? _module;

    internal JSCallAssertions(StoryContext context, string identifier, string? module)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        if (context.Story.JSInterop is null)
        {
            throw new InvalidOperationException(
                $"Story '{context.Story.Id}' declares no JS mocks, so its JS calls are not recorded in browsers. Add MockJS(...) to the story (an empty MockJS(_ => {{ }}) is enough) to assert on JS calls under every driver.");
        }

        _context = context;
        _identifier = identifier;
        _module = module;
    }

    /// <summary>A matching call was made (exactly <paramref name="times"/> times when given).</summary>
    public Task ToHaveBeenCalledAsync(int? times = null, TimeSpan? timeout = null) =>
        CheckAsync(times is null ? "to have been called" : $"to have been called {times} time(s)", calls => times is null ? calls.Count > 0 : calls.Count == times, timeout);

    /// <summary>No matching call was made (checked after the story settles).</summary>
    public async Task NotToHaveBeenCalledAsync()
    {
        await _context.Driver.WaitForIdleAsync(_context.CancellationToken).ConfigureAwait(false);
        await CheckAsync("not to have been called", calls => calls.Count == 0, TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <summary>The last matching call had exactly these arguments (compared after <see cref="JSCallLog.Format"/>).</summary>
    public Task ToHaveBeenLastCalledWithAsync(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var expected = arguments.Select(JSCallLog.Format).ToArray();
        return CheckAsync($"to have been last called with ({string.Join(", ", expected.Select(a => a ?? "null"))})", calls => calls.Count > 0 && calls[^1].Arguments.SequenceEqual(expected), null);
    }

    private Task CheckAsync(string description, Func<IReadOnlyList<JSCallRecord>, bool> evaluate, TimeSpan? timeout)
    {
        var target = (_module is null ? string.Empty : _module + ":") + _identifier;
        return _context.Recorder.RunAsync($"expect JS call {target} {description}", StepKind.Assertion, async () =>
        {
            var (success, observed) = await Poll.UntilAsync(
                async ct =>
                {
                    var all = await _context.Driver.GetJSCallsAsync(ct).ConfigureAwait(false);
                    var matching = all.Where(c => string.Equals(c.Identifier, _identifier, StringComparison.Ordinal) && string.Equals(c.Module, _module, StringComparison.Ordinal)).ToArray();
                    return (evaluate(matching), (Matching: matching, All: all));
                },
                timeout ?? _context.DefaultTimeout,
                _context.CancellationToken).ConfigureAwait(false);

            if (!success)
            {
                var seen = observed.All.Count == 0 ? "no calls" : string.Join(", ", observed.All.Select(c => c.ToString()));
                throw new StoryAssertionException($"Expected JS call {target} {description}.{Environment.NewLine}  Matching: {observed.Matching.Length}{Environment.NewLine}  Seen:     {seen}");
            }
        });
    }
}
