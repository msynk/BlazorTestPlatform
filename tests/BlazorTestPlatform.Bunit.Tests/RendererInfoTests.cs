using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class RenderModeBadgeStories : Stories<RenderModeBadge>
{
    public override string Title => "Tests/Render mode badge";

    protected override void Define(StoryCollectionBuilder<RenderModeBadge> stories) => stories.Add("Default");
}

public sealed class RendererInfoTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<RendererInfoTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Components_reading_renderer_info_render_as_interactive_by_default()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-render-mode-badge--default"), Options, TestContext.Current.CancellationToken);

        await session.StoryContext.Expect(session.StoryContext.GetByRole("status")).ToHaveTextAsync("Server interactive");
    }

    [Fact]
    public async Task Renderer_info_can_simulate_prerendering()
    {
        var options = Options;
        options.RendererInfo = new RendererInfo("Static", isInteractive: false);
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-render-mode-badge--default"), options, TestContext.Current.CancellationToken);

        await session.StoryContext.Expect(session.StoryContext.GetByRole("status")).ToHaveTextAsync("Static static");
    }
}
