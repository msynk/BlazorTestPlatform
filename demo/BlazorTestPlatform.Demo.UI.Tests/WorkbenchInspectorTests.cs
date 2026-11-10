using BlazorTestPlatform;
using BlazorTestPlatform.Playwright;
using BlazorTestPlatform.Workbench;
using Microsoft.Playwright;

namespace BlazorTestPlatform.Demo.UI.Tests;

/// <summary>
/// The workbench inspector: a recorder that turns what a person does in the frame into play-function code, and live
/// views of the frame (accessibility tree, actions, requests, JS calls, console).
/// </summary>
public sealed class WorkbenchInspectorTests(WorkbenchFixture workbench)
{
    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public async Task Recording_writes_play_code_with_unique_readable_locators(string mode)
    {
        HostingModelExtensions.TryParse(mode, out var model);
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("data-todo-list--empty"), Viewport.Desktop, ThemeDefinition.Light, model, Browsers.Chromium);
        await using var page = await workbench.Environment.Browser.CreatePageAsync(cell);
        await page.Page.GotoAsync(WorkbenchUrls.Shell("data-todo-list--empty", hostingModel: model).TrimStart('/'));
        var frame = page.Page.FrameLocator(".btp-stage iframe");
        await Assertions.Expect(frame.Locator("[data-btp-root][data-btp-interactive=true]")).ToBeAttachedAsync(new() { Timeout = 30_000 });

        var inspector = page.Page.Locator("#btp-inspector");
        await inspector.GetByRole(AriaRole.Button, new() { Name = "Record" }).ClickAsync();
        await Assertions.Expect(inspector.GetByRole(AriaRole.Button, new() { Name = "Stop recording" })).ToBeVisibleAsync();

        await frame.GetByLabel("New task").FillAsync("Ship it");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await frame.GetByRole(AriaRole.Checkbox, new() { Name = "Ship it" }).CheckAsync();
        await inspector.GetByRole(AriaRole.Button, new() { Name = "Stop recording" }).ClickAsync();

        await Assertions.Expect(inspector.Locator("[data-btp-recording]")).ToHaveTextAsync(string.Join('\n',
            "stories.Add(\"Recorded\", s => s.Play(async ctx =>",
            "{",
            "    await ctx.GetByRole(\"textbox\", \"New task\", exact: true).FillAsync(\"Ship it\");",
            "    await ctx.GetByRole(\"button\", \"Add\", exact: true).ClickAsync();",
            "    await ctx.GetByRole(\"checkbox\", \"Ship it\", exact: true).CheckAsync();",
            "}));"));
        await Assertions.Expect(inspector.GetByRole(AriaRole.Button, new() { Name = "Copy play code" })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task The_inspector_shows_the_accessibility_tree_of_the_frame()
    {
        var cell = new MatrixCase(ComponentStoryTests.Catalog.Get("data-todo-list--empty"), Viewport.Desktop, ThemeDefinition.Light, HostingModel.InteractiveServer, Browsers.Chromium);
        await using var page = await workbench.Environment.Browser.CreatePageAsync(cell);
        await page.Page.GotoAsync(WorkbenchUrls.Shell("data-todo-list--empty", hostingModel: HostingModel.InteractiveServer).TrimStart('/'));
        var frame = page.Page.FrameLocator(".btp-stage iframe");
        await Assertions.Expect(frame.Locator("[data-btp-root][data-btp-interactive=true]")).ToBeAttachedAsync(new() { Timeout = 30_000 });

        var inspector = page.Page.Locator("#btp-inspector");
        await inspector.GetByText("Accessibility tree").ClickAsync();

        await Assertions.Expect(inspector.Locator("[data-btp-aria]")).ToContainTextAsync("- region \"Sprint tasks\":\n  - heading \"Sprint tasks\" [level=2]");
        await Assertions.Expect(inspector.Locator("[data-btp-aria]")).ToContainTextAsync("- button \"Add\" [disabled]");
    }
}
