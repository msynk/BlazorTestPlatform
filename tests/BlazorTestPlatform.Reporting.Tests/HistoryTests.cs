using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Reporting.Tests;

/// <summary>Run archive, stability, smart tags, error clusters, comparisons and accessibility baselines.</summary>
public sealed class HistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static StoryResult Result(string id, StoryOutcome outcome, string? error = null, double duration = 100, params AccessibilityViolation[] violations) => new()
    {
        Key = StoryResult.CreateKey(id, "bunit"),
        StoryId = id,
        Title = "Inputs/" + id,
        Name = id,
        Driver = "bunit",
        Outcome = outcome,
        Attempts = outcome == StoryOutcome.Flaky ? 2 : 1,
        DurationMilliseconds = duration,
        Error = outcome == StoryOutcome.Failed ? error ?? "Expected role=button to be focused" : null,
        ErrorType = outcome == StoryOutcome.Failed ? "BlazorTestPlatform.StoryAssertionException" : null,
        StartedAt = Start,
        Accessibility = violations.Length == 0 ? null : new AccessibilityReport { Engine = "axe", Violations = violations },
    };

    private static RunRecord Run(int index, params StoryResult[] results) => RunRecord.Create(
        new RunInfo { RunId = $"run-{index:00}", Trigger = "test", StartedAt = Start.AddHours(index), FinishedAt = Start.AddHours(index) },
        results.Select(r => r with { StartedAt = Start.AddHours(index) }));

    private static AccessibilityViolation Violation(string rule, string target) =>
        new() { RuleId = rule, Impact = AccessibilityImpact.Serious, Tags = ["wcag143"], Nodes = [new AccessibilityNode(target, "<x>")] };

    [Fact]
    public void Statuses_follow_the_outcome_pattern()
    {
        var outcomes = new Dictionary<string, StoryOutcome[]>
        {
            ["healthy"] = [StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Passed],
            ["flaky"] = [StoryOutcome.Passed, StoryOutcome.Failed, StoryOutcome.Passed, StoryOutcome.Flaky],
            ["broken"] = [StoryOutcome.Passed, StoryOutcome.Failed, StoryOutcome.Failed, StoryOutcome.Failed],
            ["failing"] = [StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Failed],
            ["regressed-then-fixed"] = [StoryOutcome.Passed, StoryOutcome.Failed, StoryOutcome.Failed, StoryOutcome.Passed],
        };
        var runs = Enumerable.Range(0, 4).Select(i => Run(i, [.. outcomes.Select(o => Result(o.Key, o.Value[i]))])).ToArray();

        var stability = new RunHistory(runs).Stability().ToDictionary(s => s.Last.StoryId);

        Assert.Equal(Stability.Healthy, stability["healthy"].Status);
        Assert.Equal(Stability.Flaky, stability["flaky"].Status);
        Assert.Equal(0.5, stability["flaky"].FlakeRate);
        Assert.Equal(FlakeSeverity.Medium, stability["flaky"].Severity);
        Assert.InRange(stability["flaky"].FlakeRateLow, 0.1, 0.5);
        Assert.InRange(stability["flaky"].FlakeRateHigh, 0.5, 0.95);
        Assert.Equal(Stability.Broken, stability["broken"].Status);
        Assert.Equal(0, stability["broken"].FlakeRate);
        Assert.Contains(ResultTag.StillFailing, stability["broken"].Tags);
        Assert.Equal(Stability.Failing, stability["failing"].Status);
        Assert.Contains(ResultTag.NewFailure, stability["failing"].Tags);

        // One stretch of failures followed by a pass is a fixed regression, not flakiness.
        Assert.Equal(Stability.Fixed, stability["regressed-then-fixed"].Status);
        Assert.Contains(ResultTag.Fixed, stability["regressed-then-fixed"].Tags);

        // Least stable first.
        Assert.Equal("broken", new RunHistory(runs).Stability()[0].Last.StoryId);
    }

    [Fact]
    public void Five_clean_runs_after_instability_count_as_fixed_and_new_keys_as_new()
    {
        StoryOutcome[] outcomes = [StoryOutcome.Failed, StoryOutcome.Passed, StoryOutcome.Failed, StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Passed, StoryOutcome.Passed];
        var runs = outcomes.Select((o, i) => Run(i, i == outcomes.Length - 1 ? [Result("a", o), Result("brand-new", StoryOutcome.Passed)] : [Result("a", o)])).ToArray();

        var history = new RunHistory(runs);

        Assert.Equal(Stability.Fixed, history.StabilityOf(StoryResult.CreateKey("a", "bunit"))!.Status);
        var fresh = history.StabilityOf(StoryResult.CreateKey("brand-new", "bunit"))!;
        Assert.Equal(Stability.New, fresh.Status);
        Assert.Contains(ResultTag.NewTest, fresh.Tags);
    }

    [Fact]
    public void Failures_with_the_same_cause_cluster_across_stories_and_runs()
    {
        var runs = new[]
        {
            Run(0, Result("a", StoryOutcome.Failed, "Expected role=button to have text \"Saved\" (waited 5000 ms)"), Result("b", StoryOutcome.Passed)),
            Run(1, Result("a", StoryOutcome.Failed, "Expected role=button to have text \"Saved\" (waited 5012 ms)"), Result("b", StoryOutcome.Failed, "Story timed out after 30 s.")),
        };

        var clusters = new RunHistory(runs).Clusters();

        var assertion = Assert.Single(clusters, c => c.Category == FailureCategory.Assertion);
        Assert.Equal(2, assertion.Occurrences);
        Assert.Equal("run-00", assertion.FirstSeenRunId);
        Assert.True(assertion.Active);
        Assert.Single(clusters, c => c.Category == FailureCategory.Timeout);
    }

    [Fact]
    public void Comparison_sorts_keys_into_branch_review_buckets()
    {
        var @base = Run(0, Result("still", StoryOutcome.Failed), Result("fixed", StoryOutcome.Failed), Result("new-failure", StoryOutcome.Passed), Result("removed", StoryOutcome.Passed), Result("slow", StoryOutcome.Passed, duration: 100));
        var head = Run(1, Result("still", StoryOutcome.Failed), Result("fixed", StoryOutcome.Passed), Result("new-failure", StoryOutcome.Failed), Result("added", StoryOutcome.Passed), Result("slow", StoryOutcome.Passed, duration: 900));

        var comparison = RunHistory.Compare(@base, head);

        static string[] Ids(IEnumerable<ComparedResult> results) => results.Select(r => r.Latest.StoryId).ToArray();
        Assert.Equal(["new-failure"], Ids(comparison.NewFailures));
        Assert.Equal(["still"], Ids(comparison.StillFailing));
        Assert.Equal(["fixed"], Ids(comparison.Fixed));
        Assert.Equal(["added"], Ids(comparison.Added));
        Assert.Equal(["removed"], Ids(comparison.Removed));
        Assert.Equal(["slow"], Ids(comparison.Slower));
    }

    [Fact]
    public void Accessibility_baseline_tells_new_existing_and_fixed_violations_apart()
    {
        var @base = Run(0, Result("a", StoryOutcome.Passed, violations: [Violation("color-contrast", ".badge"), Violation("label", "#email")]));
        var head = Run(1, Result("a", StoryOutcome.Passed, violations: [Violation("color-contrast", ".badge"), Violation("button-name", ".close")]));

        var deltas = RunHistory.AccessibilityDelta(@base, head).ToDictionary(d => d.Violation.RuleId, d => d.Change);

        Assert.Equal(ViolationChange.New, deltas["button-name"]);
        Assert.Equal(ViolationChange.Existing, deltas["color-contrast"]);
        Assert.Equal(ViolationChange.Fixed, deltas["label"]);
    }

    [Fact]
    public void Archive_round_trips_runs_in_order_and_prunes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archive = new RunArchive(directory);
            for (var i = 0; i < 5; i++)
            {
                archive.Save(Run(i, Result("a", i % 2 == 0 ? StoryOutcome.Passed : StoryOutcome.Failed, violations: [Violation("label", "#x")])), capacity: 3);
            }

            var runs = archive.Load();
            Assert.Equal(["run-02", "run-03", "run-04"], runs.Select(r => r.Run.RunId).ToArray());
            var failed = runs[1].Results.Single();
            Assert.Equal(StoryOutcome.Failed, failed.Outcome);
            Assert.Equal(FailureCategory.Assertion, failed.Category);
            Assert.Equal(12, failed.Signature!.Length);
            Assert.Equal(["1.4.3"], failed.Violations.Single().Wcag);

            // Saving a run again replaces it.
            archive.Save(Run(4, Result("a", StoryOutcome.Passed)), capacity: 3);
            Assert.Equal(3, archive.Load().Count);
            Assert.True(archive.Delete("run-04"));
            Assert.Equal(2, archive.Load().Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_info_reads_ci_variables()
    {
        var info = RunInfo.Detect(environment: new Dictionary<string, string?>
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_SHA"] = "abc123",
            ["GITHUB_REF"] = "refs/pull/42/merge",
            ["GITHUB_HEAD_REF"] = "feature/x",
            ["GITHUB_RUN_ID"] = "777",
            ["GITHUB_SERVER_URL"] = "https://github.com",
            ["GITHUB_REPOSITORY"] = "owner/repo",
        });

        Assert.Equal("777-1", info.RunId);
        Assert.Equal("ci", info.Trigger);
        Assert.Equal("github", info.CiProvider);
        Assert.Equal(42, info.PullRequest);
        Assert.Equal("feature/x", info.Branch);
        Assert.Equal("https://github.com/owner/repo/actions/runs/777", info.CiUrl);
    }

    [Theory]
    [InlineData("Expected role=button to be focused", "StoryAssertionException", FailureCategory.Assertion)]
    [InlineData("Cannot click role=button: strict mode violation: 6 elements match", "ElementNotActionableException", FailureCategory.Locator)]
    [InlineData("Story timed out after 30 s.", "OperationCanceledException", FailureCategory.Timeout)]
    [InlineData("Story 'a' requested wasm but rendered with 'Server'.", "StoryAssertionException", FailureCategory.RenderMode)]
    [InlineData("A component threw an unhandled exception while the story ran: InvalidOperationException: boom", "StoryAssertionException", FailureCategory.ComponentCrash)]
    [InlineData("2 console/page error(s) while running the story", "StoryAssertionException", FailureCategory.PageError)]
    [InlineData("WebSocket connection failed: net::ERR_NO_BUFFER_SPACE", "StoryAssertionException", FailureCategory.Infrastructure)]
    [InlineData("Render performance regression in 'a'", "StoryAssertionException", FailureCategory.Performance)]
    public void Categories_follow_the_message(string message, string type, FailureCategory expected) =>
        Assert.Equal(expected, FailureAnalysis.Categorize(type, message));

    [Fact]
    public void Visual_and_accessibility_evidence_win_over_the_message()
    {
        Assert.Equal(FailureCategory.Visual, FailureAnalysis.Categorize(null, "Visual snapshot differs from the baseline", visualFailed: true));
        Assert.Equal(FailureCategory.Accessibility, FailureAnalysis.Categorize("BlazorTestPlatform.Accessibility.AccessibilityViolationException", "1 violation", accessibilityFailed: true));
    }

    [Fact]
    public void Signatures_ignore_what_changes_between_runs()
    {
        var a = FailureAnalysis.Signature("X", "Timeout 5000ms waiting for C:\\work\\a\\b.cs:12 id 0d6e2a3b-4c5d-4c51-9a77-8f1c3f7e1d2b");
        var b = FailureAnalysis.Signature("X", "Timeout 6123ms waiting for /home/runner/x/y.cs:99 id 2b7e4a91-6c3d-4f20-8e15-7a9b0c1d2e3f");
        Assert.Equal(a, b);
        Assert.NotEqual(a, FailureAnalysis.Signature("X", "Expected role=button to be focused"));
        Assert.Equal("Timeout #ms waiting for <path> id <guid>", FailureAnalysis.Normalize("Timeout 5000ms waiting for C:\\work\\a\\b.cs:12 id 0d6e2a3b-4c5d-4c51-9a77-8f1c3f7e1d2b\n   at Foo.Bar()"));
    }

    [Fact]
    public void Prompt_describes_the_failure_without_sending_it_anywhere()
    {
        var prompt = FailureAnalysis.ToPrompt(Result("a", StoryOutcome.Failed) with { SourceFile = "ButtonStories.cs", SourceLine = 7 });
        Assert.Contains("Story: Inputs/a / a (a)", prompt, StringComparison.Ordinal);
        Assert.Contains("Declared in: ButtonStories.cs:7", prompt, StringComparison.Ordinal);
        Assert.Contains("Category (deterministic): Assertion", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Wilson_interval_narrows_with_more_runs()
    {
        var few = RunHistory.WilsonInterval(1, 4);
        var many = RunHistory.WilsonInterval(25, 100);
        Assert.True(few.High - few.Low > many.High - many.Low);
        Assert.InRange(many.Low, 0.17, 0.25);
        Assert.InRange(many.High, 0.25, 0.35);
    }

    [Fact]
    public void Quarantined_failures_are_counted_apart_and_reported_as_skipped_in_junit()
    {
        var quarantined = Result("a", StoryOutcome.Failed) with { Quarantine = new QuarantineEntry { Pattern = "a", Reason = "Tracked in #9" } };
        var summary = RunSummary.From([quarantined, Result("b", StoryOutcome.Passed)]);

        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, summary.Quarantined);
        Assert.True(summary.Success);
        var path = Path.Combine(Path.GetTempPath(), $"btp-junit-{Guid.NewGuid():N}.xml");
        try
        {
            JUnitReportWriter.Write(summary, path);
            var xml = File.ReadAllText(path);
            Assert.Contains("quarantined: Tracked in #9", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<failure", xml, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Contains("### Quarantined", MarkdownSummaryWriter.Create(summary, null, null, null), StringComparison.Ordinal);
    }
}
