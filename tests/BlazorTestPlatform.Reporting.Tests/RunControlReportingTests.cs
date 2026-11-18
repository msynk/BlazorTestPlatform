using System.Xml.Linq;
using BlazorTestPlatform.Reporting;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting.Tests;

public sealed class RunControlReportingTests
{
    private static StoryResult Result(string id, StoryOutcome outcome) => new()
    {
        Key = $"{id}|chromium|desktop|light|wasm",
        StoryId = id,
        Title = "Forms/Field",
        Name = id,
        Driver = "chromium",
        HostingModel = "wasm",
        Outcome = outcome,
    };

    private static VisualResult Shot(string name, VisualStatus status) => new()
    {
        SnapshotName = $"forms-field--a/desktop.light.chromium-linux.{name}",
        Status = status,
        DiffPixels = status == VisualStatus.Mismatched ? 42 : 0,
    };

    [Fact]
    public void JUnit_reports_flaky_and_rerun_attempts_the_way_surefire_does()
    {
        var flaky = Result("flaky", StoryOutcome.Flaky) with { Attempts = 2, AttemptErrors = ["attempt 1: timed out\nat step 3"] };
        var failed = Result("failed", StoryOutcome.Failed) with { Attempts = 3, Error = "still broken", AttemptErrors = ["attempt 1: broken", "attempt 2: broken"] };
        var expected = Result("expected", StoryOutcome.Passed) with { ExpectedFailure = "#42", Error = "focus lost" };

        var xml = JUnitReportWriter.Create(RunSummary.From([flaky, failed, expected]));
        var cases = xml.Descendants("testcase").ToDictionary(c => ((string)c.Attribute("name")!).Split(' ')[0]);

        var flakyFailure = Assert.Single(cases["flaky"].Elements("flakyFailure"));
        Assert.Equal("attempt 1: timed out", (string?)flakyFailure.Attribute("message"));
        Assert.Contains("at step 3", flakyFailure.Element("stackTrace")!.Value, StringComparison.Ordinal);
        Assert.Empty(cases["flaky"].Elements("failure"));

        Assert.Single(cases["failed"].Elements("failure"));
        Assert.Equal(2, cases["failed"].Elements("rerunFailure").Count());

        Assert.Empty(cases["expected"].Elements("failure"));
        Assert.Contains("EXPECTED FAILURE (#42): focus lost", cases["expected"].Element("system-out")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrf_and_html_name_expected_failures_and_unexpected_passes()
    {
        var expected = Result("expected", StoryOutcome.Passed) with { ExpectedFailure = "#42", Error = "focus lost" };
        var unexpected = Result("unexpected", StoryOutcome.Failed) with { ExpectedFailure = "#43", Error = "was expected to fail" };
        var summary = RunSummary.From([expected, unexpected]);

        using var json = System.Text.Json.JsonDocument.Parse(CtrfReportWriter.Create(summary));
        var tests = json.RootElement.GetProperty("results").GetProperty("tests").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        Assert.Equal("expected-failure", tests.Single(t => t.Key.Contains("expected", StringComparison.Ordinal) && !t.Key.Contains("unexpected", StringComparison.Ordinal)).Value.GetProperty("rawStatus").GetString());
        Assert.Equal("unexpected-pass", tests.Single(t => t.Key.Contains("unexpected", StringComparison.Ordinal)).Value.GetProperty("rawStatus").GetString());

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-report-run-control", Guid.NewGuid().ToString("N"))).FullName;
        var html = File.ReadAllText(HtmlReportWriter.Write(summary, directory));
        Assert.Contains(">expected failure</span>", html, StringComparison.Ordinal);
        Assert.Contains(">unexpectedly passed</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_screenshots_count_as_visual_changes_everywhere()
    {
        var result = Result("a", StoryOutcome.Failed) with
        {
            Error = "Screenshot 'forms-field--a/desktop.light.chromium-linux.menu-open' differs by 42 px (0.01%).",
            Visual = Shot("x", VisualStatus.Matched) with { SnapshotName = "forms-field--a/desktop.light.chromium-linux" },
            Screenshots = [Shot("menu-open", VisualStatus.Mismatched), Shot("closed", VisualStatus.Matched)],
        };

        Assert.Equal(3, result.VisualComparisons().Count());
        var summary = RunSummary.From([result]);
        Assert.Equal(1, summary.VisualChanges);
        Assert.Equal(FailureCategory.Visual, FailureAnalysis.Categorize(result));

        var sarif = System.Text.Json.JsonDocument.Parse(SarifReportWriter.Create(summary, new SarifOptions()));
        var messages = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().Select(r => r.GetProperty("message").GetProperty("text").GetString()).ToArray();
        Assert.Contains(messages, m => m!.Contains("menu-open", StringComparison.Ordinal) && m.Contains("differs by 42 px", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m!.Contains("failed [", StringComparison.Ordinal));

        // A soft screenshot mismatch next to a different, hard failure: both reach code scanning.
        var both = System.Text.Json.JsonDocument.Parse(SarifReportWriter.Create(RunSummary.From([result with { Error = "Expected role=button to be visible" }]), new SarifOptions()))
            .RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().Select(r => r.GetProperty("message").GetProperty("text").GetString()).ToArray();
        Assert.Contains(both, m => m!.Contains("menu-open", StringComparison.Ordinal));
        Assert.Contains(both, m => m!.Contains("Expected role=button to be visible", StringComparison.Ordinal));

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-report-run-control", Guid.NewGuid().ToString("N"))).FullName;
        var html = File.ReadAllText(HtmlReportWriter.Write(summary, directory));
        Assert.Contains("screenshots: 2, 1 changed", html, StringComparison.Ordinal);
        Assert.Contains("chromium-linux.closed</code>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Story_coverage_shows_code_coverage_when_merged()
    {
        var coverage = new BlazorTestPlatform.Coverage.StoryCoverageReport
        {
            Components = [new BlazorTestPlatform.Coverage.ComponentCoverage { Title = "Inputs/Button", Component = "MyLib.Button", Stories = 2, Code = new BlazorTestPlatform.Coverage.CodeCoverage(30, 40, 3, 4) }],
            ComponentsWithoutStories = ["MyLib.Grid"],
        };
        var directory = Path.Combine(Path.GetTempPath(), "btp-report-code", Guid.NewGuid().ToString("N"));

        var html = File.ReadAllText(HtmlReportWriter.Write(RunSummary.From([Result("a", StoryOutcome.Passed)]), directory, new HtmlReportOptions { Coverage = coverage }));
        var without = File.ReadAllText(HtmlReportWriter.Write(RunSummary.From([Result("a", StoryOutcome.Passed)]), directory + "-plain", new HtmlReportOptions { Coverage = coverage with { Components = [coverage.Components[0] with { Code = null }] } }));

        Assert.Contains("code 75 % of lines", html, StringComparison.Ordinal);
        Assert.Contains("<th class=\"num\">Lines</th><th class=\"num\">Branches</th>", html, StringComparison.Ordinal);
        Assert.Contains("title=\"30/40 lines\">75 %</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th class=\"num\">Lines</th>", without, StringComparison.Ordinal);
    }
}
