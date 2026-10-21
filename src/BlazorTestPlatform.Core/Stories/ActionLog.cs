using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform;

/// <summary>One recorded invocation of a component callback (an "action").</summary>
public sealed record ActionRecord
{
    /// <summary>Callback/parameter name, e.g. <c>OnClick</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Monotonic sequence number within the story.</summary>
    public int Sequence { get; init; }

    /// <summary>The argument serialized as JSON (or its <c>ToString()</c> when not serializable).</summary>
    public string? ArgumentJson { get; init; }

    /// <summary>The live argument object. Only available when the driver runs in-process (bUnit).</summary>
    [JsonIgnore]
    public object? Argument { get; init; }

    /// <summary>Deserializes the argument.</summary>
    public T? ArgumentAs<T>()
    {
        if (Argument is T typed)
        {
            return typed;
        }

        return ArgumentJson is null ? default : JsonSerializer.Deserialize(ArgumentJson, ActionLog.SerializerOptions.TypeInfo<T>());
    }
}

/// <summary>
/// Records invocations of component callbacks (<c>EventCallback</c> parameters) during a story.
/// Play functions assert against it with <c>ctx.ExpectAction("OnClick")</c>, uniformly across drivers.
/// </summary>
public sealed class ActionLog
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 8,
        TypeInfoResolver = JsonResolvers.Create(ActionJsonContext.Default),
    };

    private readonly Lock _gate = new();
    private readonly List<ActionRecord> _records = [];

    /// <summary>Raised after an action is recorded.</summary>
    public event Action<ActionRecord>? Recorded;

    /// <summary>A snapshot of all records in invocation order.</summary>
    public IReadOnlyList<ActionRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    /// <summary>Records for one action name.</summary>
    public IReadOnlyList<ActionRecord> For(string name) =>
        Records.Where(r => string.Equals(r.Name, name, StringComparison.Ordinal)).ToArray();

    /// <summary>Records an invocation.</summary>
    public ActionRecord Record(string name, object? argument)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ActionRecord record;
        lock (_gate)
        {
            record = new ActionRecord
            {
                Name = name,
                Sequence = _records.Count + 1,
                Argument = argument,
                ArgumentJson = Serialize(argument),
            };
            _records.Add(record);
        }

        Recorded?.Invoke(record);
        return record;
    }

    /// <summary>Clears all records.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
        }
    }

    /// <summary>Serializes the log for transport (e.g. from the browser to the test process).</summary>
    public string ToJson() => JsonSerializer.Serialize(Records.ToList(), SerializerOptions.TypeInfo<List<ActionRecord>>());

    /// <summary>Parses a log produced by <see cref="ToJson"/>.</summary>
    public static IReadOnlyList<ActionRecord> ParseJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize(json, SerializerOptions.TypeInfo<List<ActionRecord>>()) ?? [];

    private static string? Serialize(object? argument)
    {
        if (argument is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(argument, SerializerOptions.GetTypeInfo(argument.GetType()));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or JsonException)
        {
            // Types without metadata (trimmed apps) are recorded by their string form.
            return JsonSerializer.Serialize(argument.ToString() ?? string.Empty, SerializerOptions.TypeInfo<string>());
        }
    }
}
