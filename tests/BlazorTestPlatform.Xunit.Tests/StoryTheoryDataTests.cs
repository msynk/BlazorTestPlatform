using BlazorTestPlatform.Results;
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.Xunit;
using FrameworkStories;
using Xunit.Sdk;

namespace BlazorTestPlatform.Xunit.Tests;

public sealed class StoryTheoryDataTests
{
    public static TheoryData<string> Stories => StoryTheoryData.From(FrameworkCatalog.Runnable);

    // Runs for real: "Default" passes, "Skipped" is skipped at discovery and "Needs a browser" is skipped by the driver.
    [Theory]
    [MemberData(nameof(Stories))]
    public async Task Stories_run_as_theory_rows(string id) =>
        await StoryTheoryData.PassedAsync(BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get(id), FrameworkCatalog.Options));

    [Fact]
    public void Rows_carry_display_names_traits_and_skip_reasons()
    {
        var rows = StoryTheoryData.From(FrameworkCatalog.Catalog).Cast<TheoryDataRow<string>>().ToDictionary(r => r.Data);

        var passing = rows["frameworks-greeting--default"];
        Assert.Equal("Frameworks/Greeting/Default", passing.TestDisplayName);
        Assert.Null(passing.Skip);
        Assert.Equal(["greeting", "smoke"], passing.Traits["Category"].Order(StringComparer.Ordinal));
        Assert.Equal("Waiting for the design review.", rows["frameworks-greeting--skipped"].Skip);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public async Task Failed_results_fail_the_test_with_the_timeline()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--fails"), FrameworkCatalog.Options);

        var failure = Assert.Throws<StoryFailedException>(() => StoryTheoryData.Passed(result));
        Assert.Contains("frameworks-greeting--fails", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skipped_results_are_reported_as_skipped_not_passed()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--needs-a-browser"), FrameworkCatalog.Options);

        // Assert.Throws lets skip exceptions through (the test would be skipped), so catch it by hand.
        SkipException? skip = null;
        try
        {
            StoryTheoryData.Passed(result);
        }
        catch (SkipException ex)
        {
            skip = ex;
        }

        Assert.NotNull(skip);
        Assert.Contains("Compares pixels.", skip.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matrix_cells_become_rows_keyed_by_cell()
    {
        var matrix = new TestMatrix { Viewports = { Viewport.Desktop, Viewport.Mobile } };
        var cells = matrix.Expand(FrameworkCatalog.Runnable.Take(1)).ToArray();

        var rows = StoryTheoryData.From(cells).Cast<TheoryDataRow<string>>().ToArray();

        Assert.Equal(cells.Select(c => c.Key), rows.Select(r => r.Data));
        Assert.Equal(cells.Select(c => c.ToString()), rows.Select(r => r.TestDisplayName));
    }
}
