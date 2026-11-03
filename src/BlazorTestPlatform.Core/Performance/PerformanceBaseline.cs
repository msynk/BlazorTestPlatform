using System.Globalization;
using System.Text.Json;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Performance;

/// <summary>Recorded metrics for one story, committed to source control.</summary>
public sealed record PerformanceBaselineEntry
{
    /// <summary>Baseline root renders.</summary>
    public int RootRenderCount { get; init; }

    /// <summary>Baseline total renders.</summary>
    public int TotalRenderCount { get; init; }

    /// <summary>Baseline allocations.</summary>
    public long AllocatedBytes { get; init; }

    /// <summary>Baseline median duration.</summary>
    public double MedianMilliseconds { get; init; }

    /// <summary>When the baseline was recorded.</summary>
    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>Machine/runtime description at recording time.</summary>
    public string? Environment { get; init; }

    /// <summary>Creates an entry from measured metrics.</summary>
    public static PerformanceBaselineEntry From(RenderMetrics metrics, TimeProvider? time = null) => new()
    {
        RootRenderCount = metrics.RootRenderCount,
        TotalRenderCount = metrics.TotalRenderCount,
        AllocatedBytes = metrics.AllocatedBytes,
        MedianMilliseconds = Math.Round(metrics.MedianMilliseconds, 3),
        RecordedAt = (time ?? TimeProvider.System).GetUtcNow(),
        Environment = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
    };
}

/// <summary>
/// A JSON file of <see cref="PerformanceBaselineEntry"/> keyed by story id. Safe for parallel tests: writers in one
/// process share a lock per file, writers in different processes (parallel test projects) take a lock file, and every
/// write merges into what is on disk at that moment, so no recorded entry is lost.
/// </summary>
public sealed class PerformanceBaselineStore
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lock> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan FileLockTimeout = TimeSpan.FromSeconds(30);

    private readonly Lock _gate;
    private SortedDictionary<string, PerformanceBaselineEntry>? _entries;

    /// <summary>Creates a store backed by <paramref name="path"/>.</summary>
    public PerformanceBaselineStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);
        _gate = Gates.GetOrAdd(FilePath, _ => new Lock());
    }

    /// <summary>The baseline file.</summary>
    public string FilePath { get; }

    /// <summary>All entries.</summary>
    public IReadOnlyDictionary<string, PerformanceBaselineEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, PerformanceBaselineEntry>(Load());
            }
        }
    }

    /// <summary>Gets the baseline for a story.</summary>
    public PerformanceBaselineEntry? Get(string storyId)
    {
        lock (_gate)
        {
            return Load().GetValueOrDefault(storyId);
        }
    }

    /// <summary>Records (and persists) the baseline for a story.</summary>
    public void Set(string storyId, PerformanceBaselineEntry entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(storyId);
        ArgumentNullException.ThrowIfNull(entry);
        Update(entries =>
        {
            entries[storyId] = entry;
            return true;
        });
    }

    /// <summary>Removes baselines whose story no longer exists. Returns the removed ids.</summary>
    public IReadOnlyList<string> Prune(IEnumerable<string> liveStoryIds)
    {
        var live = liveStoryIds.ToHashSet(StringComparer.Ordinal);
        List<string> removed = [];
        Update(entries =>
        {
            removed = entries.Keys.Where(k => !live.Contains(k)).ToList();
            foreach (var id in removed)
            {
                entries.Remove(id);
            }

            return removed.Count > 0;
        });
        return removed;
    }

    /// <summary>Read-modify-write under both locks, starting from the file as it is now (other writers included).</summary>
    private void Update(Func<SortedDictionary<string, PerformanceBaselineEntry>, bool> change)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using var fileLock = AcquireFileLock();
            var entries = Read();
            if (change(entries))
            {
                Save(entries);
            }

            _entries = entries;
        }
    }

    private FileStream AcquireFileLock()
    {
        var path = FilePath + ".lock";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (clock.Elapsed < FileLockTimeout)
            {
                Thread.Sleep(25);
            }
        }
    }

    private SortedDictionary<string, PerformanceBaselineEntry> Load() => _entries ??= Read();

    private SortedDictionary<string, PerformanceBaselineEntry> Read()
    {
        // Parsed into a local first: a corrupt file throws without leaving an empty cache that a later Set would save.
        var entries = new SortedDictionary<string, PerformanceBaselineEntry>(StringComparer.Ordinal);
        if (File.Exists(FilePath))
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = BtpJson.Deserialize<PerformanceBaselineDocument>(stream);
            if (document?.Stories is not null)
            {
                foreach (var pair in document.Stories)
                {
                    entries[pair.Key] = pair.Value;
                }
            }
        }

        return entries;
    }

    private void Save(SortedDictionary<string, PerformanceBaselineEntry> entries)
    {
        var document = new PerformanceBaselineDocument { Stories = new SortedDictionary<string, PerformanceBaselineEntry>(entries, StringComparer.Ordinal) };
        var temp = $"{FilePath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, BtpJson.Serialize(document));
        File.Move(temp, FilePath, overwrite: true);
    }

    internal sealed class PerformanceBaselineDocument
    {
        public string Schema { get; init; } = "https://blazortestplatform.dev/schemas/performance-baseline-v1.json";

        public SortedDictionary<string, PerformanceBaselineEntry>? Stories { get; init; }
    }
}

