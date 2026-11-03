using BlazorTestPlatform.Accessibility;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Performance;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

/// <summary>Regression tests for defects found by the audit (see docs/improvement-progress.md).</summary>
public sealed class HardeningTests
{
    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-hardening", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Empty_path_variables_never_resolve_to_the_project_directory()
    {
        var project = NewDirectory();
        var settings = BtpSettings.Load(project, new Dictionary<string, string?> { ["BTP_RESULTS_DIR"] = "", ["BTP_SNAPSHOT_DIR"] = "  " });

        Assert.Equal(Path.Combine(project, "TestResults", "btp"), settings.ResultsDirectoryPath);
        Assert.Equal(Path.Combine(project, "__snapshots__"), settings.SnapshotDirectoryPath);
    }

    [Theory]
    [InlineData("CI", "woodpecker", true)]
    [InlineData("CI", "true", true)]
    [InlineData("CI", "false", false)]
    [InlineData("CI", "0", false)]
    [InlineData("JENKINS_URL", "https://ci.contoso.com/", true)]
    [InlineData("CODEBUILD_BUILD_ID", "build:1", true)]
    [InlineData("TEAMCITY_VERSION", "2025.1", true)]
    [InlineData("UNRELATED", "1", false)]
    public void Continuous_integration_is_detected_across_systems(string variable, string value, bool expected)
    {
        var settings = BtpSettings.Load(NewDirectory(), new Dictionary<string, string?> { [variable] = value });

        Assert.Equal(expected, settings.IsContinuousIntegration);
        Assert.Equal(expected ? SnapshotUpdateMode.None : SnapshotUpdateMode.Missing, settings.UpdateSnapshots);
    }

