using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class TagOptionStories : Stories<Counter>
{
    public override string Title => "Tests/Tag options";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        stories.Add("Parked", s => s.Tags("parked"));
        stories.Add("Hangs", s => s.Tags("quick", "fails-on-purpose").Play(ctx => Task.Delay(Timeout.Infinite, ctx.CancellationToken)));
    }
}

public sealed class TagOptionRunTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new TagOptionStories()]);

    private static BunitStoryOptions Options()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-tag-runs", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(directory, "btp.json"), """
            { "tagOptions": { "parked": { "skip": "waiting for the redesign" }, "quick": { "timeoutMilliseconds": 200 } } }
            """);
        return new BunitStoryOptions { Settings = BtpSettings.Load(directory, new Dictionary<string, string?>()), WriteResults = false };
    }

    [Fact]
    public async Task A_tag_skip_is_reported_with_its_reason()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-tag-options--parked"), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Skipped, result.Outcome);
        Assert.Equal("Skipped by tag 'parked': waiting for the redesign", result.SkipReason);
    }

    [Fact]
    public async Task A_tag_timeout_bounds_the_run()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-tag-options--hangs"), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Equal("Story timed out after 0.2 s.", result.Error);
    }
}
