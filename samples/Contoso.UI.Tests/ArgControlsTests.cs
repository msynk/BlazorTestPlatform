using System.Text.RegularExpressions;
using BlazorTestPlatform;
using BlazorTestPlatform.Playwright;
using BlazorTestPlatform.Workbench;
using Microsoft.Playwright;

namespace Contoso.UI.Tests;

/// <summary>
/// Live arg controls: the workbench edits component parameters without touching story code. Interactive frames
/// re-render in place, static frames reload, and the shell URL keeps the edits shareable.
/// </summary>
public sealed class ArgControlsTests(WorkbenchFixture workbench)
{
    private static MatrixCase Cell(string storyId, HostingModel mode) =>
        new(ComponentStoryTests.Catalog.Get(storyId), Viewport.Desktop, ThemeDefinition.Light, mode, Browsers.Chromium);

    public static TheoryData<string> Modes => new(HostingModel.StaticServer.ToToken(), HostingModel.InteractiveServer.ToToken(), HostingModel.InteractiveWebAssembly.ToToken(), HostingModel.InteractiveAuto.ToToken());

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Frame_args_render_under_every_mode(string mode)
    {
        HostingModelExtensions.TryParse(mode, out var model);
        var options = new StoryPageOptions();
        options.Args["Variant"] = "Danger";
        options.Args["Disabled"] = "true";
        await using var page = await workbench.Environment.Browser.OpenAsync(Cell("inputs-button--primary", model), options);
        var ctx = page.CreateStoryContext(cancellationToken: TestContext.Current.CancellationToken);

        await ctx.Expect(ctx.GetByRole("button", "Save changes")).ToHaveClassAsync("c-button--danger");
        await ctx.Expect(ctx.GetByRole("button", "Save changes")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Links_cannot_inject_markup()
    {
        var options = new StoryPageOptions();
        options.Args["Title"] = "<img src=x onerror=alert(1)>";
        await using var page = await workbench.Environment.Browser.OpenAsync(Cell("feedback-alert--dismissible", HostingModel.InteractiveServer), options);

        await Assertions.Expect(page.Page.Locator("[data-btp-arg-errors]")).ToContainTextAsync("cannot contain '<'");
        await Assertions.Expect(page.Page.Locator(".c-alert__title")).ToHaveTextAsync("Heads up");
        Assert.Equal(0, await page.Page.Locator("img").CountAsync());
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public async Task Interactive_frames_update_in_place(string mode)
    {
        HostingModelExtensions.TryParse(mode, out var model);
        await using var page = await workbench.Environment.Browser.CreatePageAsync(Cell("inputs-button--primary", model));
        await page.Page.GotoAsync(WorkbenchUrls.Shell("inputs-button--primary", hostingModel: model).TrimStart('/'));
        var frame = page.Page.FrameLocator(".btp-stage iframe");
        await Assertions.Expect(frame.Locator("[data-btp-root][data-btp-interactive=true]")).ToBeAttachedAsync(new() { Timeout = 30_000 });
        await frame.Locator("body").EvaluateAsync("() => window.__btpMarker = 'kept'");

        await page.Page.Locator("#btp-arg-Variant").SelectOptionAsync("Danger");
        await page.Page.Locator("#btp-arg-Disabled").CheckAsync();

        var button = frame.GetByRole(AriaRole.Button, new() { Name = "Save changes" });
        await Assertions.Expect(button).ToHaveClassAsync(new Regex("c-button--danger"));
        await Assertions.Expect(button).ToBeDisabledAsync();
        Assert.Equal("kept", await frame.Locator("body").EvaluateAsync<string?>("() => window.__btpMarker"));
        await Assertions.Expect(page.Page).ToHaveURLAsync(new Regex(@"arg\.Variant=Danger.*arg\.Disabled=true|arg\.Disabled=true.*arg\.Variant=Danger"));

        // The URL is the shareable state: a fresh load shows the same controls and preview.
        await page.Page.ReloadAsync();
        await Assertions.Expect(page.Page.Locator("#btp-arg-Variant")).ToHaveValueAsync("Danger");
        await Assertions.Expect(frame.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveClassAsync(new Regex("c-button--danger"));

        // Back to the story's own value: the override disappears from the URL.
        await page.Page.Locator("#btp-arg-Variant").SelectOptionAsync("Primary");
        await Assertions.Expect(button).ToHaveClassAsync(new Regex("c-button--primary"));
        await Assertions.Expect(page.Page).Not.ToHaveURLAsync(new Regex(@"arg\.Variant="));
    }

    [Fact]
    public async Task Static_frames_reload_with_the_new_values()
    {
        await using var page = await workbench.Environment.Browser.CreatePageAsync(Cell("feedback-alert--dismissible", HostingModel.StaticServer));
        await page.Page.GotoAsync(WorkbenchUrls.Shell("feedback-alert--dismissible", hostingModel: HostingModel.StaticServer).TrimStart('/'));
        var frame = page.Page.FrameLocator(".btp-stage iframe");
        await Assertions.Expect(frame.Locator(".c-alert__title")).ToHaveTextAsync("Heads up");

        await page.Page.Locator("#btp-arg-Title").FillAsync("Server maintenance tonight");
        await page.Page.Locator("#btp-arg-Severity").SelectOptionAsync("Warning");

        await Assertions.Expect(frame.Locator(".c-alert__title")).ToHaveTextAsync("Server maintenance tonight");
        await Assertions.Expect(frame.Locator(".c-alert")).ToHaveClassAsync(new Regex("c-alert--warning"));
        await Assertions.Expect(page.Page.Locator("[data-btp-control-status]")).ToHaveTextAsync("Reloaded.");
    }

    [Fact]
    public async Task Typed_markup_renders_as_text_and_is_rejected_when_followed_as_a_link()
    {
        await using var page = await workbench.Environment.Browser.CreatePageAsync(Cell("feedback-alert--dismissible", HostingModel.InteractiveServer));
        await page.Page.GotoAsync(WorkbenchUrls.Shell("feedback-alert--dismissible", hostingModel: HostingModel.InteractiveServer).TrimStart('/'));
        var frame = page.Page.FrameLocator(".btp-stage iframe");
        await Assertions.Expect(frame.Locator("[data-btp-root][data-btp-interactive=true]")).ToBeAttachedAsync(new() { Timeout = 30_000 });

        await page.Page.Locator("#btp-arg-Title").FillAsync("<b>Bold</b>");
        await Assertions.Expect(frame.Locator(".c-alert__title")).ToHaveTextAsync("<b>Bold</b>");
        Assert.Equal(0, await frame.Locator(".c-alert__title b").CountAsync());

        await page.Page.ReloadAsync();
        await Assertions.Expect(page.Page.Locator("[data-btp-control-errors]")).ToContainTextAsync("cannot contain '<'");
        await Assertions.Expect(frame.Locator(".c-alert__title")).ToHaveTextAsync("Heads up");
    }
}
