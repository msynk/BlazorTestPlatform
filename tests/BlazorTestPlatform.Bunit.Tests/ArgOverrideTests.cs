using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class ArgOverrideTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ArgOverrideTests>();

    private static readonly BtpSettings Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>());

    [Fact]
    public async Task Args_override_story_values_and_component_defaults()
    {
        var options = new BunitStoryOptions { Settings = Settings, WriteResults = false };
        options.Args["Max"] = "3";
        options.Args["Value"] = "1";
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-rating--bound"), options);
        var ctx = session.StoryContext;

        await ctx.Expect(ctx.Locate("output")).ToHaveTextAsync("1 of 3");
        await ctx.Expect(ctx.GetByRole("radio")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task Live_overrides_re_render_and_keep_two_way_bindings_working()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-rating--bound"), new BunitStoryOptions { Settings = Settings, WriteResults = false });
        var ctx = session.StoryContext;
        var output = ctx.Locate("output");

        await session.RenderContext.SetArgOverridesAsync(new Dictionary<string, object?> { ["Max"] = 4 });
        await ctx.Expect(output).ToHaveTextAsync("2 of 4");

        await ctx.GetByRole("radio", "3 stars").ClickAsync();
        await ctx.Expect(output).ToHaveTextAsync("3 of 4");

        await session.RenderContext.SetArgOverridesAsync(new Dictionary<string, object?> { ["Value"] = 1 });
        await ctx.Expect(output).ToHaveTextAsync("1 of 5");

        // Removing every override restores the story: the binding returns to its initial value.
        await session.RenderContext.SetArgOverridesAsync(new Dictionary<string, object?>());
        await ctx.Expect(output).ToHaveTextAsync("2 of 5");
    }

    [Fact]
    public async Task Invalid_args_fail_fast()
    {
        var options = new BunitStoryOptions { Settings = Settings, WriteResults = false };
        options.Args["Max"] = "lots";

        var error = await Assert.ThrowsAsync<ArgumentException>(() => BunitStoryRunner.RenderAsync(Catalog.Get("tests-rating--bound"), options));

        Assert.Contains("'Max': 'lots' is not a valid int.", error.Message, StringComparison.Ordinal);
    }
}
