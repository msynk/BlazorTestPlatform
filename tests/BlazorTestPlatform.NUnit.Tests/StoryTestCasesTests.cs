using BlazorTestPlatform.Results;
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.NUnit;
using FrameworkStories;
using NUnit.Framework;

namespace BlazorTestPlatform.NUnit.Tests;

public sealed class StoryTestCasesTests
{
    public static IEnumerable<TestCaseData> Stories => StoryTestCases.From(FrameworkCatalog.Runnable);

    // Runs for real: "Default" passes, "Skipped" is ignored at discovery and "Needs a browser" is ignored by the driver.
    [TestCaseSource(nameof(Stories))]
    public async Task Stories_run_as_test_cases(string id) =>
        await StoryTestCases.PassedAsync(BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get(id), FrameworkCatalog.Options));

    [Test]
    public void Cases_carry_names_categories_and_ignore_reasons()
    {
        var cases = StoryTestCases.From(FrameworkCatalog.Catalog).ToDictionary(c => (string)c.Arguments[0]!);

        var passing = cases["frameworks-greeting--default"];
        Assert.That(passing.TestName, Is.EqualTo("Frameworks/Greeting/Default"));
        Assert.That(passing.RunState, Is.EqualTo(global::NUnit.Framework.Interfaces.RunState.Runnable));
        Assert.That(passing.Properties["Category"], Is.EquivalentTo(new[] { "greeting", "smoke" }));
        var skipped = cases["frameworks-greeting--skipped"];
        Assert.That(skipped.RunState, Is.EqualTo(global::NUnit.Framework.Interfaces.RunState.Ignored));
        Assert.That(skipped.Properties.Get("_SKIPREASON"), Is.EqualTo("Waiting for the design review."));
        Assert.That(cases, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task Failed_results_fail_the_test_with_the_timeline()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--fails"), FrameworkCatalog.Options);

        var failure = Assert.Throws<StoryFailedException>(() => StoryTestCases.Passed(result));
        Assert.That(failure!.Message, Does.Contain("frameworks-greeting--fails"));
    }

    [Test]
    public async Task Skipped_results_are_reported_as_ignored_not_passed()
    {
        var result = await BunitStoryRunner.RunAsync(FrameworkCatalog.Catalog.Get("frameworks-greeting--needs-a-browser"), FrameworkCatalog.Options);

        var ignored = Assert.Throws<IgnoreException>(() => StoryTestCases.Passed(result));
        Assert.That(ignored!.Message, Does.Contain("Compares pixels."));
    }
}
