using System.Globalization;
using System.Text.Json;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Configuration;

/// <summary>How visual baselines are written.</summary>
public enum SnapshotUpdateMode
{
    /// <summary>Never write baselines; missing baselines fail (default on CI).</summary>
    None,

    /// <summary>Write baselines that do not exist yet (default locally).</summary>
    Missing,

    /// <summary>Overwrite baselines that differ, and write missing ones.</summary>
    Changed,

    /// <summary>Overwrite every baseline.</summary>
    All,
}

/// <summary>When failure diagnostics (trace, DOM, screenshot) are kept.</summary>
public enum DiagnosticsMode
{
    /// <summary>Never collect.</summary>
    Off,

    /// <summary>Collect for every run, keep only for failed runs and the failed attempts of flaky runs (default).</summary>
    RetainOnFailure,

    /// <summary>Always keep.</summary>
    Always,
}

/// <summary>
/// Run-wide settings, resolved from (highest precedence first) environment variables (<c>BTP_*</c>),
/// <c>btp.json</c> in the test project directory, then defaults. Paths are relative to the project directory.
/// </summary>
public sealed class BtpSettings
{
    private static readonly Lazy<BtpSettings> LazyCurrent = new(() => Load());

    /// <summary>Settings for the current test process.</summary>
    public static BtpSettings Current => LazyCurrent.Value;

    /// <summary>The test project directory (contains the <c>.csproj</c>).</summary>
    public string ProjectDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>Baseline images directory (commit it). Default <c>__snapshots__</c>.</summary>
    public string SnapshotDirectory { get; init; } = "__snapshots__";

    /// <summary>Results/artifacts directory (do not commit). Default <c>TestResults/btp</c>.</summary>
    public string ResultsDirectory { get; init; } = Path.Combine("TestResults", "btp");

    /// <summary>Performance baseline file (commit it). Default <c>__baselines__/performance.json</c>.</summary>
    public string PerformanceBaselinePath { get; init; } = Path.Combine("__baselines__", "performance.json");

    /// <summary>Manual accessibility reviews directory (commit it). Default <c>a11y-reviews</c>.</summary>
    public string ReviewsDirectory { get; init; } = "a11y-reviews";

    /// <summary>Visual baseline update mode (<c>BTP_UPDATE_SNAPSHOTS</c>: none|missing|changed|all).</summary>
    public SnapshotUpdateMode UpdateSnapshots { get; init; } = SnapshotUpdateMode.Missing;

    /// <summary>Record current metrics as the performance baseline (<c>BTP_UPDATE_PERF=1</c>).</summary>
    public bool UpdatePerformanceBaselines { get; init; }

    /// <summary>Retries for browser runs (<c>BTP_RETRIES</c>). Default 2 on CI, 0 locally.</summary>
    public int Retries { get; init; }

    /// <summary>Diagnostics mode (<c>BTP_DIAGNOSTICS</c>: off|on-failure|always).</summary>
    public DiagnosticsMode Diagnostics { get; init; } = DiagnosticsMode.RetainOnFailure;

    /// <summary>Default auto-wait timeout (<c>BTP_TIMEOUT_MS</c>). Default 5 s.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>True on CI (detected from <c>CI</c>, <c>GITHUB_ACTIONS</c>, <c>TF_BUILD</c>...).</summary>
    public bool IsContinuousIntegration { get; init; }

    /// <summary>Visual: per-pixel threshold default.</summary>
    public double VisualThreshold { get; init; } = 0.1;

    /// <summary>Visual: tolerated diff ratio default.</summary>
    public double VisualMaxDiffRatio { get; init; }

    /// <summary>Accessibility gate.</summary>
    public AccessibilityPolicy Accessibility { get; init; } = new();

    /// <summary>
    /// Stylesheets (absolute paths) whose <c>display</c>/<c>visibility</c> rules the in-memory driver applies, from
    /// <c>btp.json</c> → <c>bunit.stylesheets</c>. Empty: only inline styles and attributes decide visibility.
    /// </summary>
    public IReadOnlyList<string> BunitStylesheets { get; init; } = [];

