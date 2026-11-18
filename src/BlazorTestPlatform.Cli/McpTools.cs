using System.ComponentModel;
using System.Globalization;
using System.Text;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Coverage;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;
using ModelContextProtocol.Server;

namespace BlazorTestPlatform.Cli;

/// <summary>Where <c>btp mcp</c> reads: the results directories and the project's settings (snapshot directory).</summary>
internal sealed class McpResults(IReadOnlyList<string> directories, BtpSettings settings)
{
    public IReadOnlyList<string> Directories { get; } = directories;

    public BtpSettings Settings { get; } = settings;

    // Read on every call: the agent asks after it reruns the tests.
    public IReadOnlyList<StoryResult> Read() => [.. Directories.Where(Directory.Exists).SelectMany(d => new ResultsStore(d).ReadAll())];
}

/// <summary>
/// Tools <c>btp mcp</c> offers coding agents over the Model Context Protocol. They read the results the last test run
/// wrote (nothing runs and nothing changes), and answer in plain text written for a model: what failed, why, where the
/// evidence is, and the command that fixes or accepts it.
/// </summary>
[McpServerToolType]
internal sealed class BtpMcpTools(McpResults results)
{
    private const int MaxListed = 200;

    [McpServerTool(Name = "run_summary", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Summary of the last BlazorTestPlatform run: counts by outcome, visual changes, accessibility and performance failures, and the failed results. Start here.")]
    public string RunSummary()
    {
        var all = results.Read();
        if (all.Count == 0)
        {
            return NoResults();
        }

        var summary = Reporting.RunSummary.From(all);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{summary.Total} results: {summary.Passed} passed, {summary.Failed} failed, {summary.Flaky} flaky, {summary.Skipped} skipped, {summary.Quarantined} quarantined failures.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Visual changes: {summary.VisualChanges}. Accessibility failures: {summary.AccessibilityFailures}. Performance regressions: {summary.PerformanceRegressions}.");
        var failed = all.Where(r => r.IsBlockingFailure).OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
        if (failed.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Failed (get_failure with a key for details):");
            foreach (var result in failed.Take(MaxListed))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {result.Key}: {FirstLine(result.Error)}");
            }
        }

        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"Results read from: {string.Join(", ", results.Directories)}");
        return text.ToString();
    }

    [McpServerTool(Name = "list_results", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists results of the last run, one line each (key, outcome, first line of the error), filtered by outcome and by text in the story id, title or key.")]
    public string ListResults(
        [Description("passed, failed, flaky or skipped; omit for all.")] string? outcome = null,
        [Description("Text the story id, title, name or result key contains (case-insensitive).")] string? search = null,
        [Description("At most this many lines (default 50, max 200).")] int limit = 50)
    {
        var all = results.Read();
        if (all.Count == 0)
        {
            return NoResults();
        }

        StoryOutcome? wanted = null;
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            if (!Enum.TryParse<StoryOutcome>(outcome, ignoreCase: true, out var parsed))
            {
                return $"Unknown outcome '{outcome}'. Use passed, failed, flaky or skipped.";
            }

            wanted = parsed;
        }

        var matching = all
            .Where(r => wanted is null || r.Outcome == wanted)
            .Where(r => string.IsNullOrWhiteSpace(search) || $"{r.Key} {r.StoryId} {r.Title} {r.Name}".Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToArray();
        var shown = matching.Take(Math.Clamp(limit, 1, MaxListed)).ToArray();
        var text = new StringBuilder();
        foreach (var result in shown)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{result.Key} [{result.Outcome.ToString().ToLowerInvariant()}]{(result.Error is null ? string.Empty : ": " + FirstLine(result.Error))}");
        }

        text.Append(CultureInfo.InvariantCulture, $"{shown.Length} of {matching.Length} matching result(s).");
        return text.ToString();
    }

    [McpServerTool(Name = "get_failure", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Everything about one result: story, run cell, failure category and likely cause, error, last steps, console, accessibility violations, visual differences, artifacts (screenshot, DOM, trace paths) and how to reproduce it.")]
    public string GetFailure([Description("The result key (from run_summary or list_results), or a story id for its first failed result.")] string key)
    {
        var all = results.Read();
        var result = all.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal))
            ?? all.Where(r => string.Equals(r.StoryId, key, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.IsBlockingFailure).FirstOrDefault();
        if (result is null)
        {
            return $"No result with key or story id '{key}'. Use list_results to find keys.";
        }

        var text = new StringBuilder();
        text.Append(FailureAnalysis.ToPrompt(result));
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Outcome: {result.Outcome}{(result.ExpectedFailure is { } expected ? $" (expected failure: {expected})" : string.Empty)}{(result.Quarantine is not null ? " (quarantined)" : string.Empty)}");
        foreach (var attempt in result.AttemptErrors)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Earlier attempt: {FirstLine(attempt)}");
        }

        if (result.Accessibility is { Violations.Count: > 0 } accessibility)
        {
            text.AppendLine();
            text.AppendLine("Accessibility violations:");
            foreach (var violation in accessibility.Violations.Take(20))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- [{violation.Impact}] {violation.RuleId}: {violation.Help} ({string.Join(", ", violation.Nodes.Take(3).Select(n => n.Target))})");
            }
        }

        var visual = result.VisualComparisons().Where(v => v.IsFailure).ToArray();
        if (visual.Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Visual differences:");
            foreach (var comparison in visual)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {comparison.SnapshotName}: {comparison.Status}{(comparison.DiffRatio is { } ratio ? $", {ratio:P2} of pixels" : string.Empty)}; baseline {comparison.BaselinePath ?? "-"}, actual {comparison.ActualPath ?? "-"}, diff {comparison.DiffPath ?? "-"}");
            }
        }

