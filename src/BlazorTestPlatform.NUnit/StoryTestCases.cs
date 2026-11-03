using BlazorTestPlatform.Results;
using NUnit.Framework;

namespace BlazorTestPlatform.NUnit;

/// <summary>
/// Runs BlazorTestPlatform stories as NUnit tests: <c>From</c> turns a catalog (or stories, or matrix cells) into <see cref="TestCaseData"/>
/// with readable names, categories from story tags and skip reasons, and <see cref="Passed(StoryResult)"/> reports a
/// story result to NUnit: a failure throws <see cref="StoryFailedException"/> with the timeline, and a skipped run (an
/// unsupported driver capability, or <c>ctx.Require</c> in play) is reported as ignored instead of passing silently.
/// </summary>
/// <example>
/// <code>
/// public static IEnumerable&lt;TestCaseData&gt; Stories => StoryTestCases.From(Catalog);
///
/// [TestCaseSource(nameof(Stories))]
/// public async Task Story_passes(string id) => StoryTestCases.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id)));
/// </code>
/// </example>
public static class StoryTestCases
{
    /// <summary>One case per story in the catalog; its value is the story id.</summary>
    public static IEnumerable<TestCaseData> From(StoryCatalog catalog) => From((catalog ?? throw new ArgumentNullException(nameof(catalog))).Stories);

    /// <summary>One case per story; its value is the story id.</summary>
    public static IEnumerable<TestCaseData> From(IEnumerable<Story> stories) => From(StoryTestCase.From(stories));

    /// <summary>One case per matrix cell; its value is the cell key (resolve it with <see cref="TestMatrix.Resolve"/>).</summary>
    public static IEnumerable<TestCaseData> From(IEnumerable<MatrixCase> cases) => From(StoryTestCase.From(cases));

    /// <summary>One case per test case, with its name, categories and ignore reason.</summary>
    public static IEnumerable<TestCaseData> From(IEnumerable<StoryTestCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(testCase =>
        {
            var data = new TestCaseData(testCase.Key).SetName(testCase.DisplayName);
            foreach (var category in testCase.Categories)
            {
                data.SetCategory(category);
            }

            return testCase.SkipReason is { } reason ? data.Ignore(reason) : data;
        });
    }

    /// <summary>Fails the test for a failed result and ignores it for a skipped one; returns passed results.</summary>
    public static StoryResult Passed(StoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome == StoryOutcome.Failed)
        {
            throw new StoryFailedException(result);
        }

        if (result.Outcome == StoryOutcome.Skipped)
        {
            Assert.Ignore(result.SkipReason ?? $"Story '{result.StoryId}' was skipped.");
        }

        return result;
    }

    /// <summary>Awaits a run and applies <see cref="Passed(StoryResult)"/>.</summary>
    public static async Task<StoryResult> PassedAsync(Task<StoryResult> run) => Passed(await (run ?? throw new ArgumentNullException(nameof(run))).ConfigureAwait(false));
}
