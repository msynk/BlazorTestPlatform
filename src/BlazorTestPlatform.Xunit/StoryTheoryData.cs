using BlazorTestPlatform.Results;
using Xunit;

namespace BlazorTestPlatform.Xunit;

/// <summary>
/// Runs BlazorTestPlatform stories as xUnit tests: <c>From</c> turns a catalog (or stories, or matrix cells) into theory rows
/// with readable names, categories from story tags and skip reasons, and <see cref="Passed(StoryResult)"/> reports a
/// story result to xUnit: a failure throws <see cref="StoryFailedException"/> with the timeline, and a skipped run (an
/// unsupported driver capability, or <c>ctx.Require</c> in play) is reported as skipped instead of passing silently.
/// </summary>
/// <example>
/// <code>
/// public static TheoryData&lt;string&gt; Stories => StoryTheoryData.From(Catalog);
///
/// [Theory, MemberData(nameof(Stories))]
/// public async Task Story_passes(string id) => StoryTheoryData.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id)));
/// </code>
/// </example>
public static class StoryTheoryData
{
    /// <summary>One row per story in the catalog; its value is the story id.</summary>
    public static TheoryData<string> From(StoryCatalog catalog) => From((catalog ?? throw new ArgumentNullException(nameof(catalog))).Stories);

    /// <summary>One row per story; its value is the story id.</summary>
    public static TheoryData<string> From(IEnumerable<Story> stories) => From(StoryTestCase.From(stories));

    /// <summary>One row per matrix cell; its value is the cell key (resolve it with <see cref="TestMatrix.Resolve"/>).</summary>
    public static TheoryData<string> From(IEnumerable<MatrixCase> cases) => From(StoryTestCase.From(cases));

    /// <summary>One row per test case, with its display name, skip reason and <c>Category</c> traits.</summary>
    public static TheoryData<string> From(IEnumerable<StoryTestCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var data = new TheoryData<string>();
        foreach (var testCase in cases)
        {
            var row = new TheoryDataRow<string>(testCase.Key) { TestDisplayName = testCase.DisplayName, Skip = testCase.SkipReason };
            foreach (var category in testCase.Categories)
            {
                row.WithTrait("Category", category);
            }

            data.Add(row);
        }

        return data;
    }

    /// <summary>Fails the test for a failed result and skips it for a skipped one; returns passed results.</summary>
    public static StoryResult Passed(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsBlockingFailure)
        {
            throw new StoryFailedException(result);
        }

        if (result.Outcome == StoryOutcome.Skipped)
        {
            Assert.Skip(result.SkipReason ?? $"Story '{result.StoryId}' was skipped.");
        }

        return result;
    }

    /// <summary>Awaits a run and applies <see cref="Passed(StoryResult)"/>.</summary>
    public static async Task<StoryResult> PassedAsync(Task<StoryResult> run) => Passed(await (run ?? throw new ArgumentNullException(nameof(run))).ConfigureAwait(false));
}