    /// <summary>
    /// Viewport the in-memory driver evaluates <c>@media</c> queries against, from <c>BTP_BUNIT_VIEWPORT</c> or
    /// <c>btp.json</c> → <c>bunit.viewport</c>: a preset name (<c>mobile</c>, <c>tablet</c>, <c>desktop</c>...) or
    /// <c>WIDTHxHEIGHT</c>. Null: <see cref="Viewport.Desktop"/>.
    /// </summary>
    public Viewport? BunitViewport { get; init; }

    /// <summary>Performance regression tolerances.</summary>
    public PerformanceTolerance Performance { get; init; } = new();

    /// <summary>Absolute snapshot directory.</summary>
    public string SnapshotDirectoryPath => Resolve(SnapshotDirectory);

    /// <summary>Absolute results directory.</summary>
    public string ResultsDirectoryPath => Resolve(ResultsDirectory);

    /// <summary>Absolute performance baseline path.</summary>
    public string PerformanceBaselineFilePath => Resolve(PerformanceBaselinePath);

    /// <summary>Absolute reviews directory.</summary>
    public string ReviewsDirectoryPath => Resolve(ReviewsDirectory);

    /// <summary>Results store for this run.</summary>
    public ResultsStore CreateResultsStore() => new(ResultsDirectoryPath);

    /// <summary>Loads settings for a project directory (auto-detected when null).</summary>
    public static BtpSettings Load(string? projectDirectory = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Env(string name) => environment is not null ? environment.GetValueOrDefault(name) : Environment.GetEnvironmentVariable(name);

        var directory = projectDirectory ?? Env("BTP_PROJECT_DIR") ?? FindProjectDirectory(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;
        var file = ReadFile(Path.Combine(directory, "btp.json"));
        var ci = IsTruthy(Env("CI")) || !string.IsNullOrEmpty(Env("GITHUB_ACTIONS")) || !string.IsNullOrEmpty(Env("TF_BUILD"))
            || !string.IsNullOrEmpty(Env("GITLAB_CI")) || !string.IsNullOrEmpty(Env("BUILDKITE")) || !string.IsNullOrEmpty(Env("TEAMCITY_VERSION"));

        var policy = new AccessibilityPolicy();
        if (file?.Accessibility is { } a11y)
        {
            if (a11y.FailOn is { } failOn)
            {
                policy.FailOn = failOn;
            }

            if (a11y.Tags is { Count: > 0 } tags)
            {
                policy.Tags.Clear();
                policy.Tags.UnionWith(tags);
            }

            if (a11y.IncludeBestPractices is { } bestPractices)
            {
                policy.IncludeBestPractices = bestPractices;
            }

            policy.DisabledRules.UnionWith(a11y.DisabledRules ?? []);
            policy.Suppressions.AddRange(a11y.Suppressions ?? []);
        }

        return new BtpSettings
        {
            ProjectDirectory = directory,
            SnapshotDirectory = Env("BTP_SNAPSHOT_DIR") ?? file?.SnapshotDirectory ?? "__snapshots__",
            ResultsDirectory = Env("BTP_RESULTS_DIR") ?? file?.ResultsDirectory ?? Path.Combine("TestResults", "btp"),
            PerformanceBaselinePath = Env("BTP_PERF_BASELINE") ?? file?.PerformanceBaselinePath ?? Path.Combine("__baselines__", "performance.json"),
            ReviewsDirectory = Env("BTP_REVIEWS_DIR") ?? file?.ReviewsDirectory ?? "a11y-reviews",
            UpdateSnapshots = ParseUpdateMode(Env("BTP_UPDATE_SNAPSHOTS")) ?? file?.UpdateSnapshots ?? (ci ? SnapshotUpdateMode.None : SnapshotUpdateMode.Missing),
            UpdatePerformanceBaselines = IsTruthy(Env("BTP_UPDATE_PERF")),
            Retries = int.TryParse(Env("BTP_RETRIES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var retries) ? retries : file?.Retries ?? (ci ? 2 : 0),
            Diagnostics = ParseDiagnostics(Env("BTP_DIAGNOSTICS")) ?? file?.Diagnostics ?? DiagnosticsMode.RetainOnFailure,
            DefaultTimeout = int.TryParse(Env("BTP_TIMEOUT_MS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout)
                ? TimeSpan.FromMilliseconds(timeout)
                : TimeSpan.FromMilliseconds(file?.TimeoutMilliseconds ?? 5000),
            IsContinuousIntegration = ci,
            VisualThreshold = file?.Visual?.Threshold ?? 0.1,
            VisualMaxDiffRatio = file?.Visual?.MaxDiffRatio ?? 0,
            Accessibility = policy,
            Performance = file?.Performance ?? new PerformanceTolerance(),
            BunitStylesheets = (file?.Bunit?.Stylesheets ?? []).Select(p => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(directory, p))).ToArray(),
            BunitViewport = ParseViewport(Env("BTP_BUNIT_VIEWPORT") ?? file?.Bunit?.Viewport),
        };
    }

    /// <summary>Parses a viewport preset name (<c>mobile</c>) or a size (<c>390x844</c>). Null for empty input.</summary>
    public static Viewport? ParseViewport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (Viewport.FindPreset(text) is { } preset)
        {
            return preset;
        }

        var parts = text.Split('x', 'X');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) && height > 0)
        {
            return new Viewport(text.ToLowerInvariant(), width, height);
        }

        throw new FormatException($"Invalid viewport '{value}'. Use a preset ({string.Join(", ", Viewport.Presets.Select(p => p.Name))}) or WIDTHxHEIGHT.");
    }

    /// <summary>Walks up from <paramref name="start"/> to the first directory containing a project file.</summary>
    public static string? FindProjectDirectory(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.csproj").Any() || directory.EnumerateFiles("*.fsproj").Any() || directory.EnumerateFiles("*.vbproj").Any())
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectDirectory, path));

