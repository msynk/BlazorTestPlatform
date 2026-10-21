using System.CommandLine;
using System.Globalization;
using BlazorTestPlatform;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Coverage;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;
using BlazorTestPlatform.Visual;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var project = new Option<string>("--project", "-p")
{
    Description = "Test project directory (contains the .csproj and btp.json). Defaults to BTP_PROJECT_DIR, else the current directory.",
    DefaultValueFactory = _ => Environment.GetEnvironmentVariable("BTP_PROJECT_DIR") is { Length: > 0 } projectDirectory ? projectDirectory : Environment.CurrentDirectory,
    Recursive = true,
};

var results = new Option<string[]>("--results", "-r")
{
    Description = "Results directories to read (repeatable). Defaults to the project's results directory.",
    AllowMultipleArgumentsPerToken = true,
};

var root = new RootCommand("BlazorTestPlatform: reports, baseline approval, accessibility reviews and flake analysis.") { project };

// btp report
var output = new Option<string?>("--output", "-o") { Description = "Directory for the HTML report (default: <results>/report)." };
var junit = new Option<string?>("--junit") { Description = "Also write JUnit XML to this path." };
var markdown = new Option<string?>("--markdown") { Description = "Also write a Markdown summary to this path." };
var githubSummary = new Option<bool>("--github-summary") { Description = "Append the Markdown summary to $GITHUB_STEP_SUMMARY." };
var history = new Option<string?>("--history") { Description = "Flake history file to update and analyse." };
var runId = new Option<string?>("--run-id") { Description = "Identifier of this run in the history (default: CI run id or timestamp)." };
var failOnFailures = new Option<bool>("--fail-on-failures") { Description = "Exit with code 1 when any story failed." };
var prComment = new Option<bool>("--pr-comment") { Description = "Post the Markdown summary on the GitHub pull request, or update the comment an earlier run posted (needs GITHUB_TOKEN with pull-requests: write)." };
var pullRequest = new Option<int?>("--pr") { Description = "Pull request number for --pr-comment (default: from the GitHub Actions event)." };
var repository = new Option<string?>("--repository") { Description = "Repository (owner/name) for --pr-comment (default: GITHUB_REPOSITORY)." };
var reportUrl = new Option<string?>("--report-url") { Description = "Link to the published HTML report, added to the Markdown summary (default: the GitHub Actions run)." };
var report = new Command("report", "Aggregate results into an HTML report (plus optional JUnit, Markdown, flake history and a pull-request comment).")
{
    results, output, junit, markdown, githubSummary, history, runId, failOnFailures, prComment, pullRequest, repository, reportUrl,
};
report.SetAction(async (parse, cancellationToken) =>
{
    var settings = Settings(parse);
    var directories = ResultDirectories(parse, settings);
    var summary = RunSummary.From(Read(directories));
    if (summary.Total == 0)
    {
        Console.Error.WriteLine($"No results found in: {string.Join(", ", directories)}");
        return 2;
    }

    FlakeReport? flakes = null;
    if (parse.GetValue(history) is { } historyPath)
    {
        var flakeHistory = FlakeHistory.Load(historyPath);
        flakeHistory.Add(parse.GetValue(runId) ?? Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? Environment.GetEnvironmentVariable("BUILD_BUILDID") ?? DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), summary.Results);
        flakeHistory.Save(historyPath);
        flakes = flakeHistory.Analyze();
    }

    var coverage = directories.Select(StoryCoverageReport.Load).OfType<StoryCoverageReport>().FirstOrDefault();
    var reportDirectory = parse.GetValue(output) ?? Path.Combine(directories[0], "report");
    var path = HtmlReportWriter.Write(summary, reportDirectory, new HtmlReportOptions { Flakes = flakes, Coverage = coverage });
    Console.WriteLine($"HTML report: {path}");

    if (parse.GetValue(junit) is { } junitPath)
    {
        JUnitReportWriter.Write(summary, junitPath);
        Console.WriteLine($"JUnit XML:   {Path.GetFullPath(junitPath)}");
    }

    var link = parse.GetValue(reportUrl) ?? GitHubRunUrl();
    var md = MarkdownSummaryWriter.Create(summary, flakes, link, coverage);
    if (parse.GetValue(markdown) is { } markdownPath)
    {
        File.WriteAllText(markdownPath, md);
        Console.WriteLine($"Markdown:    {Path.GetFullPath(markdownPath)}");
    }

    if (parse.GetValue(githubSummary) && Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
    {
        File.AppendAllText(stepSummary, md + Environment.NewLine);
    }

    if (parse.GetValue(prComment))
    {
        // A missing token or a read-only one (pull requests from forks) must not fail the build: the report is still there.
        using var http = new HttpClient();
        var comment = GitHubPullRequestComment.FromEnvironment(http, out var problem, parse.GetValue(pullRequest), parse.GetValue(repository));
        if (comment is null)
        {
            Console.WriteLine($"PR comment:  skipped. {problem}");
        }
        else
        {
            try
            {
                var posted = await comment.UpsertAsync(md, cancellationToken);
                Console.WriteLine($"PR comment:  {(posted.Created ? "posted" : "updated")} {posted.Url}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"PR comment:  skipped. {ex.Message}");
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is { Length: > 0 })
                {
                    Console.WriteLine($"::warning title=BlazorTestPlatform::Could not comment on the pull request. {ex.Message.ReplaceLineEndings(" ")}");
                }
            }
        }
    }

    PrintSummary(summary);
    return parse.GetValue(failOnFailures) && !summary.Success ? 1 : 0;
});
root.Add(report);

