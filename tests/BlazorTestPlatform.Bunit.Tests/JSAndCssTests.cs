using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class ThemeBadgeStories : Stories<ThemeBadge>
{
    public override string Title => "Tests/Theme badge";

    protected override void Define(StoryCollectionBuilder<ThemeBadge> stories)
    {
        stories.Defaults(s => s.MockJS(js => js
            .Setup("localStorage.getItem", "dark", "theme")
            .Module("./media.js", media => media.Setup("prefersReducedMotion", true))));

        stories.Add("Mocked");
        stories.Add("Tracks clicks", s => s
            .MockJS(js => js.SetupVoid("analytics.track"))
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByTestId("theme")).ToHaveTextAsync("Theme: dark");
                await ctx.GetByRole("button", "Track").ClickAsync();
                await ctx.ExpectJSCall("analytics.track").ToHaveBeenLastCalledWithAsync("clicked");
                await ctx.ExpectJSCall("prefersReducedMotion", "./media.js").ToHaveBeenCalledAsync(times: 1);
            }));
        stories.Add("Strict without track setup", s => s
            .MockJS(js => js.Strict = true)
            .Play(ctx => ctx.GetByRole("button", "Track").ClickAsync()));
    }
}

public sealed class DisclosureStories : Stories<Disclosure>
{
    public override string Title => "Tests/Disclosure";

    protected override void Define(StoryCollectionBuilder<Disclosure> stories) => stories.Add("Default");
}

public sealed class JSAndCssTests
{
    private const string DisclosureCss = """
        .disclosure-panel { display: none; }
        .disclosure-panel.is-open { display: block; }
        @media (min-width: 600px) { .disclosure-panel { display: block; } }
        """;

    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<JSAndCssTests>();

