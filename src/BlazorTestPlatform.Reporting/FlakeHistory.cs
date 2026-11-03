using System.Text.Json;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Outcomes of one run, keyed by result key.</summary>
public sealed record HistoryRun
{
    /// <summary>Run identifier (CI run id, commit, timestamp...).</summary>
    public required string RunId { get; init; }

    /// <summary>When the run happened.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Outcome per result key.</summary>
    public Dictionary<string, StoryOutcome> Outcomes { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Flakiness of one result key across runs.</summary>
/// <param name="Key">Result key.</param>
/// <param name="Runs">Runs in which it executed.</param>
/// <param name="Failures">Runs where it failed.</param>
/// <param name="Flaky">Runs where it passed only after a retry.</param>
public sealed record FlakeEntry(string Key, int Runs, int Failures, int Flaky)
{
    /// <summary>Runs that were not a clean pass.</summary>
    public int Unstable => Failures + Flaky;

    /// <summary>Share of unstable runs. Keys that failed every run are broken, not flaky, and get rate 0.</summary>
    public double FlakeRate => Runs == 0 || Failures == Runs ? 0 : (double)Unstable / Runs;

    /// <summary>
    /// How often the outcome switched between failed and passing from one run to the next. A story that regressed and
    /// was then fixed flips twice however long it stayed broken; an intermittent one flips again and again. Ties in
    /// <see cref="FlakeRate"/> are ranked by it.
    /// </summary>
    public int Flips { get; init; }
}

/// <summary>Flakiness over a window of runs.</summary>
/// <param name="Runs">Runs analysed.</param>
/// <param name="Entries">Unstable keys, flakiest first.</param>
public sealed record FlakeReport(int Runs, IReadOnlyList<FlakeEntry> Entries);

/// <summary>
/// Keeps a rolling history of outcomes (commit it to a cache or artifact store in CI) so stories that fail intermittently
/// are identified by rate, not by anecdote.
/// </summary>
public sealed class FlakeHistory
{
    /// <summary>Default number of runs kept.</summary>
    public const int DefaultCapacity = 50;

    private readonly List<HistoryRun> _runs;

    private FlakeHistory(List<HistoryRun> runs) => _runs = runs;

    /// <summary>Runs, oldest first.</summary>
    public IReadOnlyList<HistoryRun> Runs => _runs;

    /// <summary>Loads a history file (empty when missing or unreadable).</summary>
    public static FlakeHistory Load(string path) => Load(path, out _);

    /// <summary>
    /// Loads a history file. A missing file gives an empty history; so does a corrupt one (a cancelled job can leave a
    /// truncated file in a CI cache), with the reason in <paramref name="problem"/>, so one bad cache entry never breaks
    /// every later report.
    /// </summary>
    public static FlakeHistory Load(string path, out string? problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        problem = null;
        if (!File.Exists(path))
        {
            return new FlakeHistory([]);
        }

        try
        {
            var runs = JsonSerializer.Deserialize<List<HistoryRun>>(File.ReadAllText(path), BtpJson.Options) ?? [];
            return new FlakeHistory(runs.Where(r => r is not null && !string.IsNullOrEmpty(r.RunId)).ToList());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            problem = $"The flake history '{path}' is unreadable and was reset: {ex.Message}";
            return new FlakeHistory([]);
        }
    }

    /// <summary>Appends a run (keeping the newest <paramref name="capacity"/> runs).</summary>
    public void Add(string runId, IEnumerable<StoryResult> results, DateTimeOffset? timestamp = null, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(results);
        var run = new HistoryRun { RunId = runId, Timestamp = timestamp ?? DateTimeOffset.UtcNow };
        foreach (var result in results)
        {
            run.Outcomes[result.Key] = result.Outcome;
        }

        _runs.RemoveAll(r => r.RunId == runId);
        _runs.Add(run);
        if (_runs.Count > capacity)
        {
            _runs.RemoveRange(0, _runs.Count - capacity);
        }
    }

    /// <summary>Saves the history.</summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // Through a temporary file: a cancelled job must not leave a truncated history behind.
        var temp = $"{full}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_runs, BtpJson.Options));
        File.Move(temp, full, overwrite: true);
    }

    /// <summary>Computes flakiness per key.</summary>
    public FlakeReport Analyze(int minimumRuns = 2)
    {
        var entries = _runs
            .SelectMany(r => r.Outcomes)
            .Where(o => o.Value != StoryOutcome.Skipped)
            .GroupBy(o => o.Key, StringComparer.Ordinal)
            .Select(g => new FlakeEntry(g.Key, g.Count(), g.Count(o => o.Value == StoryOutcome.Failed), g.Count(o => o.Value == StoryOutcome.Flaky))
            {
                Flips = g.Zip(g.Skip(1), (previous, next) => (previous.Value == StoryOutcome.Failed) != (next.Value == StoryOutcome.Failed)).Count(flipped => flipped),
            })
            .Where(e => e.Runs >= minimumRuns && e.FlakeRate > 0)
            .OrderByDescending(e => e.FlakeRate)
            .ThenByDescending(e => e.Flips)
            .ThenByDescending(e => e.Runs)
            .ToArray();
        return new FlakeReport(_runs.Count, entries);
    }
}
