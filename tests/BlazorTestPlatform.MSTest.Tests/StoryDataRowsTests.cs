using BlazorTestPlatform.Results;
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.MSTest;
using FrameworkStories;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace BlazorTestPlatform.MSTest.Tests;

[TestClass]
public sealed class StoryDataRowsTests
{
    public static IEnumerable<TestDataRow<string>> Stories => StoryDataRows.From(FrameworkCatalog.Runnable);

    // Runs for real: "Default" passes, "Skipped" is ignored at discovery and "Needs a browser" is inconclusive.
    [TestMethod]
    [DynamicData(nameof(Stories))]
    public async Task Stories_run_as_data_rows(string id) =>
        await StoryDataRows.PassedAsync(BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get(id), FrameworkCatalog.Options));

    [TestMethod]
    public void Rows_carry_display_names_categories_and_ignore_messages()
    {
        var rows = StoryDataRows.From(FrameworkCatalog.Catalog).ToDictionary(r => r.Value);

        var passing = rows["frameworks-greeting--default"];
        Assert.AreEqual("Frameworks/Greeting/Default", passing.DisplayName);
        Assert.IsNull(passing.IgnoreMessage);
        CollectionAssert.AreEqual(new[] { "greeting", "smoke" }, passing.TestCategories!.ToArray());
        Assert.AreEqual("Waiting for the design review.", rows["frameworks-greeting--skipped"].IgnoreMessage);
        Assert.HasCount(4, rows);
    }

    [TestMethod]
    public async Task Failed_results_fail_the_test_with_the_timeline()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--fails"), FrameworkCatalog.Options);

        var failure = Assert.ThrowsExactly<StoryFailedException>(() => StoryDataRows.Passed(result));
        Assert.Contains("frameworks-greeting--fails", failure.Message);
    }

    [TestMethod]
    public async Task Skipped_results_are_reported_as_inconclusive_not_passed()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--needs-a-browser"), FrameworkCatalog.Options);

        var inconclusive = Assert.ThrowsExactly<AssertInconclusiveException>(() => StoryDataRows.Passed(result));
        Assert.Contains("Compares pixels.", inconclusive.Message);
    }
}
