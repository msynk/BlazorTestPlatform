using System.Text.RegularExpressions;
using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class ExplorationStories : Stories<BuggyStepper>
{
    public override string Title => "Tests/Exploration";

    internal static void Stepper(ExplorationBuilder e) => e
        .Action("add", c => c.GetByRole("button", "Add").ClickAsync(), code: "await ctx.GetByRole(\"button\", \"Add\").ClickAsync();")
        .Action("remove", c => c.GetByRole("button", "Remove").ClickAsync(), when: c => c.GetByRole("button", "Remove").IsEnabledAsync(), code: "await ctx.GetByRole(\"button\", \"Remove\").ClickAsync();")
        .Action("double", c => c.GetByRole("button", "Double").ClickAsync(), code: "await ctx.GetByRole(\"button\", \"Double\").ClickAsync();")
        .Invariant("count is never negative", c => c.Expect(c.GetByTestId("count")).Not.ToHaveTextAsync(new Regex("^-"), TimeSpan.Zero))
        .Steps(40);

    protected override void Define(StoryCollectionBuilder<BuggyStepper> stories)
    {
        stories.Add("Explored", s => s.Tags("fails-on-purpose").Play(ctx => ctx.ExploreAsync("stepper", Stepper)));
        stories.Add("Short and safe", s => s.Play(ctx => ctx.ExploreAsync("adding only", e => e
            .Action("add", c => c.GetByRole("button", "Add").ClickAsync())
            .Invariant("count is never negative", c => c.Expect(c.GetByTestId("count")).Not.ToHaveTextAsync(new Regex("^-"), TimeSpan.Zero))
            .Steps(5))));
    }
}

public sealed class ExplorationTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new ExplorationStories()]);

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Exploration_finds_the_sequence_that_breaks_an_invariant_and_says_how_to_replay_it()
    {
        var first = await BunitStoryRunner.RunAsync(Catalog.Get("tests-exploration--explored"), Options, TestContext.Current.CancellationToken);
        var second = await BunitStoryRunner.RunAsync(Catalog.Get("tests-exploration--explored"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, first.Outcome);
        Assert.Contains("Exploration 'stepper': invariant 'count is never negative' broke after", first.Error, StringComparison.Ordinal);
        Assert.Matches(@"Steps: [a-z]+( -> [a-z]+)*", first.Error);
        Assert.Contains("remove", Regex.Match(first.Error!, "Steps: (.*)").Groups[1].Value, StringComparison.Ordinal);
        Assert.Matches(@"Replay: \.Seed\(-?\d+\)", first.Error);
        Assert.Contains("await ctx.GetByRole(\"button\", \"Remove\").ClickAsync();", first.Error, StringComparison.Ordinal);

        // The seed derives from the story: a rerun takes the same steps.
        Assert.Equal(first.Error, second.Error);
    }

    [Fact]
    public async Task The_reported_seed_replays_the_same_steps_and_safe_explorations_pass()
    {
        var failed = await BunitStoryRunner.RunAsync(Catalog.Get("tests-exploration--explored"), Options, TestContext.Current.CancellationToken);
        var seed = int.Parse(Regex.Match(failed.Error!, @"Seed\((-?\d+)\)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        var replay = StoryCatalog.FromCollections([new ReplayStories(seed)]).Stories.Single();
        var replayed = await BunitStoryRunner.RunAsync(replay, Options, TestContext.Current.CancellationToken);
        Assert.Equal(Regex.Match(failed.Error!, "Steps: (.*)").Value, Regex.Match(replayed.Error!, "Steps: (.*)").Value);

        var safe = await BunitStoryRunner.RunAsync(Catalog.Get("tests-exploration--short-and-safe"), Options, TestContext.Current.CancellationToken);
        Assert.True(safe.Outcome == StoryOutcome.Passed, safe.Error);
        Assert.Contains(Flatten(safe.Steps), s => s.Title == "5. add");
    }

    private sealed class ReplayStories(int seed) : Stories<BuggyStepper>
    {
        public override string Title => "Tests/Exploration replay";

        protected override void Define(StoryCollectionBuilder<BuggyStepper> stories) =>
            stories.Add("Replay", s => s.Play(ctx => ctx.ExploreAsync("stepper", e =>
            {
                ExplorationStories.Stepper(e);
                e.Seed(seed);
            })));
    }

    private static IEnumerable<StepRecord> Flatten(IEnumerable<StepRecord> steps) => steps.SelectMany(s => Flatten(s.Children).Prepend(s));
}
