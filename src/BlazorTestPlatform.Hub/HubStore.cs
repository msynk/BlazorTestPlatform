using System.Text.Json;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Coverage;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Hub;

/// <summary>A review decision on a visual change that was not accepted.</summary>
/// <param name="Snapshot">Snapshot name.</param>
/// <param name="Fingerprint">Diff fingerprint the decision applies to.</param>
/// <param name="Decision"><c>rejected</c>.</param>
/// <param name="At">When.</param>
/// <param name="By">Who (machine user).</param>
public sealed record VisualDecision(string Snapshot, string? Fingerprint, string Decision, DateTimeOffset At, string? By);

/// <summary>Everything the hub shows, read from disk at one point in time.</summary>
public sealed class HubSnapshot
{
    private IReadOnlyList<KeyStability>? _stability;
    private IReadOnlyList<ErrorCluster>? _clusters;
    private Dictionary<string, StoryResult>? _byKey;
    private Dictionary<string, KeyStability>? _stabilityByKey;

    internal HubSnapshot(
        BtpSettings settings,
        IReadOnlyList<StoryResult> results,
        IReadOnlyList<string> unreadable,
        RunHistory history,
        int window,
        StoryCatalog catalog,
        StoryCoverageReport? coverage,
        ReviewGateReport? reviewGate,
        IReadOnlyList<ComponentReview> reviews,
        IReadOnlyList<VisualDecision> visualDecisions,
        string? problem)
    {
        Settings = settings;
        Results = results;
        Unreadable = unreadable;
        History = history;
        Window = window;
        Catalog = catalog;
        Coverage = coverage;
        ReviewGate = reviewGate;
        Reviews = reviews;
        VisualDecisions = visualDecisions;
        Problem = problem;
        Summary = RunSummary.From(results);
        LoadedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The project's settings.</summary>
    public BtpSettings Settings { get; }

    /// <summary>The latest result of every key in the results directory.</summary>
    public IReadOnlyList<StoryResult> Results { get; }

    /// <summary>Result files that could not be read.</summary>
    public IReadOnlyList<string> Unreadable { get; }

    /// <summary>Totals of <see cref="Results"/>.</summary>
    public RunSummary Summary { get; }

    /// <summary>The archived runs.</summary>
    public RunHistory History { get; }

    /// <summary>Runs analysed.</summary>
    public int Window { get; }

    /// <summary>Stories the hub can run and link to.</summary>
    public StoryCatalog Catalog { get; }

    /// <summary>Story-state coverage (computed from the catalog, else read from the results directory).</summary>
    public StoryCoverageReport? Coverage { get; }

    /// <summary>The last manual-review gate verdicts.</summary>
    public ReviewGateReport? ReviewGate { get; }

    /// <summary>Committed manual reviews.</summary>
    public IReadOnlyList<ComponentReview> Reviews { get; }

    /// <summary>Recorded rejections of visual changes.</summary>
    public IReadOnlyList<VisualDecision> VisualDecisions { get; }

    /// <summary>A configuration problem to show (unreadable quarantine file...).</summary>
    public string? Problem { get; }

    /// <summary>When the snapshot was read.</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>Quarantined stories.</summary>
    public QuarantineList Quarantine => Settings.Quarantine;

    /// <summary>Stability of every key in the window, least stable first.</summary>
    public IReadOnlyList<KeyStability> Stability => _stability ??= History.Stability(Window);

    /// <summary>Unique errors in the window.</summary>
    public IReadOnlyList<ErrorCluster> Clusters => _clusters ??= History.Clusters(Window);

    /// <summary>The latest result of a key.</summary>
    public StoryResult? Result(string key) => (_byKey ??= Results.GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal)).GetValueOrDefault(key);

    /// <summary>Stability of a key.</summary>
    public KeyStability? StabilityOf(string key) => (_stabilityByKey ??= Stability.ToDictionary(s => s.Key, StringComparer.Ordinal)).GetValueOrDefault(key);

    /// <summary>The URL that serves <paramref name="path"/>, when it lies in the results or snapshot directory.</summary>
    public string? FileUrl(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var full = System.IO.Path.GetFullPath(path);
        foreach (var (root, directory) in new[] { ("results", Settings.ResultsDirectoryPath), ("snapshots", Settings.SnapshotDirectoryPath) })
        {
            var relative = System.IO.Path.GetRelativePath(directory, full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative) && File.Exists(full))
            {
                return $"{HubOptions.RoutePrefix}/files/{root}/{string.Join('/', relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString))}";
            }
        }

        return null;
    }
}

/// <summary>Reads hub data from disk and caches it until a watched file changes.</summary>
public sealed class HubStore
{
    private readonly HubOptions _options;
    private readonly Lock _gate = new();
    private readonly Lazy<StoryCatalog> _catalog;
    private HubSnapshot? _snapshot;
    private string? _stamp;
    private DateTime _checkedAt;

