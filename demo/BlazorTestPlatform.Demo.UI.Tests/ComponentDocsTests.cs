using BlazorTestPlatform;
using BlazorTestPlatform.Playwright;
using BlazorTestPlatform.Workbench;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// Component documentation pages (autodocs): the workbench shows each component's summary, parameters (with types,
/// defaults, options and the XML documentation the design system ships) and every story with a live preview.
/// </summary>
public sealed class ComponentDocsTests(WorkbenchFixture workbench)
{
    [Fact]
    public void The_model_reads_parameters_defaults_and_xml_documentation()
    {
        var doc = ComponentDocs.For(ComponentStoryTests.Catalog.Groups.Single(g => g.Title == "Feedback/Alert"));

        Assert.Equal("BlazorTestPlatform.Demo.UI.Alert", doc.Component);
        Assert.StartsWith("A message about the page or a task", doc.Summary, StringComparison.Ordinal);
        var severity = doc.Parameters.Single(p => p.Name == "Severity");
        Assert.Equal("AlertSeverity", severity.Type);
        Assert.Equal("Info", severity.Default);
        Assert.Equal(["Info", "Success", "Warning", "Error"], severity.Options);
        Assert.Contains("role=\"alert\"", severity.Summary, StringComparison.Ordinal);
        Assert.Equal(ComponentParameterKind.Event, doc.Parameters.Single(p => p.Name == "OnDismiss").Kind);
        Assert.Equal(ComponentParameterKind.Content, doc.Parameters.Single(p => p.Name == "ChildContent").Kind);
        Assert.Equal("string?", doc.Parameters.Single(p => p.Name == "Title").Type);
        Assert.Equal("EventCallback<MouseEventArgs>", ComponentDocs.For(ComponentStoryTests.Catalog.Groups.Single(g => g.Title == "Inputs/Button")).Parameters.Single(p => p.Name == "OnClick").Type);
    }

    [Fact]
    public async Task The_docs_page_shows_parameters_and_live_stories()
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("feedback-alert--dismissible"), Viewport.Desktop, ThemeDefinition.Light, HostingModel.InteractiveServer, Browsers.Chromium);
        await using var page = await workbench.Environment.Browser.CreatePageAsync(cell);

        await page.Page.GotoAsync(WorkbenchUrls.Docs("feedback-alert", hostingModel: HostingModel.InteractiveServer).TrimStart('/'));

        await Assertions.Expect(page.Page.GetByRole(AriaRole.Heading, new() { Name = "Feedback/Alert", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.Page.Locator("tr[data-parameter=Severity]")).ToContainTextAsync("Info | Success | Warning | Error");
        await Assertions.Expect(page.Page.Locator("tr[data-parameter=OnDismiss]")).ToContainTextAsync("event");
        await Assertions.Expect(page.Page.Locator("tr[data-parameter=OnDismiss]")).ToContainTextAsync("Raised once the alert is dismissed.");
        var stories = ComponentStoryTests.Catalog.Groups.Single(g => g.Title == "Feedback/Alert").Stories.Count;
        await Assertions.Expect(page.Page.Locator(".btp-docs__story")).ToHaveCountAsync(stories);

        // Previews are the real story frames.
        var preview = page.Page.FrameLocator("[data-story='feedback-alert--dismissible'] iframe");
        await preview.Locator("[data-btp-root][data-btp-status=ready]").ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(preview.GetByRole(AriaRole.Button, new() { Name = "Dismiss" })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The sidebar links to the page, and it passes axe.
        await Assertions.Expect(page.Page.Locator(".btp-docs-link a[aria-current=page]")).ToHaveCountAsync(1);
        var report = await AxeAccessibilityScanner.ScanAsync(page.Page, new BlazorTestPlatform.Accessibility.AccessibilityPolicy(), new StoryAccessibilityOptions(), ".btp-docs");
        Assert.DoesNotContain(report.Violations, v => v.Impact >= BlazorTestPlatform.Accessibility.AccessibilityImpact.Serious);
    }
}
