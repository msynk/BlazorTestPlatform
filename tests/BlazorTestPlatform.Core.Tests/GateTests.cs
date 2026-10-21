using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

public sealed class MatrixTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<MatrixTests>();

    [Fact]
    public void Matrix_expands_and_honours_story_filters()
    {
        var matrix = TestMatrix.CrossHosting();
        var cells = matrix.Expand(Catalog).ToArray();

        var onlyServer = cells.Where(c => c.Story.Id == "samples-greeter--only-server").ToArray();
        Assert.Single(onlyServer);
        Assert.Equal(HostingModel.InteractiveServer, onlyServer[0].HostingModel);
        Assert.Equal(4, cells.Count(c => c.Story.Id == "samples-greeter--default"));
    }

    [Fact]
    public void Matrix_keys_round_trip()
    {
        var matrix = new TestMatrix().WithViewports(Viewport.Mobile).WithThemes(ThemeDefinition.Dark).WithHostingModels(HostingModel.InteractiveWebAssembly).WithBrowsers(Browsers.Firefox);
        var cell = matrix.Expand(Catalog).First();

        var resolved = matrix.Resolve(Catalog, cell.Key);

        Assert.Equal(cell, resolved);
        Assert.Equal("samples-greeter--default|mobile|dark|wasm|firefox", cell.Key);
    }

    [Theory]
    [InlineData("server", HostingModel.InteractiveServer)]
    [InlineData("WASM", HostingModel.InteractiveWebAssembly)]
    [InlineData("ssr", HostingModel.StaticServer)]
    [InlineData("InteractiveAuto", HostingModel.InteractiveAuto)]
    public void Hosting_model_tokens_parse(string token, HostingModel expected)
    {
        Assert.Equal(expected, HostingModelExtensions.Parse(token));
        Assert.Equal(expected, HostingModelExtensions.Parse(expected.ToToken()));
    }

    [Fact]
    public void Key_chords_parse()
    {
        Assert.Equal((Drivers.KeyModifiers.Shift, "Tab"), Keys.Parse("Shift+Tab"));
        Assert.Equal((Drivers.KeyModifiers.Control | Drivers.KeyModifiers.Shift, "a"), Keys.Parse("Control+Shift+a"));
        Assert.Equal((Drivers.KeyModifiers.None, "+"), Keys.Parse("+"));
        Assert.Equal(" ", Keys.ToDomKey(Keys.Space));
        Assert.Equal("KeyA", Keys.ToDomCode("a"));
    }

    [Theory]
    [InlineData("  Save   changes ", "save", false, true)]
    [InlineData("Save changes", "Save changes", true, true)]
    [InlineData("Save changes", "save changes", true, false)]
    [InlineData("Save", "Save changes", false, false)]
    public void Text_matching_follows_playwright_semantics(string actual, string expected, bool exact, bool matches) =>
        Assert.Equal(matches, TextMatch.Matches(actual, expected, exact));
}

public sealed class PerformanceTests
{
    private static RenderMetrics Metrics(int renders, long bytes, double ms) =>
        new() { RootRenderCount = renders, TotalRenderCount = renders + 1, AllocatedBytes = bytes, MedianMilliseconds = ms, Iterations = 5 };

    [Fact]
    public void Extra_renders_are_regressions_by_default()
    {
        var baseline = PerformanceBaselineEntry.From(Metrics(2, 10_000, 3));
        var comparison = PerformanceComparer.Compare(Metrics(3, 10_000, 3), baseline);

        Assert.True(comparison.HasFailures);
        Assert.Contains(comparison.Failures, f => f.Metric == "Root renders");
    }

    [Fact]
    public void Allocation_noise_within_tolerance_passes_and_timing_only_warns()
    {
        var baseline = PerformanceBaselineEntry.From(Metrics(2, 100_000, 3));
        var comparison = PerformanceComparer.Compare(Metrics(2, 108_000, 30), baseline);

        Assert.False(comparison.HasFailures);
        Assert.Contains(comparison.Changes, c => c.Metric == "Median ms" && c.IsRegression && !c.FailsBuild);
    }