        foreach (var mismatch in result.AriaSnapshotMismatches)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Accessibility snapshot mismatch at {mismatch.SourceFile}:{mismatch.SourceLine} ('btp snapshots update-source' rewrites it).");
        }

        if (result.Artifacts.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Artifacts:");
            foreach (var artifact in result.Artifacts)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {artifact.Kind}: {artifact.Path}{(artifact.Description is null ? string.Empty : $" ({artifact.Description})")}");
            }
        }

        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"Rerun just this story: BTP_GREP=\"^{result.StoryId}$\" dotnet test (or BTP_LAST_FAILED=1 for every failure).");
        return text.ToString();
    }

    [McpServerTool(Name = "failures_by_cause", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Groups the failed results by normalized error signature and category (product bug, test, environment...), largest group first, so one root cause is fixed once.")]
    public string FailuresByCause()
    {
        var failed = results.Read().Where(r => r.Outcome == StoryOutcome.Failed).ToArray();
        if (failed.Length == 0)
        {
            return "No failed results.";
        }

        var text = new StringBuilder();
        foreach (var group in failed.GroupBy(r => FailureAnalysis.Signature(r) ?? "(no message)").OrderByDescending(g => g.Count()))
        {
            var explanation = FailureAnalysis.Explain(FailureAnalysis.Categorize(group.First()));
            text.AppendLine(CultureInfo.InvariantCulture, $"{group.Count()} x {explanation.Label}: {group.Key}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  Likely cause: {explanation.LikelyCause} Next step: {explanation.NextStep}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  Results: {string.Join(", ", group.Select(r => r.Key).Take(10))}{(group.Count() > 10 ? ", ..." : string.Empty)}");
        }

        return text.ToString().TrimEnd();
    }

    [McpServerTool(Name = "pending_reviews", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("What waits for a human decision: changed or missing visual baselines, received markup snapshots, accessibility snapshot mismatches, with the command that accepts each kind.")]
    public string PendingReviews()
    {
        var all = results.Read();
        var text = new StringBuilder();
        var visual = all.SelectMany(r => r.VisualComparisons().Where(v => v.IsFailure).Select(v => (Result: r, Comparison: v))).ToArray();
        if (visual.Length > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Visual baselines ({visual.Length}; accept with 'btp approve', or review in the hub):");
            foreach (var (result, comparison) in visual.Take(MaxListed))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {result.Key} {comparison.SnapshotName}: {comparison.Status}, diff {comparison.DiffPath ?? "-"}");
            }
        }

        var received = Directory.Exists(results.Settings.SnapshotDirectoryPath)
            ? Directory.EnumerateFiles(results.Settings.SnapshotDirectoryPath, "*" + MarkupSnapshots.ReceivedSuffix, SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (received.Length > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Markup snapshots ({received.Length}; accept with 'btp snapshots accept'):");
            foreach (var path in received.Take(MaxListed))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {path} (compare with {path[..^MarkupSnapshots.ReceivedSuffix.Length] + MarkupSnapshots.Suffix})");
            }
        }

        var aria = all.SelectMany(r => r.AriaSnapshotMismatches).ToArray();
        if (aria.Length > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Accessibility snapshots in sources ({aria.Length}; rewrite with 'btp snapshots update-source'):");
            foreach (var mismatch in aria.Take(MaxListed))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {mismatch.SourceFile}:{mismatch.SourceLine}");
            }
        }

        return text.Length == 0 ? "Nothing waits for review." : text.ToString().TrimEnd();
    }

    [McpServerTool(Name = "story_coverage", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Story state coverage: per component, the bool and enum parameter values no story renders, components without stories, and code coverage when it was merged. Use it to decide which stories to write.")]
    public string StoryCoverage()
    {
        var coverage = results.Directories.Where(Directory.Exists).Select(StoryCoverageReport.Load).OfType<StoryCoverageReport>().FirstOrDefault();
        if (coverage is null)
        {
            return $"No {StoryCoverageReport.FileName} in the results: a test saves it (StoryCoverage.Analyze(catalog).Save(...)).";
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{coverage.CoveredStates}/{coverage.TotalStates} states ({coverage.Ratio:P0}){(coverage.Code is { } code ? $"; code {code.LineRate:P0} of lines" : string.Empty)}.");
        foreach (var component in coverage.Components.Where(c => c.MissingStates.Any() || c.Code is { LineRate: < 0.8 }))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {component.Title}: {(component.MissingStates.Any() ? "no story for " + string.Join(", ", component.MissingStates) : "every state has a story")}{(component.Code is { } c ? $"; code {c.LineRate:P0} of lines" : string.Empty)}");
        }

        foreach (var missing in coverage.ComponentsWithoutStories)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {missing}: no stories");
        }

        return text.ToString().TrimEnd();
    }

    private string NoResults() =>
        $"No results in {string.Join(", ", results.Directories)}. Run the tests first (dotnet test), or start btp mcp with --results <directory>.";

    private static string FirstLine(string? text) =>
        text is null ? string.Empty : text.Split('\n', 2)[0].Trim() is { Length: > 200 } line ? line[..200] + "…" : text.Split('\n', 2)[0].Trim();
}
