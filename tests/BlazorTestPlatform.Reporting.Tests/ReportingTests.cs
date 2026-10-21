using System.Xml.Linq;
using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting.Tests;

public sealed class ReportingTests
{
    private static StoryResult Result(string id, StoryOutcome outcome, string driver = "bunit", VisualResult? visual = null) => new()
    {
        Key = StoryResult.CreateKey(id, driver),
        StoryId = id,
        Title = "Inputs/Button",
        Name = id,
        Driver = driver,
        HostingModel = "server",
        Outcome = outcome,
        Error = outcome == StoryOutcome.Failed ? "Expected <b> | x\nsecond line" : null,
        Attempts = outcome == StoryOutcome.Flaky ? 2 : 1,
        AttemptErrors = outcome == StoryOutcome.Flaky ? ["attempt 1: timeout"] : [],
        Visual = visual,
        Steps = [new StepRecord { Title = "play", Kind = StepKind.Step, Children = [new StepRecord { Title = "click role=button", Kind = StepKind.Action, Error = outcome == StoryOutcome.Failed ? "boom" : null }] }],
        Accessibility = new AccessibilityReport
        {
            Engine = "axe-core 4.x",
            Violations = [new AccessibilityViolation { RuleId = "color-contrast", Impact = AccessibilityImpact.Serious, Help = "Elements must meet contrast", HelpUrl = "https://example.org/rule", Nodes = [new AccessibilityNode(".btn", "<button>")] }],
        },
        AccessibilityFailures = outcome == StoryOutcome.Failed ? ["color-contrast"] : [],
    };

    private static RunSummary Summary() => RunSummary.From(
    [
        Result("a", StoryOutcome.Passed),
        Result("b", StoryOutcome.Failed, visual: new VisualResult { SnapshotName = "b/desktop", Status = VisualStatus.Mismatched, DiffPixels = 10, DiffRatio = 0.01 }),
        Result("c", StoryOutcome.Flaky, "chromium"),
        Result("d", StoryOutcome.Skipped) with { SkipReason = "needs layout" },
    ]);

    [Fact]
    public void Summary_counts_outcomes_and_quality_gates()
    {
        var summary = Summary();

        Assert.Equal((1, 1, 1, 1), (summary.Passed, summary.Failed, summary.Flaky, summary.Skipped));
        Assert.Equal(1, summary.VisualChanges);
        Assert.Equal(1, summary.AccessibilityFailures);
        Assert.False(summary.Success);
        Assert.Equal(["bunit", "chromium"], summary.Drivers);
    }

    [Fact]
    public void JUnit_marks_failures_skips_and_flakes()
    {
        var xml = JUnitReportWriter.Create(Summary());
        var cases = xml.Descendants("testcase").ToArray();

        Assert.Equal("4", xml.Root!.Attribute("tests")!.Value);
        Assert.Single(cases, c => c.Element("failure") is not null);
        Assert.Single(cases, c => c.Element("skipped") is not null);
        Assert.Contains(cases.SelectMany(c => c.Elements("system-out")), e => e.Value.StartsWith("FLAKY", StringComparison.Ordinal));
        Assert.Equal("Expected <b> | x", cases.Single(c => c.Element("failure") is not null).Element("failure")!.Attribute("message")!.Value);
        _ = XDocument.Parse(xml.ToString());
    }

    [Fact]
    public void Markdown_lists_failures_and_flakes_safely()
    {
        var md = MarkdownSummaryWriter.Create(Summary());

        Assert.Contains("❌", md, StringComparison.Ordinal);
        Assert.Contains("| 1 | 1 | 1 | 1 | 1 | 1 | 0 |", md, StringComparison.Ordinal);
        Assert.Contains("Expected &lt;b> \\| x", md, StringComparison.Ordinal);
        Assert.Contains("### Flaky", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_report_is_written_and_encoded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-report-tests", Guid.NewGuid().ToString("N"));
        var path = HtmlReportWriter.Write(Summary(), directory);
        var html = File.ReadAllText(path);

        Assert.Contains("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("Expected &lt;b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected <b>", html, StringComparison.Ordinal);
        Assert.Contains("color-contrast", html, StringComparison.Ordinal);
        Assert.Contains("data-tags=\"failed visual\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Flake_history_ranks_intermittent_failures_but_not_broken_ones()
    {
        var history = FlakeHistory.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
        for (var run = 0; run < 10; run++)
        {
            history.Add($"run-{run}",
            [
                Result("stable", StoryOutcome.Passed),
                Result("intermittent", run % 3 == 0 ? StoryOutcome.Failed : run % 3 == 1 ? StoryOutcome.Flaky : StoryOutcome.Passed),
                Result("broken", StoryOutcome.Failed),
            ]);
        }

        var report = history.Analyze();

        Assert.Equal(10, report.Runs);
        var entry = Assert.Single(report.Entries);
        Assert.Equal(StoryResult.CreateKey("intermittent", "bunit"), entry.Key);
        Assert.Equal(0.7, entry.FlakeRate, precision: 3);
    }

    [Fact]
    public void Flake_history_is_bounded_and_persisted()
    {
        var path = Path.Combine(Path.GetTempPath(), "btp-history-tests", Guid.NewGuid().ToString("N") + ".json");
        var history = FlakeHistory.Load(path);
        for (var run = 0; run < 5; run++)
        {
            history.Add($"run-{run}", [Result("a", StoryOutcome.Passed)], capacity: 3);
        }

        history.Save(path);
        var reloaded = FlakeHistory.Load(path);

        Assert.Equal(["run-2", "run-3", "run-4"], reloaded.Runs.Select(r => r.RunId));
    }
}