// btp approve
var filter = new Option<string?>("--filter", "-f") { Description = "Only approve snapshots/stories matching this glob (e.g. 'inputs-button--*')." };
var dryRun = new Option<bool>("--dry-run") { Description = "List what would be approved without changing files." };
var approve = new Command("approve", "Accept changed or missing visual snapshots from the last run as the new baselines.") { results, filter, dryRun };
approve.SetAction(parse =>
{
    var settings = Settings(parse);
    var pattern = parse.GetValue(filter);
    var candidates = Read(ResultDirectories(parse, settings))
        .Where(r => r.Visual is { ActualPath: not null } v && v.Status is VisualStatus.Mismatched or VisualStatus.SizeMismatch or VisualStatus.MissingBaseline)
        .Where(r => pattern is null || GlobMatch(r.StoryId, pattern) || GlobMatch(r.Visual!.SnapshotName, pattern))
        .GroupBy(r => r.Visual!.SnapshotName, StringComparer.Ordinal)
        .Select(g => g.First())
        .ToArray();

    if (candidates.Length == 0)
    {
        Console.WriteLine("Nothing to approve.");
        return 0;
    }

    foreach (var result in candidates)
    {
        var visual = result.Visual!;
        Console.WriteLine($"{(parse.GetValue(dryRun) ? "would approve" : "approved")}  {visual.SnapshotName}  ({visual.Status}, {visual.DiffPixels:N0} px)");
        if (!parse.GetValue(dryRun))
        {
            SnapshotStore.Approve(visual);
        }
    }

    Console.WriteLine(parse.GetValue(dryRun) ? $"{candidates.Length} snapshot(s) would be approved." : $"{candidates.Length} baseline(s) updated. Review and commit them.");
    return 0;
});
root.Add(approve);

// btp perf approve
var baselineFile = new Option<string?>("--baseline") { Description = "Performance baseline file (default from settings)." };
var perfApprove = new Command("approve", "Record the measured render metrics of the last run as the performance baseline.") { results, filter, baselineFile };
perfApprove.SetAction(parse =>
{
    var settings = Settings(parse);
    var store = new PerformanceBaselineStore(parse.GetValue(baselineFile) ?? settings.PerformanceBaselineFilePath);
    var pattern = parse.GetValue(filter);
    var measured = Read(ResultDirectories(parse, settings))
        .Where(r => r.Performance is not null && (pattern is null || GlobMatch(r.StoryId, pattern)))
        .ToArray();
    foreach (var result in measured)
    {
        store.Set(result.StoryId, PerformanceBaselineEntry.From(result.Performance!));
        Console.WriteLine($"recorded  {result.StoryId}  {result.Performance}");
    }

    Console.WriteLine($"{measured.Length} baseline(s) written to {store.FilePath}");
    return 0;
});
var perf = new Command("perf", "Render-performance baselines.") { perfApprove };
root.Add(perf);

