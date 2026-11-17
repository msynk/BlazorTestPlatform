using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class FormattedStories : Stories<Formatted>
{
    public override string Title => "Tests/Formatted";

    protected override void Define(StoryCollectionBuilder<Formatted> stories)
    {
        stories.Add("German", s => s.Culture("de-DE").Play(async ctx =>
        {
            await ctx.GetByRole("button").ClickAsync();
            await ctx.Expect(ctx.GetByRole("button")).ToHaveTextAsync("Count 1.001");
        }));
        stories.Add("Invariant");
    }
}

public sealed class CultureAndThemeTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<CultureAndThemeTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Stories_render_and_play_in_their_culture_and_leave_the_caller_unchanged()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;

        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-formatted--german"), Options, TestContext.Current.CancellationToken);

        Assert.Equal(Results.StoryOutcome.Passed, result.Outcome);
        Assert.Contains("1.234,5", result.Markup, StringComparison.Ordinal);
        Assert.Contains("31.01.2030", result.Markup, StringComparison.Ordinal);
        Assert.Equal(before, System.Globalization.CultureInfo.CurrentCulture);
        Assert.Equal("de-DE", Catalog.Get("tests-formatted--german").Culture);
        Assert.Null(Catalog.Get("tests-formatted--invariant").Culture);
    }

    [Fact]
    public void Unknown_cultures_are_rejected_when_the_story_is_defined()
    {
        Assert.ThrowsAny<System.Globalization.CultureNotFoundException>(() => StoryCatalog.FromCollections([new BadCultureStories()]));
    }

    [Fact]
    public async Task Right_to_left_and_high_contrast_themes_reach_markup_and_media_queries()
    {
        var options = Options;
        options.Theme = ThemeDefinition.RightToLeft;
        await using (var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-formatted--invariant"), options, TestContext.Current.CancellationToken))
        {
            Assert.StartsWith("<div dir=\"rtl\">", session.Markup.Trim(), StringComparison.Ordinal);
        }

        const string css = ".contrast-only { display: none } @media (forced-colors: active) and (prefers-contrast: more) { .contrast-only { display: block } }";
        var plain = Options;
        plain.Stylesheets.Add(css);
        await using (var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-formatted--invariant"), plain, TestContext.Current.CancellationToken))
        {
            await session.StoryContext.Expect(session.StoryContext.GetByText("High contrast hint")).ToBeHiddenAsync();
        }

        var contrast = Options;
        contrast.Stylesheets.Add(css);
        contrast.Theme = ThemeDefinition.HighContrast;
        await using (var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-formatted--invariant"), contrast, TestContext.Current.CancellationToken))
        {
            await session.StoryContext.Expect(session.StoryContext.GetByText("High contrast hint")).ToBeVisibleAsync();
        }
    }

    private sealed class BadCultureStories : Stories<Formatted>
    {
        public override string Title => "Tests/Bad culture";

        protected override void Define(StoryCollectionBuilder<Formatted> stories) => stories.Add("Default", s => s.Culture("xx-NOPE"));
    }
}
