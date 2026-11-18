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

    /// <summary>Quarantine file (commit it). Default <c>quarantine.json</c>.</summary>
    public string QuarantinePath { get; init; } = QuarantineList.DefaultFileName;

    /// <summary>Quarantined stories: their failures are reported but do not fail the build.</summary>
    public QuarantineList Quarantine { get; init; } = QuarantineList.Empty;

    /// <summary>Visual baseline update mode (<c>BTP_UPDATE_SNAPSHOTS</c>: none|missing|changed|all).</summary>
    public SnapshotUpdateMode UpdateSnapshots { get; init; } = SnapshotUpdateMode.Missing;

    /// <summary>Record current metrics as the performance baseline (<c>BTP_UPDATE_PERF=1</c>).</summary>
    public bool UpdatePerformanceBaselines { get; init; }

    /// <summary>Retries for browser runs (<c>BTP_RETRIES</c>). Default 2 on CI, 0 locally.</summary>
    public int Retries { get; init; }

    /// <summary>
    /// Burn-in: how many times every story runs (<c>BTP_REPEAT_EACH</c>, <c>repeatEach</c>; default 1). A story fails
    /// unless every run passes on its first attempt, so a flaky new or changed story is caught before it merges (as
    /// Playwright's <c>--repeat-each</c> or JUnit's <c>@RepeatedTest</c>). Combine it with <see cref="StoryImpact"/>
    /// to repeat only the stories a change affects.
    /// </summary>
    public int RepeatEach { get; init; } = 1;

    /// <summary>
    /// Fail runs that only passed on a retry (<c>BTP_FAIL_ON_FLAKY=1</c>, <c>failOnFlaky</c>), as Playwright's
    /// <c>--fail-on-flaky-tests</c>: retries still run and their diagnostics are kept, but a flaky result fails the test.
    /// </summary>
    public bool FailOnFlaky { get; init; }

    /// <summary>
    /// Stop after this many failures (<c>BTP_MAX_FAILURES</c>, <c>maxFailures</c>; 0, the default, never stops), as
    /// Playwright's <c>--max-failures</c>: once the process has seen that many blocking failures, every story that starts
    /// afterwards is reported as skipped, saying why. Quarantined and expected failures do not count.
    /// </summary>
    public int MaxFailures { get; init; }

    /// <summary>
    /// Run only the stories whose tags match this expression (<c>BTP_TAGS</c>, <c>tags</c>), such as
    /// <c>smoke &amp;&amp; !visual</c> or <c>(forms || dialogs) and not slow</c>; see <see cref="TagExpression"/>. The
    /// test framework packages report the others as skipped.
    /// </summary>
    public TagExpression? TagFilter { get; init; }

    /// <summary>
    /// Run options by tag (<c>tagOptions</c> in <c>btp.json</c>; tag names are case insensitive): see <see cref="TagOptions"/>,
    /// <see cref="TimeoutFor"/>, <see cref="RetriesFor"/> and <see cref="TagSkipReason"/>.
    /// </summary>
    public IReadOnlyDictionary<string, TagOptions> TagOptions { get; init; } = new Dictionary<string, TagOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A runner's timeout for <paramref name="story"/>: its own <c>Timeout(...)</c>, else the longest of its tags', else
    /// <paramref name="runnerDefault"/>; tripled when the story or one of its tags is slow.
    /// </summary>
    public TimeSpan TimeoutFor(Story story, TimeSpan runnerDefault)
    {
        ArgumentNullException.ThrowIfNull(story);
        var tags = OptionsOf(story);
        var tagTimeout = tags.Max(t => t.TimeoutMilliseconds);
        var timeout = story.Timeout ?? (tagTimeout is { } ms ? TimeSpan.FromMilliseconds(ms) : runnerDefault);
        return timeout * (story.IsSlow || tags.Any(t => t.Slow == true) ? 3 : 1);
    }

    /// <summary>The most retries a tag of <paramref name="story"/> asks for, or null when no tag sets them.</summary>
    public int? RetriesFor(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        return OptionsOf(story).Max(t => t.Retries);
    }

    /// <summary>Why a tag of <paramref name="story"/> skips it, or null.</summary>
    public string? TagSkipReason(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        return story.Tags.Order(StringComparer.Ordinal)
            .Select(tag => TagOptions.TryGetValue(tag, out var options) && !string.IsNullOrWhiteSpace(options.Skip) ? $"Skipped by tag '{tag}': {options.Skip}" : null)
            .FirstOrDefault(reason => reason is not null);
    }

    private TagOptions[] OptionsOf(Story story) =>
        TagOptions.Count == 0 ? [] : [.. story.Tags.Select(tag => TagOptions.GetValueOrDefault(tag)).OfType<TagOptions>()];

    /// <summary>
    /// Run only the stories whose id or <c>Title/Name</c> path matches this regular expression (<c>BTP_GREP</c>, case
    /// insensitive), as Playwright's <c>--grep</c>.
    /// </summary>
    public System.Text.RegularExpressions.Regex? Grep { get; init; }

    /// <summary>Leave out the stories whose id or path matches this regular expression (<c>BTP_GREP_INVERT</c>).</summary>
    public System.Text.RegularExpressions.Regex? GrepInvert { get; init; }

    /// <summary>
    /// Rerun only the stories that failed in the results directory's previous run (<c>BTP_LAST_FAILED=1</c>), as
    /// Playwright's <c>--last-failed</c> or pytest's <c>--lf</c>. A story is rerun when any of its results (any driver, cell
    /// or configuration) failed; when no previous result failed, every story runs.
    /// </summary>
    public bool LastFailed { get; init; }

    /// <summary>Diagnostics mode (<c>BTP_DIAGNOSTICS</c>: off|on-failure|always).</summary>
    public DiagnosticsMode Diagnostics { get; init; } = DiagnosticsMode.RetainOnFailure;

    /// <summary>Default auto-wait timeout (<c>BTP_TIMEOUT_MS</c>). Default 5 s.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// True on CI: <c>BTP_CI</c> when set, otherwise detected from <c>CI</c> (any value but <c>false</c>/<c>0</c>) and the
    /// variables of GitHub Actions, Azure Pipelines, GitLab, Jenkins, TeamCity, Buildkite, CircleCI, AWS CodeBuild,
    /// Bitbucket, AppVeyor, Travis, Drone, Semaphore and Bitrise.
    /// </summary>
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

    /// <summary>Absolute quarantine file path.</summary>
    public string QuarantineFilePath => Resolve(QuarantinePath);

    /// <summary>Results store for this run.</summary>
    public ResultsStore CreateResultsStore() => new(ResultsDirectoryPath);

    /// <summary>Loads settings for a project directory (auto-detected when null).</summary>
    public static BtpSettings Load(string? projectDirectory = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        // An empty variable (an unset workflow input, `export X=`) means "not set", never "the project directory".
        string? Env(string name) => NonEmpty(environment is not null ? environment.GetValueOrDefault(name) : Environment.GetEnvironmentVariable(name));

        var directory = NonEmpty(projectDirectory) ?? Env("BTP_PROJECT_DIR") ?? FindProjectDirectory(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;
        var file = ReadFile(Path.Combine(directory, "btp.json"));
        var ci = DetectContinuousIntegration(Env);

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

        var quarantinePath = Env("BTP_QUARANTINE") ?? NonEmpty(file?.QuarantinePath) ?? QuarantineList.DefaultFileName;
        return new BtpSettings
        {
            ProjectDirectory = directory,
            SnapshotDirectory = Env("BTP_SNAPSHOT_DIR") ?? NonEmpty(file?.SnapshotDirectory) ?? "__snapshots__",
            ResultsDirectory = Env("BTP_RESULTS_DIR") ?? NonEmpty(file?.ResultsDirectory) ?? Path.Combine("TestResults", "btp"),
            PerformanceBaselinePath = Env("BTP_PERF_BASELINE") ?? NonEmpty(file?.PerformanceBaselinePath) ?? Path.Combine("__baselines__", "performance.json"),
            ReviewsDirectory = Env("BTP_REVIEWS_DIR") ?? NonEmpty(file?.ReviewsDirectory) ?? "a11y-reviews",
            QuarantinePath = quarantinePath,
            Quarantine = QuarantineList.Load(Path.IsPathRooted(quarantinePath) ? quarantinePath : Path.Combine(directory, quarantinePath)),
            UpdateSnapshots = ParseUpdateMode(Env("BTP_UPDATE_SNAPSHOTS")) ?? file?.UpdateSnapshots ?? (ci ? SnapshotUpdateMode.None : SnapshotUpdateMode.Missing),
            UpdatePerformanceBaselines = IsTruthy(Env("BTP_UPDATE_PERF")),
            Retries = int.TryParse(Env("BTP_RETRIES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var retries) ? retries : file?.Retries ?? (ci ? 2 : 0),
            RepeatEach = ParsePositive(Env("BTP_REPEAT_EACH"), "BTP_REPEAT_EACH", "number of runs") ?? ValidatePositive(file?.RepeatEach, "repeatEach", "number of runs") ?? 1,
            Diagnostics = ParseDiagnostics(Env("BTP_DIAGNOSTICS")) ?? file?.Diagnostics ?? DiagnosticsMode.RetainOnFailure,
            FailOnFlaky = Env("BTP_FAIL_ON_FLAKY") is { } failOnFlaky ? IsTruthy(failOnFlaky) : file?.FailOnFlaky ?? false,
            MaxFailures = ParseNonNegative(Env("BTP_MAX_FAILURES"), "BTP_MAX_FAILURES") ?? ValidateNonNegative(file?.MaxFailures, "maxFailures") ?? 0,
            TagFilter = (Env("BTP_TAGS") ?? NonEmpty(file?.Tags)) is { } tagFilter ? TagExpression.Parse(tagFilter) : null,
            TagOptions = ValidateTagOptions(file?.TagOptions),
            Grep = ParsePattern(Env("BTP_GREP"), "BTP_GREP"),
            GrepInvert = ParsePattern(Env("BTP_GREP_INVERT"), "BTP_GREP_INVERT"),
            LastFailed = IsTruthy(Env("BTP_LAST_FAILED")),
            DefaultTimeout = TimeSpan.FromMilliseconds(ParsePositive(Env("BTP_TIMEOUT_MS"), "BTP_TIMEOUT_MS") ?? ValidatePositive(file?.TimeoutMilliseconds, "timeoutMilliseconds") ?? 5000),
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

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static readonly string[] CiVariables =
    [
        "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "JENKINS_URL", "TEAMCITY_VERSION", "BUILDKITE", "CIRCLECI", "CODEBUILD_BUILD_ID",
        "BITBUCKET_BUILD_NUMBER", "APPVEYOR", "TRAVIS", "DRONE", "SEMAPHORE", "BITRISE_IO",
    ];

    private static bool DetectContinuousIntegration(Func<string, string?> env)
    {
        if (env("BTP_CI") is { } explicitValue)
        {
            return IsTruthy(explicitValue);
        }

        // CI=true is the convention, but several systems set CI to their own name (CI=woodpecker).
        if (env("CI") is { } ci)
        {
            return !IsFalsy(ci);
        }

        return CiVariables.Any(name => env(name) is not null);
    }

    private static bool IsFalsy(string value) =>
        value.Trim() is "0" || value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("no", StringComparison.OrdinalIgnoreCase);

    private static int? ParsePositive(string? value, string name, string unit = "number of milliseconds")
    {
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw new FormatException($"Invalid {name} value '{value}'. Use a positive {unit}.");
    }

    private static int? ParseNonNegative(string? value, string name) =>
        value is null
            ? null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : throw new FormatException($"Invalid {name} value '{value}'. Use 0 (no limit) or a positive number.");

    private static Dictionary<string, TagOptions> ValidateTagOptions(Dictionary<string, TagOptions>? options)
    {
        var validated = new Dictionary<string, TagOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, value) in options ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag) || value is null)
            {
                continue;
            }

            ValidatePositive(value.TimeoutMilliseconds, $"tagOptions.{tag}.timeoutMilliseconds");
            ValidateNonNegative(value.Retries, $"tagOptions.{tag}.retries");
            validated[tag.Trim()] = value;
        }

        return validated;
    }

    private static int? ValidateNonNegative(int? value, string name) =>
        value is null or >= 0 ? value : throw new FormatException($"Invalid btp.json {name} value '{value}'. Use 0 (no limit) or a positive number.");

    private static System.Text.RegularExpressions.Regex? ParsePattern(string? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return new System.Text.RegularExpressions.Regex(value, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid {name} value '{value}': {ex.Message}", ex);
        }
    }

    private static int? ValidatePositive(int? value, string name, string unit = "number of milliseconds") =>
        value is null or > 0 ? value : throw new FormatException($"Invalid btp.json {name} value '{value}'. Use a positive {unit}.");

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

        public string? QuarantinePath { get; init; }

        public SnapshotUpdateMode? UpdateSnapshots { get; init; }

        public int? Retries { get; init; }

        public int? RepeatEach { get; init; }

        public DiagnosticsMode? Diagnostics { get; init; }

        public bool? FailOnFlaky { get; init; }

        public int? MaxFailures { get; init; }

        public string? Tags { get; init; }

        public Dictionary<string, TagOptions>? TagOptions { get; init; }

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
