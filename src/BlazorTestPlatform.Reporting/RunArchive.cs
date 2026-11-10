using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Where and how a run happened.</summary>
public sealed record RunInfo
{
    /// <summary>Unique id of the run (CI run id, or a timestamp).</summary>
    public required string RunId { get; init; }

    /// <summary>What started it: <c>ci</c>, <c>local</c>, <c>hub</c>...</summary>
    public string Trigger { get; init; } = "local";

    /// <summary>Optional label (a pull request title, "nightly"...).</summary>
    public string? Label { get; init; }

    /// <summary>When the earliest result started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the run was recorded.</summary>
    public DateTimeOffset FinishedAt { get; init; }

    /// <summary>Branch.</summary>
    public string? Branch { get; init; }

    /// <summary>Commit SHA.</summary>
    public string? Commit { get; init; }

    /// <summary>Commit subject line.</summary>
    public string? CommitMessage { get; init; }

    /// <summary>Commit author.</summary>
    public string? Author { get; init; }

    /// <summary>Pull request number.</summary>
    public int? PullRequest { get; init; }

    /// <summary>CI provider (<c>github</c>, <c>azure-devops</c>, <c>gitlab</c>...).</summary>
    public string? CiProvider { get; init; }

    /// <summary>Link to the CI run.</summary>
    public string? CiUrl { get; init; }

    /// <summary>Machine name.</summary>
    public string? Machine { get; init; }

    /// <summary>Operating system.</summary>
    public string? OperatingSystem { get; init; }

    /// <summary>.NET runtime.</summary>
    public string? Runtime { get; init; }

