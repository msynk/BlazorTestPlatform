using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class CounterHarness : ComponentHarness
{
    protected override By HostQuery => By.Css("article.counter");

    public Task AddAsync() => GetByRole("button").ClickAsync();

    public async Task<int> ValueAsync() => int.Parse(await Locate(By.Css("output")).TextAsync(), System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class BoardHarness : ComponentHarness
{
    protected override By HostQuery => By.Role("region", "Counters");

    // Harnesses compose: the board hands out the harness of one of its counters.
    public CounterHarness Counter(int index) => Harness<CounterHarness>(index);
}

public sealed class HarnessStories : Stories<CounterBoard>
{
    public override string Title => "Tests/Harnesses";

    protected override void Define(StoryCollectionBuilder<CounterBoard> stories)
    {
        stories.Add("Through harnesses", s => s.Play(async ctx =>
        {
            var counters = await ctx.HarnessesAsync<CounterHarness>();
            if (counters.Count != 3)
            {
                throw new StoryAssertionException($"Expected 3 counters, found {counters.Count}.");
            }

            await counters[1].AddAsync();
            await counters[1].AddAsync();
            await ctx.Harness<BoardHarness>().Counter(2).AddAsync();

            var values = new List<int>();
            foreach (var counter in counters)
            {
                values.Add(await counter.ValueAsync());
            }

            if (!values.SequenceEqual([0, 2, 1]))
            {
                throw new StoryAssertionException($"Expected 0, 2, 1, got {string.Join(", ", values)}.");
            }
        }));
        stories.Add("Ambiguous", s => s.Tags("fails-on-purpose").Play(ctx => ctx.Harness<CounterHarness>().AddAsync()));
    }
}

public sealed class HarnessTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new HarnessStories()]);

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Harnesses_find_instances_and_compose()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-harnesses--through-harnesses"), Options, TestContext.Current.CancellationToken);

        Assert.True(result.Outcome == StoryOutcome.Passed, result.Error);
    }

    [Fact]
    public async Task A_harness_matching_several_instances_fails_like_a_strict_locator()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-harnesses--ambiguous"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("3", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_harness_created_without_a_context_says_how_to_create_one()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CounterHarness().Host);

        Assert.Contains("ctx.Harness<CounterHarness>()", error.Message, StringComparison.Ordinal);
    }
}
