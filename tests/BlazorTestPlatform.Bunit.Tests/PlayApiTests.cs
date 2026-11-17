using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class SoftCounterStories : Stories<Counter>
{
    public override string Title => "Tests/Soft counter";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        stories.Add("Failing softly", s => s.Play(async ctx =>
        {
            await ctx.ExpectSoft(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 7", TimeSpan.FromMilliseconds(50));
            await ctx.GetByRole("button", "Increment").ClickAsync();
            await ctx.SoftAsync(() => ctx.ExpectAction("OnCountChanged").ToHaveBeenCalledAsync(times: 3, TimeSpan.FromMilliseconds(50)));
            ctx.Log("reached the end");
        }));
        stories.Add("Passing softly", s => s.Play(async ctx =>
        {
            await ctx.GetByRole("button", "Increment").ClickAsync();
            await ctx.ExpectSoft(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 1");
        }));
    }
}

public sealed class PlayApiTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<PlayApiTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Soft_assertions_let_play_continue_and_fail_the_story_with_every_failure()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-soft-counter--failing-softly"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.StartsWith("2 soft assertions failed:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Current count: 7", result.Error, StringComparison.Ordinal);
        Assert.Contains("OnCountChanged", result.Error, StringComparison.Ordinal);
        Assert.Contains(Flatten(result.Steps), s => s.Title == "reached the end");
        Assert.Equal(2, Flatten(result.Steps).Count(s => s.Kind == StepKind.Assertion && s.Failed));
    }

    [Fact]
    public async Task Passing_soft_assertions_pass()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-soft-counter--passing-softly"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task Eventually_retries_the_whole_block_and_keeps_the_last_attempt()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-counter--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;
        var attempts = 0;

        await ctx.ExpectEventuallyAsync(async () =>
        {
            attempts++;
            await ctx.GetByRole("button", "Increment").ClickAsync();
            await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 3");
        });

        Assert.Equal(3, attempts);
        var block = Assert.Single(ctx.Steps, s => s.Title == "expect eventually");
        Assert.False(block.Failed);
        Assert.Equal(2, block.Children.Count);
    }

    [Fact]
    public async Task Eventually_fails_with_the_last_error_within_its_own_deadline()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-counter--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<StoryAssertionException>(() =>
            ctx.ExpectEventuallyAsync(() => ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Current count: 9"), TimeSpan.FromMilliseconds(300), "the count to reach 9"));

        Assert.Contains("Expected the count to reach 9 to pass within 300 ms", error.Message, StringComparison.Ordinal);
        Assert.Contains("Current count: 0", error.Message, StringComparison.Ordinal);

        // Inner assertions check once per attempt instead of waiting their own 5 s each.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Poll_waits_for_state_outside_the_dom()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-counter--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;
        await ctx.GetByRole("button", "Increment").ClickAsync();

        await ctx.ExpectPollAsync(() => Task.FromResult(session.Actions.For("OnCountChanged").Count), n => n == 1, "one count change");
        var error = await Assert.ThrowsAsync<StoryAssertionException>(() =>
            ctx.ExpectPollAsync(() => Task.FromResult(session.Actions.For("OnCountChanged").Count), n => n == 2, "two count changes", TimeSpan.FromMilliseconds(100)));

        Assert.Contains("Expected two count changes.", error.Message, StringComparison.Ordinal);
        Assert.Contains("Last value: 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remain_checks_a_condition_over_fake_time_until_it_breaks()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-delayed-save--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;
        await ctx.GetByRole("button", "Save").ClickAsync();
        var status = ctx.GetByRole("status");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await ctx.ExpectToRemainAsync(() => ctx.Expect(status).ToHaveTextAsync("Saving…"), TimeSpan.FromSeconds(4), "the save to stay pending");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), "fake time is advanced, not waited for");

        var error = await Assert.ThrowsAsync<StoryAssertionException>(() =>
            ctx.ExpectToRemainAsync(() => ctx.Expect(status).ToHaveTextAsync("Saving…"), TimeSpan.FromSeconds(4), "the save to stay pending"));
        Assert.Contains("but it failed after 1000 ms", error.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<StepRecord> Flatten(IEnumerable<StepRecord> steps) =>
        steps.SelectMany(s => new[] { s }.Concat(Flatten(s.Children)));
}
