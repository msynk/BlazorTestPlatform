using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

/// <summary>Browser default behaviour the bUnit driver must reproduce exactly as Chromium, Firefox and WebKit do.</summary>
public sealed class BrowserFidelityTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<BrowserFidelityTests>();

    private static readonly BtpSettings Settings = BtpSettings.Load(
        Path.Combine(Path.GetTempPath(), "btp-bunit-tests"),
        new Dictionary<string, string?>());

    private static BunitStoryOptions Options => new() { Settings = Settings, WriteResults = false };

    private static Task<BunitStorySession> RenderAsync(string storyId) => BunitStoryRunner.RenderAsync(Catalog.Get(storyId), Options);

    [Fact]
    public async Task Tab_skips_disabled_fieldsets_inert_subtrees_and_extra_summaries()
    {
        await using var session = await RenderAsync("tests-focus-scopes--default");
        var ctx = session.StoryContext;

        await ctx.ExpectTabOrderAsync(
            ctx.GetByRole("button", "First"),
            ctx.GetByRole("button", "Legend action"),
            ctx.GetByText("More", exact: true),
            ctx.GetByRole("button", "Last"));
        await ctx.TabAsync();
        Assert.Null(await session.Driver.GetFocusedElementAsync(CancellationToken.None));

        await ctx.Expect(ctx.GetByRole("button", "Locked")).ToBeDisabledAsync();
        await ctx.Expect(ctx.GetByLabel("Locked field")).ToBeDisabledAsync();
        await ctx.Expect(ctx.GetByRole("button", "Legend action")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Clicking_a_control_disabled_by_its_fieldset_does_not_focus_it()
    {
        await using var session = await RenderAsync("tests-focus-scopes--default");
        var ctx = session.StoryContext;

        await ctx.GetByRole("button", "First").FocusAsync();
        await ctx.GetByRole("button", "Locked").ClickAsync(force: true);

        Assert.Null(await session.Driver.GetFocusedElementAsync(CancellationToken.None));
        await ctx.Expect(ctx.GetByTestId("clicks")).ToHaveTextAsync("locked=0 inert=0 panel=0");
    }

    [Fact]
    public async Task Inert_content_cannot_be_clicked_or_focused()
    {
        await using var session = await RenderAsync("tests-focus-scopes--default");
        var ctx = session.StoryContext;
        var inert = ctx.GetByText("Inert button");

        var error = await Assert.ThrowsAsync<ElementNotActionableException>(() => inert.ClickAsync(timeout: TimeSpan.FromMilliseconds(100)));
        Assert.Contains("element is inert", error.Message, StringComparison.Ordinal);

        await inert.FocusAsync();
        Assert.Null(await session.Driver.GetFocusedElementAsync(CancellationToken.None));

        // A forced click passes through inert content to what is underneath: the focusable panel around it.
        await ctx.GetByText("Inert text").ClickAsync(force: true);
        await ctx.Expect(ctx.GetByTestId("clicks")).ToHaveTextAsync("locked=0 inert=0 panel=1");
        await ctx.Expect(ctx.GetByTestId("panel")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Enter_submits_from_checkboxes_and_radios_while_space_toggles_them()
    {
        await using var session = await RenderAsync("tests-keyboard-form--default");
        var ctx = session.StoryContext;
        var agree = ctx.GetByLabel("Agree");
        var basic = ctx.GetByLabel("Basic");
        var state = ctx.GetByTestId("state");

        await agree.PressAsync(Keys.Enter);
        await ctx.Expect(state).ToHaveTextAsync("submits=1 default=1 agree=False plan=none link=0 button=0");
        await ctx.Expect(agree).Not.ToBeCheckedAsync();
        await ctx.PressAsync(Keys.Space);
        await ctx.Expect(agree).ToBeCheckedAsync();

        await basic.PressAsync(Keys.Enter);
        await ctx.Expect(state).ToHaveTextAsync("submits=2 default=2 agree=True plan=none link=0 button=0");
        await ctx.Expect(basic).Not.ToBeCheckedAsync();
        await ctx.PressAsync(Keys.Space);
        await ctx.Expect(basic).ToBeCheckedAsync();
        await ctx.Expect(state).ToHaveTextAsync("submits=2 default=2 agree=True plan=basic link=0 button=0");
    }

    [Fact]
    public async Task Enter_activates_buttons_and_links_but_space_only_buttons()
    {
        await using var session = await RenderAsync("tests-keyboard-form--default");
        var ctx = session.StoryContext;

        await ctx.GetByRole("link", "Top").PressAsync(Keys.Enter);
        await ctx.PressAsync(Keys.Space);
        await ctx.GetByRole("button", "Plain").PressAsync(Keys.Enter);
        await ctx.PressAsync(Keys.Space);

        await ctx.Expect(ctx.GetByTestId("state")).ToHaveTextAsync("submits=0 default=0 agree=False plan=none link=1 button=2");
    }

    [Fact]
    public async Task Enter_in_a_text_field_clicks_the_default_button()
    {
        await using var session = await RenderAsync("tests-keyboard-form--default");
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Name").FillAsync("Ada");
        await ctx.PressAsync(Keys.Enter);

        await ctx.Expect(ctx.GetByTestId("state")).ToHaveTextAsync("submits=1 default=1 agree=False plan=none link=0 button=0");
    }

    [Fact]
    public async Task Implicit_submission_follows_the_default_button_rules()
    {
        await using var session = await RenderAsync("tests-implicit-submission--default");
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Search").PressAsync(Keys.Enter);
        await ctx.GetByLabel("First name").PressAsync(Keys.Enter);
        await ctx.GetByLabel("Code").PressAsync(Keys.Enter);
        await ctx.GetByLabel("Outside").PressAsync(Keys.Enter);
        await ctx.GetByLabel("Standalone").FillAsync("draft");
        await ctx.PressAsync(Keys.Enter);

        // One field and no submit button submits; two fields don't; a disabled default button blocks submission.
        // Enter commits the edit (change) even outside a form.
        await ctx.Expect(ctx.GetByTestId("counts")).ToHaveTextAsync("single=1 pair=0 blocked=0 external=1 committed=draft");
        await ctx.Expect(ctx.GetByLabel("Standalone")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Programmatic_focus_fires_change_blur_and_focus_events()
    {
        await using var session = await RenderAsync("tests-focus-events--default");
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Field").FillAsync("Ada");
        await ctx.PressAsync(Keys.ArrowDown);

        await ctx.Expect(ctx.GetByRole("button", "Target", exact: true)).ToBeFocusedAsync();
        await ctx.Expect(ctx.GetByTestId("log")).ToHaveTextAsync("field change Ada, field blur, field focusout, target focus, target focusin, target keyup");
    }

    [Fact]
    public async Task Focus_moved_by_a_focus_handler_is_applied_in_turn()
    {
        await using var session = await RenderAsync("tests-focus-events--default");
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Field").FocusAsync();
        await ctx.PressAsync(Keys.PageDown);

        await ctx.Expect(ctx.GetByRole("button", "Target", exact: true)).ToBeFocusedAsync();
        await ctx.Expect(ctx.GetByTestId("log")).ToHaveTextAsync("field blur, field focusout, redirect focus, target focus, target focusin, target keyup");
    }

    [Fact]
    public async Task Programmatic_focus_ignores_elements_that_cannot_take_it()
    {
        await using var session = await RenderAsync("tests-focus-events--default");
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Field").FocusAsync();
        await ctx.PressAsync(Keys.ArrowUp);

        await ctx.Expect(ctx.GetByLabel("Field")).ToBeFocusedAsync();
        await ctx.Expect(ctx.GetByTestId("log")).Not.ToContainTextAsync("blur");
    }

    [Fact]
    public async Task Handlers_bouncing_focus_back_and_forth_do_not_hang()
    {
        await using var session = await RenderAsync("tests-focus-events--default");
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Field").FocusAsync();
        await ctx.PressAsync(Keys.End).WaitAsync(TimeSpan.FromSeconds(10));

        var focused = await session.Driver.GetFocusedElementAsync(CancellationToken.None);
        Assert.Contains(focused?.Text ?? "nothing", new[] { "Ping", "Pong" });
    }
}
