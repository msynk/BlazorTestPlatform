using BlazorTestPlatform.Results;
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.TUnit;
using FrameworkStories;

namespace BlazorTestPlatform.TUnit.Tests;

public sealed class StoryDataRowsTests
{
    public static IEnumerable<TestDataRow<string>> Stories() => StoryDataRows.From(FrameworkCatalog.Runnable);

    // Runs for real: "Default" passes, "Skipped" is skipped at discovery and "Needs a browser" is skipped by the driver.
    [Test]
    [MethodDataSource(nameof(Stories))]
    public async Task Stories_run_as_data_rows(string id) =>
        await StoryDataRows.PassedAsync(BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get(id), FrameworkCatalog.Options));

    [Test]
    public async Task Rows_carry_display_names_categories_and_skip_reasons()
    {
        var rows = StoryDataRows.From(FrameworkCatalog.Catalog).ToDictionary(r => r.Data);

        var passing = rows["frameworks-greeting--default"];
        await Assert.That(passing.DisplayName).IsEqualTo("Frameworks/Greeting/Default");
        await Assert.That(passing.Skip).IsNull();
        await Assert.That(string.Join(",", passing.Categories!)).IsEqualTo("greeting,smoke");
        await Assert.That(rows["frameworks-greeting--skipped"].Skip).IsEqualTo("Waiting for the design review.");
        await Assert.That(rows.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Failed_results_fail_the_test_with_the_timeline()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--fails"), FrameworkCatalog.Options);

        var failure = Catch(() => StoryDataRows.Passed(result));
        await Assert.That(failure is StoryFailedException).IsTrue();
        await Assert.That(failure!.Message).Contains("frameworks-greeting--fails");
    }

    [Test]
    public async Task Skipped_results_are_reported_as_skipped_not_passed()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--needs-a-browser"), FrameworkCatalog.Options);

        var skip = Catch(() => StoryDataRows.Passed(result));
        await Assert.That(skip?.GetType().Name ?? "").Contains("Skip");
        await Assert.That(skip!.Message).Contains("Compares pixels.");
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