    [Fact]
    public void Missing_baseline_is_reported()
    {
        Assert.True(PerformanceComparer.Compare(Metrics(1, 1, 1), null).BaselineMissing);
    }

    [Fact]
    public void Budgets_report_each_exceeded_limit()
    {
        var violations = PerformanceBudgetEvaluator.Evaluate(Metrics(5, 50_000, 12), new PerformanceBudget { MaxRootRenders = 3, MaxAllocatedBytes = 10_000, MaxMedianDuration = TimeSpan.FromMilliseconds(20) });

        Assert.Equal(["Root renders", "Allocated bytes"], violations.Select(v => v.Metric));
    }

    [Fact]
    public void Baseline_store_persists_and_prunes()
    {
        var path = Path.Combine(Path.GetTempPath(), "btp-tests", Guid.NewGuid().ToString("N"), "performance.json");
        var store = new PerformanceBaselineStore(path);
        store.Set("a--b", PerformanceBaselineEntry.From(Metrics(1, 2, 3)));
        store.Set("c--d", PerformanceBaselineEntry.From(Metrics(4, 5, 6)));

        var reloaded = new PerformanceBaselineStore(path);
        Assert.Equal(4, reloaded.Get("c--d")!.RootRenderCount);
        Assert.Equal(["c--d"], reloaded.Prune(["a--b"]));
        Assert.Null(new PerformanceBaselineStore(path).Get("c--d"));
    }
}

public sealed class AccessibilityGateTests
{
    private static AccessibilityReport Report(params (string Rule, AccessibilityImpact Impact)[] violations) => new()
    {
        Engine = "test",
        Violations = violations.Select(v => new AccessibilityViolation { RuleId = v.Rule, Impact = v.Impact, Nodes = [new AccessibilityNode("#x", "<x>")] }).ToArray(),
    };

