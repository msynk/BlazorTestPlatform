using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Workbench.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Workbench.Tests;

/// <summary>The in-app driver's C# side reports and waits like the bUnit driver.</summary>
public sealed class InAppDriverParityTests
{
    private static async Task<(FakeDriverModule Module, StoryContext Context)> CreateAsync()
    {
        var story = StoryCatalog.FromAssemblyOf<InAppDriverParityTests>().Get("tests-widget--plain");
        var scope = new StoryServiceScope();
        var render = new StoryRenderContext(story, new ServiceCollection().BuildServiceProvider(), HostingModel.InteractiveServer);
        await scope.ActivateAsync(story, HostingModel.InteractiveServer, render.Authentication);
        var module = new FakeDriverModule();
        var driver = new InAppStoryDriver(module, render, scope, "Server");
        return (module, new StoryContext(story, driver, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Strict_mode_violations_list_the_matches_like_the_bunit_driver()
    {
        var (module, ctx) = await CreateAsync();
        module.Handler = (id, _) => id switch
        {
            "performJson" => "strict mode violation: 2 elements match",
            "queryJson" => """[{"tagName":"button","html":"<button>Save</button>"},{"tagName":"button","html":"<button>Save as</button>"}]""",
            _ => null,
        };

        var error = await Assert.ThrowsAsync<ElementNotActionableException>(() => ctx.GetByRole("button", "Save").ClickAsync(timeout: TimeSpan.FromMilliseconds(100)));

        var lines = error.Message.Split(Environment.NewLine);
        Assert.Equal("Cannot click role=button[name=\"Save\"i]: strict mode violation: 2 elements match (waited 100 ms).", lines[0]);
        Assert.Equal(["  <button>Save</button>", "  <button>Save as</button>"], lines[1..]);
    }

    [Fact]
    public async Task Check_clicks_once_then_waits_for_the_state()
    {
        var (module, ctx) = await CreateAsync();
        var probes = 0;
        module.Handler = (id, _) => id switch
        {
            "performJson" => null,

            // A Server re-render applies the new state a few probes after the click.
            "targetCheckedJson" => ++probes < 3 ? "false" : "true",
            _ => null,
        };

        await ctx.GetByRole("switch", "Dark mode").CheckAsync();

        Assert.Single(module.Calls, c => c.Identifier == "performJson");
        Assert.Equal(3, probes);
    }

    [Fact]
    public async Task Check_fails_when_the_state_never_changes()
    {
        var (module, ctx) = await CreateAsync();
        module.Handler = (id, _) => id == "targetCheckedJson" ? "true" : null;

        var error = await Assert.ThrowsAsync<ElementNotActionableException>(() => ctx.GetByRole("checkbox").UncheckAsync(timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Equal("Clicking did not change the checked state of role=checkbox to False.", error.Message);
        Assert.Single(module.Calls, c => c.Identifier == "performJson");
    }
}