// btp a11y status | init
var a11yStatus = new Command("status", "Show the state of manual accessibility reviews (and the gate states of the last review gate run).") { results };
a11yStatus.SetAction(parse =>
{
    var settings = Settings(parse);
    var store = new ManualReviewStore(settings.ReviewsDirectoryPath);
    var reviews = store.LoadAll().ToDictionary(r => r.Component, StringComparer.Ordinal);

    // Fingerprints need the components rendered, so gate states come from the last run of the review gate test.
    var gate = ResultDirectories(parse, settings).Select(ReviewGateReport.Load).OfType<ReviewGateReport>().MaxBy(g => g.EvaluatedAt);
    var states = gate?.Entries.ToDictionary(e => e.Component, StringComparer.Ordinal) ?? [];
    if (reviews.Count == 0 && states.Count == 0)
    {
        Console.WriteLine($"No reviews in {store.Directory}. Run the review gate test, then 'btp a11y init'.");
        return 0;
    }

    foreach (var component in reviews.Keys.Union(states.Keys).Order(StringComparer.Ordinal))
    {
        var state = states.TryGetValue(component, out var entry) ? entry.State.ToString() : "?";
        if (!reviews.TryGetValue(component, out var review))
        {
            Console.WriteLine($"{state,-10} {component,-40} no review file");
            continue;
        }

        var counts = review.Items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"{state,-10} {review.Component,-40} pass {counts.GetValueOrDefault(ReviewStatus.Pass),2}  fail {counts.GetValueOrDefault(ReviewStatus.Fail),2}  n/a {counts.GetValueOrDefault(ReviewStatus.NotApplicable),2}  pending {counts.GetValueOrDefault(ReviewStatus.NotReviewed),2}  {review.Reviewer ?? "-"} {review.ReviewedAt:yyyy-MM-dd}");
    }

    Console.WriteLine(gate is null
        ? "Gate states are unknown ('?'): run the review gate test (AccessibilityReviews.VerifyAsync) to compare fingerprints."
        : $"Gate states from the review gate run at {gate.EvaluatedAt:yyyy-MM-dd HH:mm} UTC.");
    return 0;
});
var force = new Option<bool>("--force") { Description = "Overwrite existing review files (stale reviews)." };
var a11yInit = new Command("init", "Copy review templates produced by the review gate test into the reviews directory.") { results, force };
a11yInit.SetAction(parse =>
{
    var settings = Settings(parse);
    var target = new ManualReviewStore(settings.ReviewsDirectoryPath);
    var copied = 0;
    foreach (var directory in ResultDirectories(parse, settings))
    {
        var templates = new ManualReviewStore(Path.Combine(directory, "a11y-review-templates"));
        foreach (var template in templates.LoadAll())
        {
            if (File.Exists(target.PathFor(template.Component)) && !parse.GetValue(force))
            {
                Console.WriteLine($"exists    {template.Component} (use --force to reset a stale review)");
                continue;
            }

            Console.WriteLine($"created   {target.Save(template)}");
            copied++;
        }
    }

    Console.WriteLine(copied == 0 ? "No templates found. Run the manual review gate test first." : $"{copied} review file(s) ready to fill in and commit.");
    return 0;
});
root.Add(new Command("a11y", "Manual accessibility review workflow.") { a11yStatus, a11yInit });

// btp flaky
var historyRequired = new Option<string>("--history") { Description = "Flake history file.", Required = true };
var flaky = new Command("flaky", "List the flakiest stories from a flake history file.") { historyRequired };
flaky.SetAction(parse =>
{
    var analysis = FlakeHistory.Load(parse.GetRequiredValue(historyRequired)).Analyze();
    Console.WriteLine($"{analysis.Runs} runs analysed.");
    foreach (var entry in analysis.Entries.Take(25))
    {
        Console.WriteLine($"{entry.FlakeRate,6:P0}  {entry.Unstable,3}/{entry.Runs,-3}  {entry.Key}");
    }

    return 0;
});
root.Add(flaky);

// btp clean
var clean = new Command("clean", "Delete the results directory (baselines are kept).");
clean.SetAction(parse =>
{
    var settings = Settings(parse);
    settings.CreateResultsStore().Clear();
    Console.WriteLine($"Deleted {settings.ResultsDirectoryPath}");
    return 0;
});
root.Add(clean);

return await root.Parse(args).InvokeAsync();

BtpSettings Settings(ParseResult parse) => BtpSettings.Load(Path.GetFullPath(parse.GetValue(project) ?? Environment.CurrentDirectory));

string[] ResultDirectories(ParseResult parse, BtpSettings settings) =>
    parse.GetValue(results) is { Length: > 0 } explicitDirectories ? explicitDirectories.Select(Path.GetFullPath).ToArray() : [settings.ResultsDirectoryPath];

string? GitHubRunUrl() =>
    Environment.GetEnvironmentVariable("GITHUB_SERVER_URL") is { Length: > 0 } server
    && Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") is { Length: > 0 } repo
    && Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { Length: > 0 } run
        ? $"{server}/{repo}/actions/runs/{run}"
        : null;

IReadOnlyList<StoryResult> Read(IEnumerable<string> directories) => directories.SelectMany(d => new ResultsStore(d).ReadAll()).ToArray();

bool GlobMatch(string input, string pattern) =>
    System.Text.RegularExpressions.Regex.IsMatch(input, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

void PrintSummary(RunSummary summary)
{
    Console.WriteLine();
    Console.WriteLine($"  passed {summary.Passed}   failed {summary.Failed}   flaky {summary.Flaky}   skipped {summary.Skipped}   visual changes {summary.VisualChanges}   a11y {summary.AccessibilityFailures}   perf {summary.PerformanceRegressions}");
    foreach (var failed in summary.Results.Where(r => r.Outcome == StoryOutcome.Failed).Take(20))
    {
        var message = failed.Error ?? string.Empty;
        var newline = message.IndexOf('\n', StringComparison.Ordinal);
        Console.WriteLine($"  ✗ {failed.Key}: {(newline < 0 ? message : message[..newline].TrimEnd())}");
    }
}
