using System.Globalization;
using System.Text;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting;

/// <summary>Writes a compact Markdown summary (for <c>$GITHUB_STEP_SUMMARY</c> or PR comments).</summary>
public static class MarkdownSummaryWriter
{
    /// <summary>Builds the Markdown.</summary>
    public static string Create(RunSummary summary, FlakeReport? flakes = null, string? reportUrl = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var md = new StringBuilder();
        md.AppendLine(summary.Success ? "## ✅ BlazorTestPlatform: all stories passed" : $"## ❌ BlazorTestPlatform: {summary.Failed} failing");
        md.AppendLine();
        md.AppendLine("| Passed | Failed | Flaky | Skipped | Visual changes | A11y failures | Perf regressions |");
        md.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        md.AppendLine(CultureInfo.InvariantCulture, $"| {summary.Passed} | {summary.Failed} | {summary.Flaky} | {summary.Skipped} | {summary.VisualChanges} | {summary.AccessibilityFailures} | {summary.PerformanceRegressions} |");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"{summary.Stories} stories · drivers: {string.Join(", ", summary.Drivers)} · render modes: {string.Join(", ", summary.HostingModels)}");

        var failed = summary.Results.Where(r => r.Outcome == StoryOutcome.Failed).ToArray();
        if (failed.Length > 0)
        {
            md.AppendLine().AppendLine("### Failures").AppendLine();
            foreach (var result in failed.Take(30))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- **{result.Title} / {result.Name}** `{JUnitReportWriter.Cell(result)}` — {Escape(FirstLine(result.Error))}");
            }

            if (failed.Length > 30)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- …and {failed.Length - 30} more");
            }
        }

        var flaky = summary.Results.Where(r => r.Outcome == StoryOutcome.Flaky).ToArray();
        if (flaky.Length > 0)
        {
            md.AppendLine().AppendLine("### Flaky (passed on retry)").AppendLine();
            foreach (var result in flaky)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- {result.Title} / {result.Name} `{JUnitReportWriter.Cell(result)}` — {result.Attempts} attempts");
            }
        }

        if (flakes is { Entries.Count: > 0 })
        {
            md.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"### Flakiest over the last {flakes.Runs} runs").AppendLine();
            md.AppendLine("| Story | Flake rate | Unstable runs |").AppendLine("|---|---:|---:|");
            foreach (var entry in flakes.Entries.Take(10))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {entry.Key} | {entry.FlakeRate:P0} | {entry.Unstable}/{entry.Runs} |");
            }
        }

        if (reportUrl is not null)
        {
            md.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"[Open the full report]({reportUrl})");
        }

        return md.ToString();
    }

    private static string FirstLine(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var index = value.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? value : value[..index].TrimEnd('\r');
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
}
