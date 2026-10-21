using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Coverage;

namespace Contoso.UI.Tests;

/// <summary>
/// Story state coverage: every component of the design system has stories, and the stories render every state of its
/// <c>bool</c> and enum parameters. The report lands next to the results, where <c>btp report</c> shows it (HTML,
/// Markdown and the pull-request comment).
/// </summary>
public sealed class StoryCoverageTests
{
    [Fact]
    public void Every_component_state_has_a_story()
    {
        var report = StoryCoverage.Analyze(ComponentStoryTests.Catalog, StoryCoverage.ComponentsIn(typeof(Button).Assembly));
        report.Save(BtpSettings.Current.ResultsDirectoryPath);

        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, report.Components.Select(c => $"{c.Title,-28} {c.CoveredStates,2}/{c.TotalStates,-2} {string.Join(", ", c.MissingStates)}")));
        report.ThrowIfBelow(1, requireStoriesForEveryComponent: true);
    }
}