    /// <summary>True for the values that switch a <c>BTP_*</c> flag on: <c>1</c>, <c>true</c> or <c>yes</c> (case-insensitive).</summary>
    public static bool IsTruthy(string? value) => value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static SnapshotUpdateMode? ParseUpdateMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "none" or "0" or "false" => SnapshotUpdateMode.None,
        "missing" => SnapshotUpdateMode.Missing,
        "changed" or "failed" => SnapshotUpdateMode.Changed,
        "all" or "1" or "true" => SnapshotUpdateMode.All,
        _ => throw new FormatException($"Invalid BTP_UPDATE_SNAPSHOTS value '{value}'. Use none, missing, changed or all."),
    };

    private static DiagnosticsMode? ParseDiagnostics(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "off" or "0" or "false" => DiagnosticsMode.Off,
        "on-failure" or "retain-on-failure" or "failure" => DiagnosticsMode.RetainOnFailure,
        "always" or "on" or "1" => DiagnosticsMode.Always,
        _ => throw new FormatException($"Invalid BTP_DIAGNOSTICS value '{value}'. Use off, on-failure or always."),
    };

    private static SettingsFile? ReadFile(string path) =>
        File.Exists(path) ? BtpJson.Deserialize<SettingsFile>(File.ReadAllText(path)) : null;

    internal sealed class SettingsFile
    {
        public string? SnapshotDirectory { get; init; }

        public string? ResultsDirectory { get; init; }

        public string? PerformanceBaselinePath { get; init; }

        public string? ReviewsDirectory { get; init; }

        public SnapshotUpdateMode? UpdateSnapshots { get; init; }

        public int? Retries { get; init; }

        public DiagnosticsMode? Diagnostics { get; init; }

        public int? TimeoutMilliseconds { get; init; }

        public VisualSection? Visual { get; init; }

        public AccessibilitySection? Accessibility { get; init; }

        public PerformanceTolerance? Performance { get; init; }

        public BunitSection? Bunit { get; init; }
    }

    internal sealed class BunitSection
    {
        public List<string>? Stylesheets { get; init; }

        public string? Viewport { get; init; }
    }

    internal sealed class VisualSection
    {
        public double? Threshold { get; init; }

        public double? MaxDiffRatio { get; init; }
    }

    internal sealed class AccessibilitySection
    {
        public AccessibilityImpact? FailOn { get; init; }

        public List<string>? Tags { get; init; }

        public bool? IncludeBestPractices { get; init; }

        public List<string>? DisabledRules { get; init; }

        public List<AccessibilitySuppression>? Suppressions { get; init; }
    }
}
