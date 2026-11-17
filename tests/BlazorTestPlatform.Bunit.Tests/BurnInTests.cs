using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class BurnInCounterStories : Stories<Counter>
{
    private static int runs;

    public override string Title => "Tests/Burn in";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        // Fails on every second run, as an order- or timing-dependent story would.
        stories.Add("Failing every second run", s => s.Play(_ =>
            Interlocked.Increment(ref runs) % 2 == 0 ? throw new StoryAssertionException("unlucky run") : Task.CompletedTask));
        stories.Add("Stable", s => s.Play(ctx => ctx.GetByRole("button", "Increment").ClickAsync()));
    }
}

public sealed class BurnInTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<BurnInTests>();

    private static BunitStoryOptions Options(int repeatEach) => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?> { ["BTP_REPEAT_EACH"] = repeatEach.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
        WriteResults = false,
    };

    [Fact]
    public async Task Burn_in_fails_a_story_that_does_not_pass_every_run()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-burn-in--failing-every-second-run"), Options(4), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Matches(@"^Burn-in: run [12] of 4 failed after [01] passing run\(s\): unlucky run$", result.Error);
    }

    [Fact]
    public async Task Burn_in_passes_stable_stories_and_one_run_is_the_default()
    {
        Assert.Equal(StoryOutcome.Passed, (await BunitStoryRunner.RunAsync(Catalog.Get("tests-burn-in--stable"), Options(5), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(1, BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()).RepeatEach);
        Assert.Throws<FormatException>(() => BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?> { ["BTP_REPEAT_EACH"] = "0" }));
    }

    [Fact]
    public async Task Flaky_runs_fail_burn_in()
    {
        var flaky = new StoryResult { Key = "k", StoryId = "s", Title = "T", Name = "N", Driver = "chromium", Outcome = StoryOutcome.Flaky, Attempts = 2, AttemptErrors = ["attempt 1: timeout"] };

        var result = await BurnIn.RunAsync(3, run => Task.FromResult(run == 1 ? flaky with { Outcome = StoryOutcome.Passed } : flaky));

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Equal("Burn-in: run 2 of 3 failed after 1 passing run(s): passed only after 2 attempts (attempt 1: timeout)", result.Error);
    }
}