    [Fact]
    public void Btp_ci_overrides_detection()
    {
        Assert.False(BtpSettings.Load(NewDirectory(), new Dictionary<string, string?> { ["GITHUB_ACTIONS"] = "true", ["BTP_CI"] = "false" }).IsContinuousIntegration);
        Assert.True(BtpSettings.Load(NewDirectory(), new Dictionary<string, string?> { ["BTP_CI"] = "1" }).IsContinuousIntegration);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void Invalid_timeouts_are_rejected(string value) =>
        Assert.Throws<FormatException>(() => BtpSettings.Load(NewDirectory(), new Dictionary<string, string?> { ["BTP_TIMEOUT_MS"] = value }));

    [Fact]
    public void Clearing_refuses_directories_that_hold_sources()
    {
        var project = NewDirectory();
        File.WriteAllText(Path.Combine(project, "App.Tests.csproj"), "<Project />");

        var error = Assert.Throws<InvalidOperationException>(() => new ResultsStore(project).Clear());

        Assert.Contains("App.Tests.csproj", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project, "App.Tests.csproj")));
    }

    [Fact]
    public void Clearing_deletes_a_results_directory()
    {
        var results = Path.Combine(NewDirectory(), "TestResults", "btp");
        var store = new ResultsStore(results);
        store.Write(new StoryResult { Key = "a|bunit", StoryId = "a", Title = "A", Name = "a", Driver = "bunit" });

        store.Clear();

        Assert.False(Directory.Exists(results));
    }

    [Fact]
    public void Unreadable_results_are_reported_not_silently_dropped()
    {
        var store = new ResultsStore(NewDirectory());
        store.Write(new StoryResult { Key = "a|bunit", StoryId = "a", Title = "A", Name = "a", Driver = "bunit", Outcome = StoryOutcome.Passed });
        File.WriteAllText(Path.Combine(store.ResultsDirectory, "b~bunit.json"), "{\"key\":\"b|bunit\",\"outco");

        var results = store.ReadAll(out var unreadable);

        Assert.Single(results);
        Assert.Contains("b~bunit.json", Assert.Single(unreadable), StringComparison.Ordinal);
    }

    [Fact]
    public void Results_carry_a_schema_version_and_newer_ones_are_not_misread()
    {
        var store = new ResultsStore(NewDirectory());
        store.Write(new StoryResult { Key = "a|bunit", StoryId = "a", Title = "A", Name = "a", Driver = "bunit" });
        store.Write(new StoryResult { Key = "b|bunit", StoryId = "b", Title = "B", Name = "b", Driver = "bunit", SchemaVersion = StoryResult.CurrentSchemaVersion + 1 });
        File.WriteAllText(Path.Combine(store.ResultsDirectory, "c~bunit.json"), "{\"key\":\"c|bunit\",\"storyId\":\"c\",\"title\":\"C\",\"name\":\"c\",\"driver\":\"bunit\"}");

        var results = store.ReadAll(out var unreadable);

        Assert.Equal(["a", "c"], results.Select(r => r.StoryId));
        Assert.All(results, r => Assert.Equal(1, r.SchemaVersion));
        Assert.Contains("newer version", Assert.Single(unreadable), StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(Path.Combine(store.ResultsDirectory, "a~bunit.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/../b")]
    public void Result_file_names_never_address_other_directories(string key)
    {
        var name = ResultsStore.SafeFileName(key);

        Assert.NotEqual("..", name);
        Assert.NotEqual(".", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }

    [Fact]
    public async Task Parallel_baseline_writers_keep_every_entry()
    {
        var path = Path.Combine(NewDirectory(), "__baselines__", "performance.json");
        var entry = new PerformanceBaselineEntry { RootRenderCount = 1, TotalRenderCount = 2 };

        // Separate store instances, as BunitStoryRunner creates one per story.
        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() => new PerformanceBaselineStore(path).Set($"story-{i:00}", entry), TestContext.Current.CancellationToken)));

        Assert.Equal(24, new PerformanceBaselineStore(path).Entries.Count);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.False(File.Exists(path + ".lock"));
    }

    [Fact]
    public void A_corrupt_baseline_file_is_never_overwritten()
    {
        var path = Path.Combine(NewDirectory(), "performance.json");
        File.WriteAllText(path, "{ \"stories\": { \"a\": ");
        var store = new PerformanceBaselineStore(path);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => store.Set("b", new PerformanceBaselineEntry()));
        Assert.Equal("{ \"stories\": { \"a\": ", File.ReadAllText(path));
    }

    [Fact]
    public void Chords_can_end_with_plus_and_control_or_meta_follows_the_platform()
    {
        Assert.Equal((KeyModifiers.Shift, "+"), Keys.Parse("Shift++"));
        Assert.Equal((KeyModifiers.Control | KeyModifiers.Alt, "+"), Keys.Parse("Control+Alt++"));
        Assert.Equal((KeyModifiers.Shift, "+"), Keys.Parse(Keys.Chord(Keys.Shift, "+")));
        Assert.Equal(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control, Keys.Parse("ControlOrMeta+s").Modifiers);
    }

    [Fact]
    public async Task Infinite_timeouts_wait_until_success()
    {
        var probes = 0;
        var (success, value) = await Poll.UntilAsync(_ => Task.FromResult((++probes >= 3, probes)), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.Equal(3, value);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Poll.UntilAsync(_ => Task.FromResult((false, 0)), TimeSpan.FromSeconds(-2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Reviews_without_a_fingerprint_or_checks_are_not_approved()
    {
        var store = new ManualReviewStore(NewDirectory());
        var pass = ManualChecklists.Wcag22AA.Select(c => new ReviewItem { CheckId = c.Id, Status = ReviewStatus.Pass }).ToArray();
        store.Save(new ComponentReview { Component = "NoFingerprint", Items = pass });
        store.Save(new ComponentReview { Component = "NoItems", Fingerprint = "f" });

        var entries = ManualReviewGate.Evaluate(new Dictionary<string, string> { ["NoFingerprint"] = "f", ["NoItems"] = "f" }, store).ToDictionary(e => e.Component);

        Assert.Equal(ReviewGateState.Stale, entries["NoFingerprint"].State);
        Assert.Contains("\"fingerprint\": \"f\"", entries["NoFingerprint"].Detail, StringComparison.Ordinal);
        Assert.Equal(ReviewGateState.Incomplete, entries["NoItems"].State);
    }

    [Theory]
    [InlineData("javascript:alert(1)", true)]
    [InlineData("  JavaScript:alert(1)", true)]
    [InlineData("java\tscript:alert(1)", true)]
    [InlineData("\u0001javascript:alert(1)", true)]
    [InlineData("vbscript:msgbox", true)]
    [InlineData("data:text/html,<b>", true)]
    [InlineData("https://contoso.com/a?b=javascript:", false)]
    [InlineData("/relative/path", false)]
    [InlineData("Plain text: with a colon", false)]
    [InlineData("", false)]
    public void Script_urls_are_recognized_with_browser_obfuscations(string text, bool expected) =>
        Assert.Equal(expected, StoryArgs.IsScriptUrl(text));

    [Fact]
    public void Links_cannot_pass_script_urls_but_typed_values_can()
    {
        var story = StoryCatalog.FromAssemblyOf<HardeningTests>().Get("samples-greeter--default");
        var overrides = new Dictionary<string, string?> { ["Name"] = "javascript:alert(document.domain)" };

        var fromLink = StoryArgs.Resolve(story, overrides, fromLink: true);
        var typed = StoryArgs.Resolve(story, overrides, fromLink: false);

        Assert.Empty(fromLink.Values);
        Assert.Contains("javascript:", Assert.Single(fromLink.Errors), StringComparison.Ordinal);
        Assert.Equal("javascript:alert(document.domain)", typed.Values["Name"]);
    }

    [Fact]
    public void Built_stories_have_read_only_options()
    {
        var story = StoryCatalog.FromAssemblyOf<HardeningTests>().Get("samples-greeter--default");

        Assert.True(story.Visual.IsReadOnly);
        var error = Assert.Throws<InvalidOperationException>(() => story.Visual.Threshold = 0.5);
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => story.Accessibility.Skip = true);
        Assert.False(new VisualOptions().IsReadOnly);
    }

    [Fact]
    public void Story_scoped_suppressions_do_not_apply_without_a_story()
    {
        var policy = new AccessibilityPolicy();
        policy.Suppressions.Add(new AccessibilitySuppression { RuleId = "color-contrast", StoryPattern = "inputs-*", Justification = "#42" });
        var report = new AccessibilityReport
        {
            Engine = "test",
            Violations = [new AccessibilityViolation { RuleId = "color-contrast", Impact = AccessibilityImpact.Serious, Nodes = [new AccessibilityNode("#x", "<x>")] }],
        };

        Assert.False(policy.Evaluate(report).Passed);
        Assert.True(policy.Evaluate(report, "inputs-button--primary").Passed);

        policy.Suppressions.Add(new AccessibilitySuppression { RuleId = "color-contrast", Justification = "global" });
        Assert.True(policy.Evaluate(report).Passed);
    }
}
