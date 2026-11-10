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
            Waivers = [new AccessibilityWaiver { RuleId = "image-alt", Source = AccessibilityWaiverSource.Story, Justification = "Decorative logo, tracked in #123" }],
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
    public void JUnit_names_tell_configurations_of_one_story_apart()
    {
        var xml = JUnitReportWriter.Create(RunSummary.From(
        [
            Result("a", StoryOutcome.Passed),
            Result("a", StoryOutcome.Passed) with { Key = "a|bunit|Mobile|Dark", Viewport = "Mobile", Theme = "Dark" },
            Result("a", StoryOutcome.Passed) with { Key = "a|bunit|args-1", Args = new Dictionary<string, string?> { ["Label"] = "Go" } },
        ]));

        var names = xml.Descendants("testcase").Select(c => c.Attribute("name")!.Value).ToArray();
        Assert.Equal(3, names.Distinct().Count());
        Assert.Contains("a [bunit, server, Label=Go]", names);
    }

    [Fact]
    public void Markdown_lists_failures_and_flakes_safely()
    {
        var md = MarkdownSummaryWriter.Create(Summary());

        Assert.Contains("❌", md, StringComparison.Ordinal);
        Assert.Contains("| 1 | 1 | 1 | 0 | 1 | 1 | 1 | 0 |", md, StringComparison.Ordinal);
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
        Assert.Contains("Decorative logo, tracked in #123", html, StringComparison.Ordinal);
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
    public void Flips_tell_intermittent_failures_from_a_regression_that_was_fixed()
    {
        var history = FlakeHistory.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
        for (var run = 0; run < 10; run++)
        {
            history.Add($"run-{run}",
            [
                Result("regressed", run is >= 3 and < 8 ? StoryOutcome.Failed : StoryOutcome.Passed),
                Result("intermittent", run % 2 == 0 ? StoryOutcome.Failed : StoryOutcome.Passed),
            ]);
        }

        var entries = history.Analyze().Entries.ToDictionary(e => e.Key[..e.Key.IndexOf('|', StringComparison.Ordinal)]);

        Assert.Equal(2, entries["regressed"].Flips);
        Assert.Equal(9, entries["intermittent"].Flips);
    }

    [Fact]
    public void Sarif_reports_accessibility_failures_visual_and_story_failures_at_the_story_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "btp-sarif-repo");
        var source = Path.Combine(root, "stories", "ButtonStories.cs");
        var failing = Result("b", StoryOutcome.Failed, visual: new VisualResult { SnapshotName = "b/desktop", Status = VisualStatus.Mismatched, DiffPixels = 10, DiffRatio = 0.01 })
            with { SourceFile = source, SourceLine = 12 };
        var crashed = Result("c", StoryOutcome.Failed) with { Accessibility = null, AccessibilityFailures = [], SourceFile = "/_/stories/CardStories.cs", SourceLine = 3 };

        var sarif = System.Text.Json.JsonDocument.Parse(SarifReportWriter.Create(RunSummary.From([failing, crashed]), new SarifOptions { SourceRoot = root }));

        var run = sarif.RootElement.GetProperty("runs")[0];
        Assert.Equal("2.1.0", sarif.RootElement.GetProperty("version").GetString());
        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToArray();
        Assert.Contains("color-contrast", rules);
        Assert.Contains(SarifReportWriter.VisualRule, rules);

        var results = run.GetProperty("results").EnumerateArray().ToArray();
        var contrast = results.Single(r => r.GetProperty("ruleId").GetString() == "color-contrast");
        Assert.Equal("error", contrast.GetProperty("level").GetString());
        var location = contrast.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("stories/ButtonStories.cs", location.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(12, location.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.Single(results, r => r.GetProperty("ruleId").GetString() == SarifReportWriter.VisualRule);

        // A failure without accessibility or visual cause is a failed story, located through the deterministic /_/ path.
        var storyFailure = results.Single(r => r.GetProperty("ruleId").GetString() == SarifReportWriter.StoryFailedRule);
        Assert.Equal("stories/CardStories.cs", storyFailure.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.GetProperty("partialFingerprints").GetProperty("btpIdentity/v1").GetString())));
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

    [Fact]
    public void JUnit_survives_control_characters_and_ansi_colours()
    {
        var path = Path.Combine(Path.GetTempPath(), "btp-junit-tests", Guid.NewGuid().ToString("N") + ".xml");
        var result = Result("ansi", StoryOutcome.Failed) with { Error = "\u001b[31mred\u001b[0m failure\0 with NUL \u0007 bell", Name = "name\u0001" };

        JUnitReportWriter.Write(RunSummary.From([result]), path);

        var failure = XDocument.Load(path).Descendants("failure").Single();
        Assert.Equal("red failure� with NUL � bell", failure.Attribute("message")!.Value);
        Assert.Equal("😀", JUnitReportWriter.Text("😀"));
        Assert.Equal("�", JUnitReportWriter.Text("\uD83D"));
    }

    [Fact]
    public void Markdown_neutralizes_mentions_code_spans_and_table_breaks()
    {
        Assert.Equal("&#64;media \\` a\\|b &lt;i>", MarkdownSummaryWriter.Escape("@media ` a|b <i>"));

        var flakes = new FlakeReport(3, [new FlakeEntry("button--primary|chromium|desktop|light|server", 3, 1, 1)]);
        var md = MarkdownSummaryWriter.Create(Summary(), flakes);

        Assert.Contains("| button--primary\\|chromium\\|desktop\\|light\\|server | 67% | 2/3 |", md.Replace(" %", "%", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_flake_history_is_reset_with_a_warning_instead_of_breaking_reports()
    {
        var path = Path.Combine(Path.GetTempPath(), "btp-history-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[{\"runId\":\"run-1\",\"outco");

        var history = FlakeHistory.Load(path, out var problem);

        Assert.Empty(history.Runs);
        Assert.Contains("unreadable", problem, StringComparison.Ordinal);
        history.Add("run-2", [Result("a", StoryOutcome.Passed)]);
        history.Save(path);
        Assert.Single(FlakeHistory.Load(path).Runs);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void Html_report_links_only_http_help_urls()
    {
        var unsafeLink = Result("x", StoryOutcome.Failed) with
        {
            Accessibility = new AccessibilityReport
            {
                Engine = "axe-core",
                Violations = [new AccessibilityViolation { RuleId = "r", Impact = AccessibilityImpact.Serious, Help = "Help text", HelpUrl = "javascript:alert(1)", Nodes = [new AccessibilityNode(".a", "<a>")] }],
            },
        };
        var directory = Path.Combine(Path.GetTempPath(), "btp-report-tests", Guid.NewGuid().ToString("N"));

        var html = File.ReadAllText(HtmlReportWriter.Write(RunSummary.From([unsafeLink]), directory));

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Help text", html, StringComparison.Ordinal);
    }
}
