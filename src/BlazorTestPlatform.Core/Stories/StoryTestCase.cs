namespace BlazorTestPlatform;

/// <summary>
/// One story (or one matrix cell) described the way test frameworks describe a data row: a serializable key, a readable
/// display name, a skip reason and categories. The framework packages (<c>BlazorTestPlatform.Xunit</c>, <c>.NUnit</c>,
/// <c>.MSTest</c>, <c>.TUnit</c>) turn these into their native rows; other frameworks can use them directly.
/// </summary>
/// <param name="Key">The value the test receives: a story id, or a <see cref="MatrixCase.Key"/>.</param>
/// <param name="Story">The story.</param>
/// <param name="DisplayName">The name test explorers show, such as <c>Forms/Button/Disabled</c>.</param>
/// <param name="SkipReason">Why the case is skipped up front (<see cref="Story.SkipReason"/>, or the run's selection: <see cref="Results.StoryRunFilter"/>), or null.</param>
/// <param name="Categories">The story's tags, for category filters (<c>--filter-trait</c>, <c>--where cat==</c>...).</param>
public sealed record StoryTestCase(string Key, Story Story, string DisplayName, string? SkipReason, IReadOnlyList<string> Categories)
{
    /// <summary>One case per story; the key is the story id.</summary>
    public static IEnumerable<StoryTestCase> From(IEnumerable<Story> stories)
    {
        ArgumentNullException.ThrowIfNull(stories);
        return stories.Select(story => new StoryTestCase(story.Id, story, story.Path, SkipReasonOf(story), TagsOf(story)));
    }

    /// <summary>One case per matrix cell; the key is <see cref="MatrixCase.Key"/> (resolve it with <see cref="TestMatrix.Resolve"/>).</summary>
    public static IEnumerable<StoryTestCase> From(IEnumerable<MatrixCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(cell => new StoryTestCase(cell.Key, cell.Story, cell.ToString(), SkipReasonOf(cell.Story), TagsOf(cell.Story)));
    }

    // The story's own skip, else the run's selection (BTP_TAGS, BTP_GREP, BTP_LAST_FAILED): reported as skipped with the
    // reason, rather than removed, so a filter that selects nothing never leaves a test without data rows.
    private static string? SkipReasonOf(Story story) => story.SkipReason ?? Configuration.BtpSettings.Current.TagSkipReason(story) ?? Results.StoryRunFilter.ExcludeReason(story);

    private static string[] TagsOf(Story story) => [.. story.Tags.Order(StringComparer.Ordinal)];
}
