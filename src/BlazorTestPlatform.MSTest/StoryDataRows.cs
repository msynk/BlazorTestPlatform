using BlazorTestPlatform.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BlazorTestPlatform.MSTest;

/// <summary>
/// Runs BlazorTestPlatform stories as MSTest tests: <c>From</c> turns a catalog (or stories, or matrix cells) into <see cref="TestDataRow{T}"/> values
/// with readable names, categories from story tags and skip reasons, and <see cref="Passed(StoryResult)"/> reports a
/// story result to MSTest: a failure throws <see cref="StoryFailedException"/> with the timeline, and a skipped run (an
/// unsupported driver capability, or <c>ctx.Require</c> in play) is reported as inconclusive instead of passing silently.
/// </summary>
/// <example>
/// <code>
/// public static IEnumerable&lt;TestDataRow&lt;string&gt;&gt; Stories => StoryDataRows.From(Catalog);
///
/// [TestMethod, DynamicData(nameof(Stories))]
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

    /// <summary>One row per test case, with its display name, categories and ignore message.</summary>
    public static IEnumerable<TestDataRow<string>> From(IEnumerable<StoryTestCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(testCase => new TestDataRow<string>(testCase.Key)
        {
            DisplayName = testCase.DisplayName,
            IgnoreMessage = testCase.SkipReason,
            TestCategories = [.. testCase.Categories],
        });
    }

    /// <summary>Fails the test for a failed result and marks it inconclusive for a skipped one; returns passed results.</summary>
    public static StoryResult Passed(StoryResult result) => Passed(result, context: null);

    /// <summary>
    /// As <see cref="Passed(StoryResult)"/>, and attaches the evidence of failed, flaky and visually changed runs (screenshots,
    /// visual diffs, DOM snapshots, traces; see <see cref="StoryAttachments"/>) to the test result as result files. Pass
    /// the test class's <c>TestContext</c> property.
    /// </summary>
    public static StoryResult Passed(StoryResult result, TestContext? context)
    {
        ArgumentNullException.ThrowIfNull(result);
        Attach(result, context);
        if (result.IsBlockingFailure)
        {
            throw new StoryFailedException(result);
        }

        if (result.Outcome == StoryOutcome.Skipped)
        {
            Assert.Inconclusive(result.SkipReason ?? $"Story '{result.StoryId}' was skipped.");
        }

        return result;
    }

    /// <summary>Awaits a run and applies <see cref="Passed(StoryResult)"/>.</summary>
    public static async Task<StoryResult> PassedAsync(Task<StoryResult> run) => Passed(await (run ?? throw new ArgumentNullException(nameof(run))).ConfigureAwait(false));

    /// <summary>Awaits a run and applies <see cref="Passed(StoryResult, TestContext?)"/>.</summary>
    public static async Task<StoryResult> PassedAsync(Task<StoryResult> run, TestContext? context) =>
        Passed(await (run ?? throw new ArgumentNullException(nameof(run))).ConfigureAwait(false), context);

    // Screenshots, visual diffs, DOM snapshots and traces become MSTest result files (TestContext.AddResultFile).
    private static void Attach(StoryResult result, TestContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var attachment in StoryAttachments.For(result))
        {
            try
            {
                context.AddResultFile(attachment.Path);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                // The file went away: the evidence stays in the results directory.
            }
        }
    }
}