    /// <summary>
    /// Describes the current environment: CI variables for GitHub Actions, Azure Pipelines and GitLab, otherwise
    /// <c>git</c> in <paramref name="workingDirectory"/> (best effort, bounded to a few seconds).
    /// </summary>
    public static RunInfo Detect(string? runId = null, string? trigger = null, string? workingDirectory = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Env(string name)
        {
            var value = environment is not null ? environment.GetValueOrDefault(name) : Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        string? provider = null, branch = null, commit = null, url = null, id = null;
        int? pr = null;
        if (Env("GITHUB_ACTIONS") is not null)
        {
            provider = "github";
            commit = Env("GITHUB_SHA");
            branch = Env("GITHUB_HEAD_REF") ?? Env("GITHUB_REF_NAME");
            id = Env("GITHUB_RUN_ID") is { } run ? $"{run}-{Env("GITHUB_RUN_ATTEMPT") ?? "1"}" : null;
            url = Env("GITHUB_SERVER_URL") is { } server && Env("GITHUB_REPOSITORY") is { } repo && Env("GITHUB_RUN_ID") is { } runNumber ? $"{server}/{repo}/actions/runs/{runNumber}" : null;
            if (Env("GITHUB_REF") is { } gitRef && gitRef.StartsWith("refs/pull/", StringComparison.Ordinal)
                && int.TryParse(gitRef.Split('/')[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                pr = number;
            }
        }
        else if (Env("TF_BUILD") is not null)
        {
            provider = "azure-devops";
            commit = Env("BUILD_SOURCEVERSION");
            branch = Env("SYSTEM_PULLREQUEST_SOURCEBRANCH") ?? Env("BUILD_SOURCEBRANCHNAME");
            id = Env("BUILD_BUILDID");
            pr = int.TryParse(Env("SYSTEM_PULLREQUEST_PULLREQUESTNUMBER") ?? Env("SYSTEM_PULLREQUEST_PULLREQUESTID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
        }
        else if (Env("GITLAB_CI") is not null)
        {
            provider = "gitlab";
            commit = Env("CI_COMMIT_SHA");
            branch = Env("CI_MERGE_REQUEST_SOURCE_BRANCH_NAME") ?? Env("CI_COMMIT_REF_NAME");
            id = Env("CI_PIPELINE_ID");
            url = Env("CI_PIPELINE_URL");
            pr = int.TryParse(Env("CI_MERGE_REQUEST_IID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
        }

        string? message = null, author = null;
        if (environment is null)
        {
            commit ??= Git(workingDirectory, "rev-parse", "HEAD");
            branch ??= Git(workingDirectory, "rev-parse", "--abbrev-ref", "HEAD");
            message = Git(workingDirectory, "log", "-1", "--format=%s");
            author = Git(workingDirectory, "log", "-1", "--format=%an");
        }

        var now = DateTimeOffset.UtcNow;
        return new RunInfo
        {
            RunId = runId ?? id ?? now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
            Trigger = trigger ?? (provider is null ? "local" : "ci"),
            StartedAt = now,
            FinishedAt = now,
            Branch = branch == "HEAD" ? null : branch,
            Commit = commit,
            CommitMessage = message,
            Author = author,
            PullRequest = pr,
            CiProvider = provider,
            CiUrl = url,
            Machine = Environment.MachineName,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        };
    }

    private static string? Git(string? workingDirectory, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            if (workingDirectory is not null)
            {
                start.WorkingDirectory = workingDirectory;
            }

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 && output.Trim() is { Length: > 0 } value ? value : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

/// <summary>An accessibility violation as kept in the archive: enough to tell new, existing and fixed violations apart.</summary>
/// <param name="RuleId">axe rule id.</param>
/// <param name="Impact">Impact.</param>
/// <param name="Targets">Selectors of the first affected elements.</param>
/// <param name="Wcag">WCAG success criteria (<c>1.4.3</c>).</param>
/// <param name="Help">One-line help text.</param>
public sealed record ArchivedViolation(string RuleId, AccessibilityImpact Impact, IReadOnlyList<string> Targets, IReadOnlyList<string> Wcag, string? Help = null);

/// <summary>One result as kept in the archive: the facts history views need, without timelines and artifacts.</summary>
public sealed record ArchivedResult
{
    /// <summary>Result key.</summary>
    public required string Key { get; init; }

    /// <summary>Story id.</summary>
    public required string StoryId { get; init; }

    /// <summary>Story title.</summary>
    public required string Title { get; init; }

    /// <summary>Story name.</summary>
    public required string Name { get; init; }

    /// <summary>Component type.</summary>
    public string? Component { get; init; }

    /// <summary>Driver.</summary>
    public required string Driver { get; init; }

    /// <summary>Render mode token.</summary>
    public string? HostingModel { get; init; }

    /// <summary>Viewport.</summary>
    public string? Viewport { get; init; }

    /// <summary>Theme.</summary>
    public string? Theme { get; init; }

    /// <summary>Outcome.</summary>
    public StoryOutcome Outcome { get; init; }

    /// <summary>Attempts.</summary>
    public int Attempts { get; init; } = 1;

    /// <summary>Duration.</summary>
    public double DurationMilliseconds { get; init; }

    /// <summary>First lines of the error.</summary>
    public string? Error { get; init; }

    /// <summary>Exception type.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Failure signature (<see cref="FailureAnalysis.Signature(StoryResult)"/>).</summary>
    public string? Signature { get; init; }

    /// <summary>Failure category.</summary>
    public FailureCategory Category { get; init; }

    /// <summary>Skip reason.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Quarantine reason, when a quarantined failure.</summary>
    public string? Quarantine { get; init; }

    /// <summary>Story source file.</summary>
    public string? SourceFile { get; init; }

    /// <summary>Visual status.</summary>
    public VisualStatus? Visual { get; init; }

    /// <summary>Share of differing pixels.</summary>
    public double? VisualDiffRatio { get; init; }

    /// <summary>Visual diff fingerprint.</summary>
    public string? VisualFingerprint { get; init; }

    /// <summary>Accessibility violations found.</summary>
    public IReadOnlyList<ArchivedViolation> Violations { get; init; } = [];

    /// <summary>Rules axe could not decide (needs review).</summary>
    public int Incomplete { get; init; }

    /// <summary>Rules that failed the accessibility gate.</summary>
    public IReadOnlyList<string> AccessibilityFailures { get; init; } = [];

    /// <summary>Render metrics (bUnit).</summary>
    public RenderMetrics? Render { get; init; }

    /// <summary>Browser runtime metrics.</summary>
    public BrowserMetrics? Browser { get; init; }

    /// <summary>Budget violations.</summary>
    public int BudgetViolations { get; init; }

    /// <summary>True for passed or flaky.</summary>
    public bool IsSuccess => Outcome is StoryOutcome.Passed or StoryOutcome.Flaky;

    /// <summary>Failed and not quarantined.</summary>
    public bool IsBlockingFailure => Outcome == StoryOutcome.Failed && Quarantine is null;

    /// <summary>Compacts a full result.</summary>
    public static ArchivedResult From(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var error = result.Error is { } e ? string.Join(Environment.NewLine, e.ReplaceLineEndings("\n").Split('\n').Take(6)) : null;
        return new ArchivedResult
        {
            Key = result.Key,
            StoryId = result.StoryId,
            Title = result.Title,
            Name = result.Name,
            Component = result.Component,
            Driver = result.Driver,
            HostingModel = result.HostingModel,
            Viewport = result.Viewport,
            Theme = result.Theme,
            Outcome = result.Outcome,
            Attempts = result.Attempts,
            DurationMilliseconds = Math.Round(result.DurationMilliseconds, 1),
            Error = error is { Length: > 2000 } ? error[..2000] : error,
            ErrorType = result.ErrorType,
            Signature = FailureAnalysis.Signature(result),
            Category = FailureAnalysis.Categorize(result),
            SkipReason = result.SkipReason,
            Quarantine = result.Quarantine?.Reason,
            SourceFile = result.SourceFile,
            Visual = result.Visual?.Status,
            VisualDiffRatio = result.Visual is { DiffRatio: > 0 } v ? v.DiffRatio : null,
            VisualFingerprint = result.Visual?.DiffFingerprint,
            Violations = result.Accessibility?.Violations
                .Select(v => new ArchivedViolation(v.RuleId, v.Impact, v.Nodes.Select(n => n.Target).Take(5).ToArray(), v.WcagCriteria.ToArray(), v.Help))
                .ToArray() ?? [],
            Incomplete = result.Accessibility?.Incomplete.Count ?? 0,
            AccessibilityFailures = result.AccessibilityFailures,
            Render = result.Performance,
            Browser = result.BrowserMetrics,
            BudgetViolations = result.BudgetViolations.Count,
        };
    }
}

/// <summary>A run in the archive: its metadata and compacted results.</summary>
public sealed record RunRecord
{
    /// <summary>Format version.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Run metadata.</summary>
    public required RunInfo Run { get; init; }

    /// <summary>Results.</summary>
    public IReadOnlyList<ArchivedResult> Results { get; init; } = [];

    /// <summary>Results counted by outcome.</summary>
    public int Count(StoryOutcome outcome) => Results.Count(r => r.Outcome == outcome);

    /// <summary>Blocking failures.</summary>
    public int Failed => Results.Count(r => r.IsBlockingFailure);

    /// <summary>Quarantined failures.</summary>
    public int Quarantined => Results.Count(r => r.Outcome == StoryOutcome.Failed && r.Quarantine is not null);

    /// <summary>Passed and flaky out of everything that ran.</summary>
    public double PassRate
    {
        get
        {
            var ran = Results.Count(r => r.Outcome != StoryOutcome.Skipped);
            return ran == 0 ? 1 : (double)Results.Count(r => r.IsSuccess) / ran;
        }
    }

    /// <summary>Sum of durations.</summary>
    public double DurationMilliseconds => Results.Sum(r => r.DurationMilliseconds);

    /// <summary>Wall-clock time of the run.</summary>
    public TimeSpan WallClock => Run.FinishedAt - Run.StartedAt;

    /// <summary>Distinct stories.</summary>
    public int Stories => Results.Select(r => r.StoryId).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Creates a record from results.</summary>
    public static RunRecord Create(RunInfo run, IEnumerable<StoryResult> results)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(results);
        var list = results.ToArray();
        var started = list.Length == 0 ? run.StartedAt : list.Min(r => r.StartedAt);
        var finished = list.Length == 0 ? run.FinishedAt : list.Max(r => r.StartedAt + TimeSpan.FromMilliseconds(r.DurationMilliseconds));
        return new RunRecord
        {
            Run = run with
            {
                StartedAt = started == default ? run.StartedAt : started,
                FinishedAt = finished == default || finished < started ? run.FinishedAt : finished,
            },
            Results = list.OrderBy(r => r.Key, StringComparer.Ordinal).Select(ArchivedResult.From).ToArray(),
        };
    }
}

/// <summary>
/// The run history behind dashboards and trends: one JSON file per run in a directory (by default
/// <c>.btp/runs</c>). Persist the directory between CI runs (a cache, an artifact, a branch) to build history there;
/// local runs and the hub add to the same directory.
/// </summary>
public sealed class RunArchive
{
    /// <summary>Runs kept by default.</summary>
    public const int DefaultCapacity = 100;

    /// <summary>Creates an archive in <paramref name="directory"/>.</summary>
    public RunArchive(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = System.IO.Path.GetFullPath(directory);
    }

    /// <summary>Archive directory.</summary>
    public string Directory { get; }

    /// <summary>Saves a run (replacing one with the same id) and prunes to <paramref name="capacity"/> runs.</summary>
    public string Save(RunRecord record, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(record);
        System.IO.Directory.CreateDirectory(Directory);
        foreach (var existing in FilesFor(record.Run.RunId))
        {
            File.Delete(existing);
        }

        var path = System.IO.Path.Combine(Directory, $"{record.Run.FinishedAt.UtcDateTime:yyyyMMddHHmmssfff}_{ResultsStore.SafeFileName(record.Run.RunId)}.json");
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record, BtpJson.Options));
        File.Move(temp, path, overwrite: true);
        Prune(capacity);
        return path;
    }

    /// <summary>Loads runs, oldest first. Unreadable files are skipped and listed in <paramref name="unreadable"/>.</summary>
    public IReadOnlyList<RunRecord> Load(out IReadOnlyList<string> unreadable, int? newest = null)
    {
        var problems = new List<string>();
        unreadable = problems;
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var files = System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        if (newest is { } n)
        {
            files = files.TakeLast(n).ToArray();
        }

        var runs = new List<RunRecord>();
        foreach (var file in files)
        {
            try
            {
                if (JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file), BtpJson.Options) is { Run.RunId.Length: > 0 } record)
                {
                    runs.Add(record);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                problems.Add($"{file}: {ex.Message}");
            }
        }

        return runs.OrderBy(r => r.Run.FinishedAt).ToArray();
    }

    /// <summary>Loads runs, oldest first, skipping unreadable files.</summary>
    public IReadOnlyList<RunRecord> Load(int? newest = null) => Load(out _, newest);

    /// <summary>Deletes a run.</summary>
    public bool Delete(string runId)
    {
        var files = FilesFor(runId).ToArray();
        foreach (var file in files)
        {
            File.Delete(file);
        }

        return files.Length > 0;
    }

    /// <summary>Keeps the newest <paramref name="capacity"/> runs.</summary>
    public void Prune(int capacity)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var old in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal).SkipLast(Math.Max(1, capacity)))
        {
            File.Delete(old);
        }
    }

    private IEnumerable<string> FilesFor(string runId)
    {
        var suffix = $"_{ResultsStore.SafeFileName(runId)}.json";
        return System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.json").Where(f => System.IO.Path.GetFileName(f).EndsWith(suffix, StringComparison.Ordinal))
            : [];
    }
}
