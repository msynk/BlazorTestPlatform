using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorTestPlatform.Results;

/// <summary>
/// One quarantined story or result. A quarantined story still runs and still reports its outcome, but a failure no
/// longer fails the build: the result carries the entry, <c>VerifyAsync</c> returns instead of throwing, and reports
/// list it apart from real failures. Entries live in a committed <c>quarantine.json</c>, so who muted what, why and
/// until when is reviewed like code.
/// </summary>
public sealed record QuarantineEntry
{
    /// <summary>
    /// What is quarantined: a story id (<c>inputs-button--primary</c>), a result key
    /// (<c>inputs-button--primary|chromium|desktop|light|wasm</c>) or a pattern with <c>*</c> and <c>?</c>
    /// (<c>data-people-*</c>, <c>*|webkit|*</c>).
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>Why, in a sentence. Required: a mute without a reason is never revisited.</summary>
    public required string Reason { get; init; }

    /// <summary>Who owns the fix.</summary>
    public string? Owner { get; init; }

    /// <summary>Issue or ticket that tracks the fix (URL or id).</summary>
    public string? Issue { get; init; }

    /// <summary>When it was added.</summary>
    public DateTimeOffset? Added { get; init; }

    /// <summary>Last day the entry applies; after it, failures fail the build again. Null: until removed.</summary>
    public DateOnly? Expires { get; init; }

    /// <summary>Whether the entry still applies on <paramref name="today"/>.</summary>
    public bool IsActive(DateOnly today) => Expires is null || today <= Expires.Value;

    /// <summary>Whether the entry covers a result of <paramref name="storyId"/> with <paramref name="key"/>.</summary>
    public bool Matches(string storyId, string key)
    {
        ArgumentNullException.ThrowIfNull(storyId);
        ArgumentNullException.ThrowIfNull(key);
        return Glob(Pattern, storyId) || Glob(Pattern, key);
    }

    private static bool Glob(string pattern, string value) =>
        pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal)
            ? System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, value, ignoreCase: true)
            : string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The committed list of quarantined stories (<c>quarantine.json</c> next to <c>btp.json</c>).</summary>
public sealed class QuarantineList
{
    /// <summary>Default file name, next to <c>btp.json</c>.</summary>
    public const string DefaultFileName = "quarantine.json";

    private static readonly JsonSerializerOptions FileOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private QuarantineList(IReadOnlyList<QuarantineEntry> entries) => Entries = entries;

    /// <summary>No quarantined stories.</summary>
    public static QuarantineList Empty { get; } = new([]);

    /// <summary>Entries in file order.</summary>
    public IReadOnlyList<QuarantineEntry> Entries { get; }

    /// <summary>Creates a list from entries.</summary>
    public static QuarantineList From(IEnumerable<QuarantineEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToArray();
        foreach (var entry in list)
        {
            if (string.IsNullOrWhiteSpace(entry.Pattern) || string.IsNullOrWhiteSpace(entry.Reason))
            {
                throw new ArgumentException("Every quarantine entry needs a pattern and a reason.", nameof(entries));
            }
        }

        return new QuarantineList(list);
    }

    /// <summary>Loads a quarantine file; a missing file is an empty list. A malformed file throws, naming the file.</summary>
    public static QuarantineList Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(path), QuarantineJsonContext.Default.QuarantineFile);
            return From(file?.Quarantined ?? []);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"The quarantine file '{path}' is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>Saves the list (through a temporary file).</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = $"{full}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new QuarantineFile { Quarantined = [.. Entries] }, QuarantineJsonContext.Default.QuarantineFile) + Environment.NewLine);
        File.Move(temp, full, overwrite: true);
    }

    /// <summary>The active entry that covers a result, or null.</summary>
    public QuarantineEntry? Find(string storyId, string key, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return Entries.FirstOrDefault(e => e.IsActive(day) && e.Matches(storyId, key));
    }

    /// <summary>A copy with <paramref name="entry"/> added (replacing an entry with the same pattern).</summary>
    public QuarantineList With(QuarantineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return From([.. Entries.Where(e => !string.Equals(e.Pattern, entry.Pattern, StringComparison.OrdinalIgnoreCase)), entry]);
    }

    /// <summary>A copy without the entry for <paramref name="pattern"/>.</summary>
    public QuarantineList Without(string pattern) =>
        From(Entries.Where(e => !string.Equals(e.Pattern, pattern, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Marks a failed result as quarantined when an active entry covers it.</summary>
    public StoryResult Apply(StoryResult result, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome == StoryOutcome.Failed && result.Quarantine is null && Find(result.StoryId, result.Key, today) is { } entry
            ? result with { Quarantine = entry }
            : result;
    }

    /// <summary>The file's shape: <c>{ "quarantined": [ ... ] }</c>.</summary>
    internal sealed class QuarantineFile
    {
        public List<QuarantineEntry> Quarantined { get; set; } = [];
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QuarantineList.QuarantineFile))]
internal sealed partial class QuarantineJsonContext : JsonSerializerContext;