    /// <summary>Creates the store.</summary>
    public HubStore(HubOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _catalog = new Lazy<StoryCatalog>(options.BuildCatalog);
    }

    /// <summary>The run archive.</summary>
    public RunArchive Archive => new(_options.ResolvedArchiveDirectory);

    /// <summary>Stories the hub can run.</summary>
    public StoryCatalog Catalog => _catalog.Value;

    /// <summary>File holding visual review rejections.</summary>
    public string VisualDecisionsPath => Path.Combine(Path.GetDirectoryName(_options.ResolvedArchiveDirectory)!, "visual-decisions.json");

    /// <summary>The current snapshot (re-read when results, archive, quarantine or reviews changed).</summary>
    public HubSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                if (_snapshot is not null && DateTime.UtcNow - _checkedAt < TimeSpan.FromSeconds(1))
                {
                    return _snapshot;
                }

                _checkedAt = DateTime.UtcNow;
                var settings = TryLoadSettings(out var problem);
                var stamp = Stamp(settings);
                if (_snapshot is null || stamp != _stamp)
                {
                    _snapshot = Load(settings, problem);
                    _stamp = stamp;
                }

                return _snapshot;
            }
        }
    }

    /// <summary>Forgets the cached snapshot.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }

    /// <summary>Records a decision on a visual change.</summary>
    public void RecordVisualDecision(VisualDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var decisions = ReadDecisions(VisualDecisionsPath).Where(d => !(d.Snapshot == decision.Snapshot && d.Fingerprint == decision.Fingerprint)).Append(decision).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(VisualDecisionsPath)!);
        File.WriteAllText(VisualDecisionsPath, JsonSerializer.Serialize(decisions, BtpJson.Options));
        Invalidate();
    }

    private BtpSettings TryLoadSettings(out string? problem)
    {
        problem = null;
        try
        {
            return _options.LoadSettings();
        }
        catch (InvalidOperationException ex)
        {
            problem = ex.Message;
            return BtpSettings.Load(_options.ResolvedProjectDirectory, new Dictionary<string, string?> { ["BTP_QUARANTINE"] = Path.Combine(Path.GetTempPath(), "btp-missing-quarantine.json") });
        }
    }

    private string Stamp(BtpSettings settings)
    {
        static string Of(string path, string pattern = "*")
        {
            if (File.Exists(path))
            {
                return File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!Directory.Exists(path))
            {
                return "-";
            }

            var files = new DirectoryInfo(path).EnumerateFiles(pattern).ToArray();
            return $"{files.Length}:{(files.Length == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks))}";
        }

        return string.Join('|',
            Of(Path.Combine(settings.ResultsDirectoryPath, "results"), "*.json"),
            Of(Path.Combine(settings.ResultsDirectoryPath, StoryCoverageReport.FileName)),
            Of(Path.Combine(settings.ResultsDirectoryPath, ReviewGateReport.FileName)),
            Of(_options.ResolvedArchiveDirectory, "*.json"),
            Of(settings.QuarantineFilePath),
            Of(settings.ReviewsDirectoryPath),
            Of(VisualDecisionsPath));
    }

    private HubSnapshot Load(BtpSettings settings, string? problem)
    {
        var results = settings.CreateResultsStore().ReadAll(out var unreadable);
        var history = new RunHistory(Archive.Load(_options.ArchiveCapacity));
        var catalog = Catalog;
        StoryCoverageReport? coverage = null;
        try
        {
            var components = _options.StoryAssemblies.Count == 0
                ? []
                : catalog.Stories.Select(s => s.ComponentType.Assembly).Distinct().Where(a => !_options.StoryAssemblies.Contains(a) && a != typeof(Composition).Assembly)
                    .SelectMany(StoryCoverage.ComponentsIn).ToArray();
            coverage = catalog.Count > 0 ? StoryCoverage.Analyze(catalog, components) : StoryCoverageReport.Load(settings.ResultsDirectoryPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            coverage = StoryCoverageReport.Load(settings.ResultsDirectoryPath);
        }

        IReadOnlyList<ComponentReview> reviews;
        try
        {
            reviews = new ManualReviewStore(settings.ReviewsDirectoryPath).LoadAll();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            reviews = [];
            problem ??= $"Manual reviews could not be read: {ex.Message}";
        }

        return new HubSnapshot(
            settings,
            results,
            unreadable,
            history,
            _options.Window,
            catalog,
            coverage,
            ReviewGateReport.Load(settings.ResultsDirectoryPath),
            reviews,
            ReadDecisions(VisualDecisionsPath),
            problem);
    }

    private static List<VisualDecision> ReadDecisions(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<VisualDecision>>(File.ReadAllText(path), BtpJson.Options) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
