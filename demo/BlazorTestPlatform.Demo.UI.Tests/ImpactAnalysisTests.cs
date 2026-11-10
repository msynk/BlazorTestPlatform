namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// Test impact analysis: a pull request that changes <c>Button.razor</c> needs the button's stories and the stories of
/// every component that renders a Button, not the whole catalog. In CI, <c>BTP_CHANGED_SINCE=origin/main</c> feeds
/// <c>StoryImpact.FromEnvironment</c> the files the branch changed.
/// </summary>
public sealed class ImpactAnalysisTests
{
    [Fact]
    public void A_component_change_selects_its_stories_and_the_stories_of_components_that_render_it()
    {
        var impact = StoryImpact.Analyze(ComponentStoryTests.Catalog, ["demo/BlazorTestPlatform.Demo.UI/Button.razor"]);

        Assert.False(impact.RunsEverything);
        var components = impact.Stories.Select(s => s.Title).Distinct().ToArray();
        Assert.Contains("Inputs/Button", components);
        Assert.Contains("Patterns/Sign-up form", components);    // renders TextField, Button and Alert
        Assert.Contains("Feedback/Cookie consent", components);
        Assert.Contains("Data/Todo list", components);
        Assert.DoesNotContain("Navigation/Tabs", components);
        Assert.DoesNotContain("Display/Badge", components);
        Assert.InRange(impact.Stories.Count, 1, ComponentStoryTests.Catalog.Count / 2);
    }

    [Fact]
    public void Documentation_selects_nothing_and_unplaceable_code_selects_everything()
    {
        Assert.Empty(StoryImpact.Analyze(ComponentStoryTests.Catalog, ["README.md", "docs/stories.md"]).Stories);

        var shared = StoryImpact.Analyze(ComponentStoryTests.Catalog, ["src/BlazorTestPlatform.Core/Stories/StoryBuilder.cs"]);
        Assert.True(shared.RunsEverything);
        Assert.Equal(ComponentStoryTests.Catalog.Count, shared.Stories.Count);
    }
}
