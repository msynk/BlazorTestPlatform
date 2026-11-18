using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class RevealsImageAfterActionStories : Stories<RevealsImage>
{
    public override string Title => "Tests/A11y after actions/Image";

    protected override void Define(StoryCollectionBuilder<RevealsImage> stories) =>
        stories.Add("Checked", s => s.Tags("fails-on-purpose").CheckAccessibilityAfterActions().Play(ctx => ctx.GetByRole("button", "Show picture").ClickAsync()));
}

public sealed class CounterAfterActionStories : Stories<Counter>
{
    public override string Title => "Tests/A11y after actions/Counter";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        stories.Add("Checked", s => s.CheckAccessibilityAfterActions().Play(async ctx =>
        {
            await ctx.GetByRole("button", "Increment").ClickAsync();
            await ctx.GetByRole("button", "Increment").FocusAsync();
            await ctx.PressAsync("Enter");
            await ctx.TabAsync(3);
            ctx.CheckAccessibilityAfterActions = false;
            await ctx.GetByRole("button", "Increment").ClickAsync();
        }));
    }
}

public sealed class AccessibilityAfterActionsTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new RevealsImageAfterActionStories(), new CounterAfterActionStories()]);

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task The_action_that_made_the_story_inaccessible_is_named()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-a11y-after-actions-image--checked"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("(after click role=button[name=\"Show picture\"i])", result.Error, StringComparison.Ordinal);
        Assert.Contains(Flatten(result.Steps), s => s.Title.StartsWith("expect accessible (after click", StringComparison.Ordinal) && s.Failed);
    }

    [Fact]
    public async Task Every_action_and_key_press_is_checked_until_play_turns_it_off()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-a11y-after-actions-counter--checked"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        var checks = Flatten(result.Steps).Where(s => s.Title.StartsWith("expect accessible (after ", StringComparison.Ordinal)).Select(s => s.Title).ToArray();
        Assert.Equal(
            [
                "expect accessible (after click role=button[name=\"Increment\"i])",
                "expect accessible (after focus role=button[name=\"Increment\"i])",
                "expect accessible (after press Enter)",
                "expect accessible (after press Tab 3 times)",
            ],
            checks);
    }

    private static IEnumerable<StepRecord> Flatten(IEnumerable<StepRecord> steps) => steps.SelectMany(s => Flatten(s.Children).Prepend(s));
}