    [Fact]
    public void Impact_threshold_splits_failures_and_warnings()
    {
        var evaluation = new AccessibilityPolicy().Evaluate(Report(("image-alt", AccessibilityImpact.Critical), ("heading-order", AccessibilityImpact.Moderate)));

        Assert.Equal("image-alt", Assert.Single(evaluation.Failures).RuleId);
        Assert.Equal("heading-order", Assert.Single(evaluation.Warnings).RuleId);
        Assert.Contains("image-alt", evaluation.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void Suppressions_apply_until_they_expire()
    {
        var policy = new AccessibilityPolicy();
        policy.Suppressions.Add(new AccessibilitySuppression { RuleId = "color-contrast", StoryPattern = "inputs-*", Justification = "#42", Expires = new DateOnly(2030, 1, 1) });
        var report = Report(("color-contrast", AccessibilityImpact.Serious));

        Assert.True(policy.Evaluate(report, "inputs-button--primary", today: new DateOnly(2029, 12, 31)).Passed);
        Assert.False(policy.Evaluate(report, "data-grid--default", today: new DateOnly(2029, 12, 31)).Passed);

        var expired = policy.Evaluate(report, "inputs-button--primary", today: new DateOnly(2030, 1, 2));
        Assert.False(expired.Passed);
        Assert.Single(expired.ExpiredSuppressions);
    }

    [Fact]
    public void Story_disabled_rules_are_suppressed()
    {
        var options = new StoryAccessibilityOptions();
        options.DisabledRules.Add("image-alt");

        Assert.True(new AccessibilityPolicy().Evaluate(Report(("image-alt", AccessibilityImpact.Critical)), "x", options).Passed);
    }

    [Fact]
    public void Wcag_criteria_derive_from_tags() =>
        Assert.Equal(["1.4.3", "4.1.2"], new AccessibilityViolation { RuleId = "x", Tags = ["wcag2aa", "wcag143", "wcag412", "best-practice"] }.WcagCriteria);
}

public sealed class ManualReviewTests
{
    private static ManualReviewStore NewStore() => new(Path.Combine(Path.GetTempPath(), "btp-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public void Gate_reports_missing_stale_failed_incomplete_and_approved()
    {
        var store = NewStore();
        var pass = ManualChecklists.Wcag22AA.Select(c => new ReviewItem { CheckId = c.Id, Status = ReviewStatus.Pass }).ToArray();
        store.Save(new ComponentReview { Component = "A", Fingerprint = "f-a", Reviewer = "Ada", ReviewedAt = DateTimeOffset.UnixEpoch, Items = pass });
        store.Save(new ComponentReview { Component = "B", Fingerprint = "old", Items = pass });
        store.Save(new ComponentReview { Component = "C", Fingerprint = "f-c", Items = [new ReviewItem { CheckId = "keyboard", Status = ReviewStatus.Fail }] });
        store.Save(ManualReviewStore.CreateTemplate("D", "f-d"));

        var entries = ManualReviewGate.Evaluate(new Dictionary<string, string> { ["A"] = "f-a", ["B"] = "new", ["C"] = "f-c", ["D"] = "f-d", ["E"] = "f-e" }, store)
            .ToDictionary(e => e.Component, e => e.State);

        Assert.Equal(ReviewGateState.Approved, entries["A"]);
        Assert.Equal(ReviewGateState.Stale, entries["B"]);
        Assert.Equal(ReviewGateState.Failed, entries["C"]);
        Assert.Equal(ReviewGateState.Incomplete, entries["D"]);
        Assert.Equal(ReviewGateState.Missing, entries["E"]);
    }

    [Fact]
    public void Gate_options_decide_what_fails()
    {
        var entries = new[] { new ReviewGateEntry("X", ReviewGateState.Missing, "none", "f") };

        ManualReviewGate.ThrowIfFailing(entries, new ManualReviewGateOptions { RequireReview = false });
        Assert.Throws<StoryAssertionException>(() => ManualReviewGate.ThrowIfFailing(entries, new ManualReviewGateOptions { RequireReview = true }));
    }
}

public sealed class SettingsAndResultsTests
{
    [Fact]
    public void Ci_defaults_are_strict()
    {
        var settings = BtpSettings.Load(Path.GetTempPath(), new Dictionary<string, string?> { ["GITHUB_ACTIONS"] = "true" });

        Assert.True(settings.IsContinuousIntegration);
        Assert.Equal(SnapshotUpdateMode.None, settings.UpdateSnapshots);
        Assert.Equal(2, settings.Retries);
    }

    [Fact]
    public void Environment_overrides_apply()
    {
        var settings = BtpSettings.Load(Path.GetTempPath(), new Dictionary<string, string?>
        {
            ["BTP_UPDATE_SNAPSHOTS"] = "changed",
            ["BTP_RETRIES"] = "5",
            ["BTP_DIAGNOSTICS"] = "always",
            ["BTP_TIMEOUT_MS"] = "1500",
            ["BTP_UPDATE_PERF"] = "1",
        });

        Assert.Equal(SnapshotUpdateMode.Changed, settings.UpdateSnapshots);
        Assert.Equal(5, settings.Retries);
        Assert.Equal(DiagnosticsMode.Always, settings.Diagnostics);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), settings.DefaultTimeout);
        Assert.True(settings.UpdatePerformanceBaselines);
        Assert.Throws<FormatException>(() => BtpSettings.Load(Path.GetTempPath(), new Dictionary<string, string?> { ["BTP_UPDATE_SNAPSHOTS"] = "sometimes" }));
    }

    [Fact]
    public void Settings_file_configures_paths_and_policy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "btp.json"), """
            { "snapshotDirectory": "baselines", "accessibility": { "failOn": "moderate", "disabledRules": ["region"] }, "visual": { "threshold": 0.2 } }
            """);

        var settings = BtpSettings.Load(directory, new Dictionary<string, string?>());

