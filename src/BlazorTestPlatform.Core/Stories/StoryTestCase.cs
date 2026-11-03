namespace BlazorTestPlatform;

/// <summary>
/// One story (or one matrix cell) described the way test frameworks describe a data row: a serializable key, a readable
/// display name, a skip reason and categories. The framework packages (<c>BlazorTestPlatform.Xunit</c>, <c>.NUnit</c>,
/// <c>.MSTest</c>, <c>.TUnit</c>) turn these into their native rows; other frameworks can use them directly.
/// </summary>
/// <param name="Key">The value the test receives: a story id, or a <see cref="MatrixCase.Key"/>.</param>
/// <param name="Story">The story.</param>
/// <param name="DisplayName">The name test explorers show, such as <c>Forms/Button/Disabled</c>.</param>
/// <param name="SkipReason">Why the case is skipped up front (<see cref="Story.SkipReason"/>), or null.</param>
/// <param name="Categories">The story's tags, for category filters (<c>--filter-trait</c>, <c>--where cat==</c>...).</param>
public sealed record StoryTestCase(string Key, Story Story, string DisplayName, string? SkipReason, IReadOnlyList<string> Categories)
{
    /// <summary>One case per story; the key is the story id.</summary>
    public static IEnumerable<StoryTestCase> From(IEnumerable<Story> stories)
    {
        ArgumentNullException.ThrowIfNull(stories);
        return stories.Select(story => new StoryTestCase(story.Id, story, story.Path, story.SkipReason, TagsOf(story)));
    }

    /// <summary>One case per matrix cell; the key is <see cref="MatrixCase.Key"/> (resolve it with <see cref="TestMatrix.Resolve"/>).</summary>
    public static IEnumerable<StoryTestCase> From(IEnumerable<MatrixCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(cell => new StoryTestCase(cell.Key, cell.Story, cell.ToString(), cell.Story.SkipReason, TagsOf(cell.Story)));
    }

    private static string[] TagsOf(Story story) => [.. story.Tags.Order(StringComparer.Ordinal)];
}
