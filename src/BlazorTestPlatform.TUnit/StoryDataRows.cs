using BlazorTestPlatform.Results;
using TUnit.Core;

namespace BlazorTestPlatform.TUnit;

/// <summary>
/// Runs BlazorTestPlatform stories as TUnit tests: <c>From</c> turns a catalog (or stories, or matrix cells) into <see cref="TestDataRow{T}"/> values
/// with readable names, categories from story tags and skip reasons, and <see cref="Passed(StoryResult)"/> reports a
/// story result to TUnit: a failure throws <see cref="StoryFailedException"/> with the timeline, and a skipped run (an
/// unsupported driver capability, or <c>ctx.Require</c> in play) is reported as skipped instead of passing silently.
/// </summary>
/// <example>
/// <code>
/// public static IEnumerable&lt;TestDataRow&lt;string&gt;&gt; Stories() => StoryDataRows.From(Catalog);
///
/// [Test, MethodDataSource(nameof(Stories))]
/// public async Task Story_passes(string id) => StoryDataRows.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id)));
/// </code>
/// </example>
public static class StoryDataRows
{
    /// <summary>One row per story in the catalog; its value is the story id.</summary>
    public static IEnumerable<TestDataRow<string>> From(StoryCatalog catalog) => From((catalog ?? throw new ArgumentNullException(nameof(catalog))).Stories);

    /// <summary>One row per story; its value is the story id.</summary>
    public static IEnumerable<TestDataRow<string>> From(IEnumerable<Story> stories) => From(StoryTestCase.From(stories));

    /// <summary>One row per matrix cell; its value is the cell key (resolve it with <see cref="TestMatrix.Resolve"/>).</summary>
    public static IEnumerable<TestDataRow<string>> From(IEnumerable<MatrixCase> cases) => From(StoryTestCase.From(cases));

    /// <summary>One row per test case, with its display name, categories and skip reason.</summary>
    public static IEnumerable<TestDataRow<string>> From(IEnumerable<StoryTestCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(testCase => new TestDataRow<string>(testCase.Key, testCase.DisplayName, testCase.SkipReason, [.. testCase.Categories]));
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
            Skip.Test(result.SkipReason ?? $"Story '{result.StoryId}' was skipped.");
        }

        return result;
    }

    /// <summary>Awaits a run and applies <see cref="Passed(StoryResult)"/>.</summary>
    public static async Task<StoryResult> PassedAsync(Task<StoryResult> run) => Passed(await (run ?? throw new ArgumentNullException(nameof(run))).ConfigureAwait(false));
}