        Assert.Equal(Path.Combine(directory, "baselines"), settings.SnapshotDirectoryPath);
        Assert.Equal(AccessibilityImpact.Moderate, settings.Accessibility.FailOn);
        Assert.Contains("region", settings.Accessibility.DisabledRules);
        Assert.Equal(0.2, settings.VisualThreshold);
    }

    [Fact]
    public void Results_round_trip_through_json()
    {
        var store = new ResultsStore(Path.Combine(Path.GetTempPath(), "btp-tests", Guid.NewGuid().ToString("N")));
        var result = new StoryResult
        {
            Key = StoryResult.CreateKey("a--b", "chromium", "desktop", "dark", "wasm"),
            StoryId = "a--b",
            Title = "A",
            Name = "B",
            Driver = "chromium",
            Outcome = StoryOutcome.Flaky,
            Visual = new VisualResult { SnapshotName = "a--b/desktop", Status = VisualStatus.Mismatched, DiffPixels = 12 },
            Steps = [new StepRecord { Title = "play", Kind = StepKind.Step, Children = [new StepRecord { Title = "click", Kind = StepKind.Action, Error = "boom" }] }],
        };

        store.Write(result);
        var read = Assert.Single(store.ReadAll());

        Assert.Equal(StoryOutcome.Flaky, read.Outcome);
        Assert.Equal(12, read.Visual!.DiffPixels);
        Assert.True(read.Steps[0].Children[0].Failed);
        Assert.Equal("a--b~chromium~desktop~dark~wasm", ResultsStore.SafeFileName(result.Key));
    }
}

public sealed class FileFormatCompatibilityTests
{
    [Fact]
    public void Results_keep_camel_case_enums_and_survive_infinite_ratios()
    {
        var store = new ResultsStore(Path.Combine(Path.GetTempPath(), "btp-tests", Guid.NewGuid().ToString("N")));
        var comparison = PerformanceComparer.Compare(
            new RenderMetrics { RootRenderCount = 2, TotalRenderCount = 2, AllocatedBytes = 10 },
            new PerformanceBaselineEntry { RootRenderCount = 0, TotalRenderCount = 0, AllocatedBytes = 0 });
        var path = store.Write(new StoryResult { Key = "k", StoryId = "a--b", Title = "A", Name = "B", Driver = "bunit", Outcome = StoryOutcome.Flaky, PerformanceComparison = comparison });

        var text = File.ReadAllText(path);
        Assert.Contains("\"outcome\": \"flaky\"", text, StringComparison.Ordinal);
        Assert.True(double.IsInfinity(Assert.Single(store.ReadAll()).PerformanceComparison!.Changes[0].ChangeRatio));
    }

    [Fact]
    public void Files_written_by_earlier_versions_still_load()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "results"));
        File.WriteAllText(Path.Combine(directory, "results", "old.json"), """
            { "key": "x", "storyId": "a--b", "title": "A", "name": "B", "driver": "chromium", "outcome": "Passed",
              "visual": { "snapshotName": "a--b/desktop", "status": "newBaseline" }, "steps": [ { "title": "play", "kind": "step" } ] }
            """);
        File.WriteAllText(Path.Combine(directory, "btp.json"), """{ "updateSnapshots": "All", "accessibility": { "failOn": "Moderate" } }""");

        var result = Assert.Single(new ResultsStore(directory).ReadAll());
        var settings = BtpSettings.Load(directory, new Dictionary<string, string?>());

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        Assert.Equal(VisualStatus.NewBaseline, result.Visual!.Status);
        Assert.Equal(SnapshotUpdateMode.All, settings.UpdateSnapshots);
        Assert.Equal(AccessibilityImpact.Moderate, settings.Accessibility.FailOn);
    }

    [Fact]
    public void Action_arguments_serialize_primitives_and_event_args_without_reflection()
    {
        var log = new ActionLog();
        log.Record("OnClick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs { Detail = 2, ShiftKey = true });
        log.Record("OnChange", new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "x" });
        log.Record("OnCount", 3);

        var parsed = ActionLog.ParseJson(log.ToJson());

        Assert.Equal(2, parsed[0].ArgumentAs<Microsoft.AspNetCore.Components.Web.MouseEventArgs>()!.Detail);
        Assert.Contains("\"value\":\"x\"", parsed[1].ArgumentJson, StringComparison.Ordinal);
        Assert.Equal(3, parsed[2].ArgumentAs<int>());
    }
}
