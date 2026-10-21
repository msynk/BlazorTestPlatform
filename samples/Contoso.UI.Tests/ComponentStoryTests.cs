using BlazorTestPlatform.Bunit;
using Contoso.UI.Stories;

namespace Contoso.UI.Tests;

/// <summary>
/// Every story is an in-memory test: render, play, static accessibility rules and render budgets.
/// Fast (milliseconds per story), no browser required — run on every save.
/// </summary>
public sealed class ComponentStoryTests
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ButtonStories>();

    public static TheoryData<string> Stories => new(Catalog.Ids);

    [Theory]
    [MemberData(nameof(Stories))]
    public Task Story_passes_in_memory(string storyId) => BunitStoryRunner.VerifyAsync(Catalog.Get(storyId));
}

/// <summary>The workbench and tests use the source-generated catalog; it must match a reflection scan exactly.</summary>
public sealed class GeneratedCatalogTests
{
    [Fact]
    public void Stories_assembly_uses_the_generated_catalog()
    {
        var assembly = typeof(ButtonStories).Assembly;
        Assert.True(StoryCatalog.HasGeneratedProvider(assembly));

        var reflected = typeof(StoryCatalog)
            .GetMethod("FromAssembliesByReflection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [new[] { assembly }]) as StoryCatalog;

        Assert.Equal(reflected!.Ids, ComponentStoryTests.Catalog.Ids);
    }
}

/// <summary>bUnit evaluates the design system's media queries against the viewport it pretends to have.</summary>
public sealed class ResponsiveInMemoryTests
{
    [Theory]
    [InlineData("desktop", true)]
    [InlineData("mobile", false)]
    public async Task Consent_detail_follows_the_viewport(string viewport, bool visible)
    {
        var options = new BunitStoryOptions { Viewport = BlazorTestPlatform.Viewport.FindPreset(viewport) };
        await using var session = await BunitStoryRunner.RenderAsync(ComponentStoryTests.Catalog.Get("feedback-cookie-consent--first-visit"), options);
        var detail = session.StoryContext.GetByText("Analytics cookies are only set if you accept.");

        if (visible)
        {
            await session.StoryContext.Expect(detail).ToBeVisibleAsync();
        }
        else
        {
            await session.StoryContext.Expect(detail).ToBeHiddenAsync();
        }
    }
}
