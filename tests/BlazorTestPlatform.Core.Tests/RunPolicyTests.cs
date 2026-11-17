using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Core.Tests;

public sealed class RunPolicyTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new AnnotatedStories()]);

    private static string Directory() => System.IO.Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-run-policy", Guid.NewGuid().ToString("N"))).FullName;

    private static BtpSettings Settings(params (string Name, string Value)[] environment) =>
        BtpSettings.Load(Directory(), environment.ToDictionary(e => e.Name, e => (string?)e.Value));

    private static StoryResult Result(Story story, StoryOutcome outcome, string driver = "bunit", string? hostingModel = "memory") => new()
    {
        Key = $"{story.Id}|{driver}|{Guid.NewGuid():N}",
        StoryId = story.Id,
        Title = story.Title,
        Name = story.Name,
        Driver = driver,
        HostingModel = hostingModel,
        Outcome = outcome,
        Attempts = outcome == StoryOutcome.Flaky ? 2 : 1,
        AttemptErrors = outcome == StoryOutcome.Flaky ? ["attempt 1: boom"] : [],
        Error = outcome == StoryOutcome.Failed ? "boom" : null,
        StartedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("smoke", "smoke,forms", true)]
    [InlineData("SMOKE", "smoke", true)]
    [InlineData("smoke && !visual", "smoke,visual", false)]
    [InlineData("smoke and not visual", "smoke", true)]
    [InlineData("(forms || dialogs) && !slow", "dialogs", true)]
    [InlineData("(forms or dialogs) and not slow", "dialogs,slow", false)]
    [InlineData("forms, dialogs", "dialogs", true)]
    [InlineData("a11y-*", "a11y-review", true)]
    [InlineData("a11y-*", "a11y", false)]
    [InlineData("!!smoke", "smoke", true)]
    [InlineData("android", "android", true)]
    [InlineData("notes || orders", "notes", true)]
    [InlineData("a || b && c", "a", true)]
    [InlineData("(a || b) && c", "a", false)]
    public void Tag_expressions_follow_boolean_precedence(string expression, string tags, bool expected) =>
        Assert.Equal(expected, TagExpression.Parse(expression).Matches(tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

    [Theory]
    [InlineData("smoke &&")]
    [InlineData("(smoke")]
    [InlineData("smoke visual")]
    [InlineData("&& smoke")]
    [InlineData("smoke )")]
    [InlineData("and")]
    public void Malformed_tag_expressions_say_where_they_break(string expression)
    {
        var error = Assert.Throws<FormatException>(() => TagExpression.Parse(expression));
        Assert.Contains("at position", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expected_failures_pass_when_they_fail_and_fail_when_they_pass()
    {
        var story = Catalog.Get("tests-annotated--known-bug");
        var settings = Settings();

        var failed = RunPolicy.Apply(story, Result(story, StoryOutcome.Failed), settings);
        Assert.Equal(StoryOutcome.Passed, failed.Outcome);
        Assert.Equal("#42: focus is lost", failed.ExpectedFailure);
        Assert.Equal("boom", failed.Error);

        // An intermittent known issue: a failure on any attempt is the issue showing up.
        Assert.Equal(StoryOutcome.Passed, RunPolicy.Apply(story, Result(story, StoryOutcome.Flaky), settings).Outcome);

        var passed = RunPolicy.Apply(story, Result(story, StoryOutcome.Passed), settings);
        Assert.Equal(StoryOutcome.Failed, passed.Outcome);
        Assert.Contains("was expected to fail (#42: focus is lost) but passed", passed.Error, StringComparison.Ordinal);

        // Runners write a result more than once (burn-in): applying again decides nothing new.
        Assert.Equal(passed, RunPolicy.Apply(story, passed, settings));
        Assert.Equal(failed, RunPolicy.Apply(story, failed, settings));

        var skipped = Result(story, StoryOutcome.Skipped);
        Assert.Equal(skipped, RunPolicy.Apply(story, skipped, settings));
    }

    [Fact]
    public void Expected_failures_can_be_limited_to_some_runs()
    {
        var story = Catalog.Get("tests-annotated--broken-in-webassembly");
        var settings = Settings();

        Assert.Equal(StoryOutcome.Passed, RunPolicy.Apply(story, Result(story, StoryOutcome.Failed, "chromium", "wasm"), settings).Outcome);
        Assert.Equal(StoryOutcome.Failed, RunPolicy.Apply(story, Result(story, StoryOutcome.Failed, "chromium", "server"), settings).Outcome);
        Assert.Equal(StoryOutcome.Passed, RunPolicy.Apply(story, Result(story, StoryOutcome.Passed, "bunit"), settings).Outcome);
        Assert.Null(RunPolicy.Apply(story, Result(story, StoryOutcome.Passed, "bunit"), settings).ExpectedFailure);
    }

    [Fact]
    public void Fail_on_flaky_turns_retried_passes_into_failures()
    {
        var story = Catalog.Get("tests-annotated--plain");
        var flaky = Result(story, StoryOutcome.Flaky);

        Assert.Equal(StoryOutcome.Flaky, RunPolicy.Apply(story, flaky, Settings()).Outcome);

        var failed = RunPolicy.Apply(story, flaky, Settings(("BTP_FAIL_ON_FLAKY", "1")));
        Assert.Equal(StoryOutcome.Failed, failed.Outcome);
        Assert.Equal(RunPolicy.FlakyErrorType, failed.ErrorType);
        Assert.Contains("attempt 1: boom", failed.Error, StringComparison.Ordinal);
        Assert.True(failed.IsBlockingFailure);
    }

    [Fact]
    public void Max_failures_stops_the_run_and_counts_each_blocking_failure_once()
    {
        var story = Catalog.Get("tests-annotated--plain");
        var before = RunPolicy.BlockingFailures;
        var settings = Settings(("BTP_MAX_FAILURES", (before + 2).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Null(RunPolicy.StopReason(settings));
        Assert.Null(RunPolicy.StopReason(Settings()));

        var first = RunPolicy.Apply(story, Result(story, StoryOutcome.Failed), settings);
        RunPolicy.Apply(story, first, settings);
        Assert.Null(RunPolicy.StopReason(settings));

        // Expected failures and passes do not count.
        RunPolicy.Apply(Catalog.Get("tests-annotated--known-bug"), Result(Catalog.Get("tests-annotated--known-bug"), StoryOutcome.Failed), settings);
        RunPolicy.Apply(story, Result(story, StoryOutcome.Passed), settings);
        Assert.Null(RunPolicy.StopReason(settings));

        RunPolicy.Apply(story, Result(story, StoryOutcome.Failed), settings);
        Assert.Contains("stopped after", RunPolicy.StopReason(settings), StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => Settings(("BTP_MAX_FAILURES", "-1")));
    }

    [Fact]
    public void Run_filters_select_by_tags_grep_and_inverted_grep()
    {
        var plain = Catalog.Get("tests-annotated--plain");
        var known = Catalog.Get("tests-annotated--known-bug");

        Assert.Null(StoryRunFilter.ExcludeReason(plain, Settings()));
        Assert.Null(StoryRunFilter.ExcludeReason(plain, Settings(("BTP_TAGS", "smoke && !slow"))));
        Assert.Contains("BTP_TAGS", StoryRunFilter.ExcludeReason(known, Settings(("BTP_TAGS", "smoke && !slow"))), StringComparison.Ordinal);
        Assert.Null(StoryRunFilter.ExcludeReason(known, Settings(("BTP_GREP", "KNOWN"))));
        Assert.Contains("BTP_GREP", StoryRunFilter.ExcludeReason(plain, Settings(("BTP_GREP", "known"))), StringComparison.Ordinal);
        Assert.Contains("BTP_GREP_INVERT", StoryRunFilter.ExcludeReason(plain, Settings(("BTP_GREP_INVERT", "Annotated/Plain"))), StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => Settings(("BTP_GREP", "(")));
        Assert.Throws<FormatException>(() => Settings(("BTP_TAGS", "smoke &&")));

        Assert.Equal(["tests-annotated--plain"], Catalog.WithTags("smoke && !slow").Ids);
        Assert.Equal(["tests-annotated--plain"], Catalog.ForRun(Settings(("BTP_TAGS", "smoke")))
            .Ids.Where(id => id == "tests-annotated--plain"));
    }

    [Fact]
    public void Last_failed_reruns_the_stories_that_failed_before()
    {
        var plain = Catalog.Get("tests-annotated--plain");
        var known = Catalog.Get("tests-annotated--known-bug");
        var directory = Directory();
        var environment = new Dictionary<string, string?> { ["BTP_LAST_FAILED"] = "1" };

        // No previous failure: everything runs.
        Assert.Null(StoryRunFilter.ExcludeReason(plain, BtpSettings.Load(directory, environment)));

        var other = Directory();
        var store = BtpSettings.Load(other, new Dictionary<string, string?>()).CreateResultsStore();
        store.Write(Result(known, StoryOutcome.Failed, "chromium", "wasm"));
        store.Write(Result(plain, StoryOutcome.Passed));
        var settings = BtpSettings.Load(other, environment);

        Assert.Null(StoryRunFilter.ExcludeReason(known, settings));
        Assert.Contains("BTP_LAST_FAILED", StoryRunFilter.ExcludeReason(plain, settings), StringComparison.Ordinal);

        // Read once: results this run writes do not change its selection.
        store.Write(Result(plain, StoryOutcome.Failed));
        Assert.NotNull(StoryRunFilter.ExcludeReason(plain, settings));
    }

    [Fact]
    public void Fixme_skips_with_a_tag_and_slow_triples_the_timeout()
    {
        var fixme = Catalog.Get("tests-annotated--not-done");
        Assert.Equal("Fix me: needs the new date picker", fixme.SkipReason);
        Assert.True(fixme.HasTag("fixme"));

        var slow = Catalog.Get("tests-annotated--cold-start");
        Assert.True(slow.IsSlow);
        Assert.Equal(TimeSpan.FromSeconds(90), slow.TimeoutOr(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(6), Catalog.Get("tests-annotated--slow-with-timeout").TimeoutOr(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(30), Catalog.Get("tests-annotated--plain").TimeoutOr(TimeSpan.FromSeconds(30)));
    }

    private sealed class AnnotatedStories : Stories<Greeter>
    {
        public override string Title => "Tests/Annotated";

        protected override void Define(StoryCollectionBuilder<Greeter> stories)
        {
            stories.Add("Plain", s => s.Tags("smoke"));
            stories.Add("Known bug", s => s.Tags("slow").ExpectFailure("#42: focus is lost"));
            stories.Add("Broken in WebAssembly", s => s.ExpectFailure("#7", run => run.HostingModel == HostingModel.InteractiveWebAssembly));
            stories.Add("Not done", s => s.Fixme("needs the new date picker"));
            stories.Add("Cold start", s => s.Slow());
            stories.Add("Slow with timeout", s => s.Slow().Timeout(TimeSpan.FromSeconds(2)));
        }
    }
}