    private static readonly BtpSettings Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>());

    [Fact]
    public async Task Js_mocks_answer_calls_and_module_calls()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-theme-badge--mocked"), new BunitStoryOptions { Settings = Settings });

        await session.StoryContext.Expect(session.StoryContext.GetByTestId("theme")).ToHaveTextAsync("Theme: dark");
        await session.StoryContext.Expect(session.StoryContext.GetByTestId("motion")).ToHaveTextAsync("Reduced motion: True");
    }

    [Fact]
    public async Task Js_calls_are_recorded_in_order_with_their_module()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-theme-badge--mocked"), new BunitStoryOptions { Settings = Settings });
        await session.StoryContext.Expect(session.StoryContext.GetByTestId("motion")).ToHaveTextAsync("Reduced motion: True");

        var calls = await session.StoryContext.GetJSCallsAsync();

        Assert.Equal(["localStorage.getItem(theme) [mocked]", "import(./media.js) [mocked]", "./media.js:prefersReducedMotion() [mocked]"], calls.Select(c => c.ToString()));
        Assert.Equal([1, 2, 3], calls.Select(c => c.Sequence));
        Assert.Equal(calls.Select(c => c.ToString()), JSCallLog.Parse(JSCallLog.ToJson(calls)).Select(c => c.ToString()));
    }

    [Fact]
    public async Task Js_call_assertions_require_declared_mocks()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), new BunitStoryOptions { Settings = Settings });

        var error = Assert.Throws<InvalidOperationException>(() => session.StoryContext.ExpectJSCall("anything"));
        Assert.Contains("MockJS", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Void_mocks_complete_and_strict_mode_rejects_unexpected_calls()
    {
        var options = new BunitStoryOptions { Settings = Settings, WriteResults = false };

        Assert.Equal(StoryOutcome.Passed, (await BunitStoryRunner.RunAsync(Catalog.Get("tests-theme-badge--tracks-clicks"), options)).Outcome);

        var strict = await BunitStoryRunner.RunAsync(Catalog.Get("tests-theme-badge--strict-without-track-setup"), options);
        Assert.Equal(StoryOutcome.Failed, strict.Outcome);
        Assert.Contains("analytics.track", strict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stylesheets_make_css_hidden_elements_hidden()
    {
        var options = new BunitStoryOptions { Settings = Settings, Viewport = Viewport.Mobile };
        options.Stylesheets.Add(DisclosureCss);
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), options);
        var ctx = session.StoryContext;

        await ctx.Expect(ctx.GetByRole("link", "Read more")).ToHaveCountAsync(0);
        await ctx.Expect(ctx.Locate("#details-panel")).ToBeHiddenAsync();

        await ctx.GetByRole("button", "Details").ClickAsync();

        await ctx.Expect(ctx.Locate("#details-panel")).ToBeVisibleAsync();
        await ctx.GetByRole("link", "Read more").FocusAsync();
        await ctx.Expect(ctx.GetByRole("link", "Read more")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Without_stylesheets_behaviour_is_unchanged()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), new BunitStoryOptions { Settings = Settings });

        await session.StoryContext.Expect(session.StoryContext.Locate("#details-panel")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Css_hidden_content_is_skipped_by_tab_order()
    {
        var options = new BunitStoryOptions { Settings = Settings, Viewport = Viewport.Mobile };
        options.Stylesheets.Add(DisclosureCss);
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), options);
        var ctx = session.StoryContext;

        await ctx.TabAsync(2);

        Assert.Null(await ctx.GetFocusedAsync());
    }

    [Fact]
    public async Task Media_queries_follow_the_viewport()
    {
        var desktop = new BunitStoryOptions { Settings = Settings };
        desktop.Stylesheets.Add(DisclosureCss);
        await using (var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), desktop))
        {
            // Desktop is the default, as in browser runs: the min-width rule shows the panel.
            Assert.Equal(Viewport.Desktop, session.RenderContext.Viewport);
            await session.StoryContext.Expect(session.StoryContext.Locate("#details-panel")).ToBeVisibleAsync();
        }

        var phone = new BunitStoryOptions { Settings = Settings, Viewport = Viewport.MobileSmall };
        phone.Stylesheets.Add(DisclosureCss);
        await using (var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), phone))
        {
            await session.StoryContext.Expect(session.StoryContext.Locate("#details-panel")).ToBeHiddenAsync();
        }
    }

    [Fact]
    public async Task Dark_theme_drives_prefers_color_scheme()
    {
        var options = new BunitStoryOptions { Settings = Settings, Theme = ThemeDefinition.Dark };
        options.Stylesheets.Add("@media (prefers-color-scheme: dark) { .disclosure-panel { display: none } }");
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-disclosure--default"), options);

        Assert.Equal("dark", session.RenderContext.Theme?.Name);
        await session.StoryContext.Expect(session.StoryContext.Locate("#details-panel")).ToBeHiddenAsync();
    }

    [Fact]
    public void Settings_supply_the_in_memory_viewport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-css-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "btp.json"), """{ "bunit": { "viewport": "tablet" } }""");

        Assert.Equal(Viewport.Tablet, BtpSettings.Load(directory, new Dictionary<string, string?>()).BunitViewport);
        Assert.Equal(new Viewport("1024x600", 1024, 600), BtpSettings.Load(directory, new Dictionary<string, string?> { ["BTP_BUNIT_VIEWPORT"] = "1024x600" }).BunitViewport);
        Assert.Null(BtpSettings.Load(Path.Combine(directory, "missing"), new Dictionary<string, string?>()).BunitViewport);
        Assert.Throws<FormatException>(() => BtpSettings.ParseViewport("huge"));
    }

    [Fact]
    public void Settings_file_supplies_stylesheets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "btp-css-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "theme.css"), DisclosureCss);
        File.WriteAllText(Path.Combine(directory, "btp.json"), """{ "bunit": { "stylesheets": ["theme.css"] } }""");

        var settings = BtpSettings.Load(directory, new Dictionary<string, string?>());

        Assert.Equal([Path.Combine(directory, "theme.css")], settings.BunitStylesheets);
    }
}
