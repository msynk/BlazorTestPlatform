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

    /// <summary>Loads a history file (empty when missing).</summary>
    public static FlakeHistory Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new FlakeHistory([]);
        }

        var runs = JsonSerializer.Deserialize<List<HistoryRun>>(File.ReadAllText(path), BtpJson.Options) ?? [];
        return new FlakeHistory(runs);
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
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(_runs, BtpJson.Options));
    }

    /// <summary>Computes flakiness per key.</summary>
    public FlakeReport Analyze(int minimumRuns = 2)
    {
        var entries = _runs
            .SelectMany(r => r.Outcomes)
            .Where(o => o.Value != StoryOutcome.Skipped)
            .GroupBy(o => o.Key, StringComparer.Ordinal)
            .Select(g => new FlakeEntry(g.Key, g.Count(), g.Count(o => o.Value == StoryOutcome.Failed), g.Count(o => o.Value == StoryOutcome.Flaky)))
            .Where(e => e.Runs >= minimumRuns && e.FlakeRate > 0)
            .OrderByDescending(e => e.FlakeRate)
            .ThenByDescending(e => e.Runs)
            .ToArray();
        return new FlakeReport(_runs.Count, entries);
    }
}