/// <summary>Regression tolerances. Render counts are deterministic and compared exactly by default.</summary>
public sealed class PerformanceTolerance
{
    /// <summary>Allowed relative render-count increase (default 0: any extra render is a regression).</summary>
    public double RenderCountRatio { get; set; }

    /// <summary>Allowed relative allocation increase (default 10%).</summary>
    public double AllocationRatio { get; set; } = 0.10;

    /// <summary>Absolute allocation slack in bytes, absorbing runtime noise on tiny components (default 4 KiB).</summary>
    public long AllocationSlackBytes { get; set; } = 4096;

    /// <summary>Allowed relative duration increase (default 50%; timing is noisy).</summary>
    public double DurationRatio { get; set; } = 0.5;

    /// <summary>Absolute duration slack in milliseconds (default 5 ms).</summary>
    public double DurationSlackMilliseconds { get; set; } = 5;

    /// <summary>Fail on render-count regressions (default true).</summary>
    public bool FailOnRenderRegression { get; set; } = true;

    /// <summary>Fail on allocation regressions (default true).</summary>
    public bool FailOnAllocationRegression { get; set; } = true;

    /// <summary>Fail on duration regressions (default false: reported as a warning; shared CI agents are noisy).</summary>
    public bool FailOnDurationRegression { get; set; }
}

/// <summary>Change of one metric relative to the baseline.</summary>
public sealed record MetricChange(string Metric, double Baseline, double Current, bool IsRegression, bool IsImprovement, bool FailsBuild)
{
    /// <summary>Relative change (0.25 = +25%; infinite when the baseline is zero).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double ChangeRatio => Baseline == 0 ? (Current == 0 ? 0 : double.PositiveInfinity) : (Current - Baseline) / Baseline;

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Metric}: {Baseline:N0} -> {Current:N0} ({ChangeRatio:+0.#%;-0.#%;0%}){(FailsBuild ? " [regression]" : IsRegression ? " [warning]" : string.Empty)}");
}

/// <summary>Result of comparing metrics against a baseline.</summary>
public sealed record PerformanceComparison
{
    /// <summary>True when no baseline existed.</summary>
    public bool BaselineMissing { get; init; }

    /// <summary>Per-metric changes.</summary>
    public IReadOnlyList<MetricChange> Changes { get; init; } = [];

    /// <summary>Changes that fail the build.</summary>
    public IEnumerable<MetricChange> Failures => Changes.Where(c => c.FailsBuild);

    /// <summary>True when any change fails the build.</summary>
    public bool HasFailures => Changes.Any(c => c.FailsBuild);
}

/// <summary>Compares metrics to a baseline using <see cref="PerformanceTolerance"/>.</summary>
public static class PerformanceComparer
{
    /// <summary>Compares current metrics to a baseline entry.</summary>
    public static PerformanceComparison Compare(RenderMetrics current, PerformanceBaselineEntry? baseline, PerformanceTolerance? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        tolerance ??= new PerformanceTolerance();
        if (baseline is null)
        {
            return new PerformanceComparison { BaselineMissing = true };
        }

        var changes = new List<MetricChange>
        {
            Evaluate("Root renders", baseline.RootRenderCount, current.RootRenderCount, tolerance.RenderCountRatio, 0, tolerance.FailOnRenderRegression),
            Evaluate("Total renders", baseline.TotalRenderCount, current.TotalRenderCount, tolerance.RenderCountRatio, 0, tolerance.FailOnRenderRegression),
            Evaluate("Allocated bytes", baseline.AllocatedBytes, current.AllocatedBytes, tolerance.AllocationRatio, tolerance.AllocationSlackBytes, tolerance.FailOnAllocationRegression),
            Evaluate("Median ms", baseline.MedianMilliseconds, current.MedianMilliseconds, tolerance.DurationRatio, tolerance.DurationSlackMilliseconds, tolerance.FailOnDurationRegression),
        };

        return new PerformanceComparison { Changes = changes };
    }

    private static MetricChange Evaluate(string metric, double baseline, double current, double ratio, double slack, bool fail)
    {
        var limit = baseline * (1 + ratio) + slack;
        var regression = current > limit;
        var improvement = current < baseline - (baseline * ratio + slack);
        return new MetricChange(metric, baseline, current, regression, improvement, regression && fail);
    }
}
