using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class RunControlStories : Stories<Counter>
{
    public override string Title => "Tests/Run control";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        stories.Add("Known bug", s => s.ExpectFailure("#12: the count never reaches 5").Play(async ctx =>
            await ctx.Expect(ctx.GetByText("Count: 5")).ToBeVisibleAsync(TimeSpan.FromMilliseconds(50))));
        stories.Add("Fixed bug", s => s.Tags("fails-on-purpose").ExpectFailure("#13").Play(ctx => ctx.GetByRole("button", "Increment").ClickAsync()));
        stories.Add("Tagged", s => s.Tags("smoke"));
        stories.Add("Slow", s => s.Slow().Timeout(TimeSpan.FromMilliseconds(100)).Play(_ => Task.Delay(200, TestContext.Current.CancellationToken)));
    }
}

public sealed class RunControlTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new RunControlStories()]);

    private static BunitStoryOptions Options(params (string Name, string Value)[] environment) => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), environment.ToDictionary(e => e.Name, e => (string?)e.Value)),
        WriteResults = false,
    };

    [Fact]
    public async Task Expected_failures_pass_while_the_bug_exists_and_fail_once_it_is_fixed()
    {
        var known = await BunitStoryRunner.RunAsync(Catalog.Get("tests-run-control--known-bug"), Options(), TestContext.Current.CancellationToken);
        Assert.Equal(StoryOutcome.Passed, known.Outcome);
        Assert.Equal("#12: the count never reaches 5", known.ExpectedFailure);
        Assert.Contains("Count: 5", known.Error, StringComparison.Ordinal);
        await BunitStoryRunner.VerifyAsync(Catalog.Get("tests-run-control--known-bug"), Options(), TestContext.Current.CancellationToken);

        var fixedBug = await BunitStoryRunner.RunAsync(Catalog.Get("tests-run-control--fixed-bug"), Options(), TestContext.Current.CancellationToken);
        Assert.Equal(StoryOutcome.Failed, fixedBug.Outcome);
        Assert.Contains("expected to fail (#13) but passed", fixedBug.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runners_skip_stories_the_run_does_not_select()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-run-control--fixed-bug"), Options(("BTP_TAGS", "smoke")), TestContext.Current.CancellationToken);
        Assert.Equal(StoryOutcome.Skipped, result.Outcome);
        Assert.Contains("BTP_TAGS", result.SkipReason, StringComparison.Ordinal);

        Assert.Equal(StoryOutcome.Passed, (await BunitStoryRunner.RunAsync(Catalog.Get("tests-run-control--tagged"), Options(("BTP_TAGS", "smoke")), TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task Stories_a_filtered_run_leaves_out_keep_their_last_result_for_last_failed()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-run-control", Guid.NewGuid().ToString("N"))).FullName;
        BunitStoryOptions Writing(params (string Name, string Value)[] environment) => new()
        {
            Settings = BtpSettings.Load(directory, environment.Append((Name: "BTP_RESULTS_DIR", Value: Path.Combine(directory, "results"))).ToDictionary(e => e.Name, e => (string?)e.Value)),
        };

        var story = Catalog.Get("tests-run-control--fixed-bug");
        Assert.Equal(StoryOutcome.Failed, (await BunitStoryRunner.RunAsync(story, Writing(), TestContext.Current.CancellationToken)).Outcome);

        // A run filtered to other stories reports it as skipped but does not replace the failure on disk...
        Assert.Equal(StoryOutcome.Skipped, (await BunitStoryRunner.RunAsync(story, Writing(("BTP_TAGS", "smoke")), TestContext.Current.CancellationToken)).Outcome);
        var stored = Assert.Single(Writing().Settings!.CreateResultsStore().ReadAll());
        Assert.Equal(StoryOutcome.Failed, stored.Outcome);

        // ...so the next BTP_LAST_FAILED run still selects it.
        Assert.Null(StoryRunFilter.ExcludeReason(story, Writing(("BTP_LAST_FAILED", "1")).Settings));
    }

    [Fact]
    public async Task Slow_stories_get_three_times_their_timeout()
    {
        // 100 ms tripled is 300 ms: the 200 ms play fits.
        Assert.Equal(StoryOutcome.Passed, (await BunitStoryRunner.RunAsync(Catalog.Get("tests-run-control--slow"), Options(), TestContext.Current.CancellationToken)).Outcome);
    }
}
