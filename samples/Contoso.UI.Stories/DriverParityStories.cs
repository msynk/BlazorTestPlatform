using BlazorTestPlatform;

namespace Contoso.UI.Stories;

/// <summary>A part of <see cref="DriverParityPanel"/>.</summary>
public enum DriverParitySection
{
    /// <summary>Implicit roles, accessible names and the story scope.</summary>
    Landmarks,

    /// <summary>Element text around line-breaking and non-rendered elements.</summary>
    Text,

    /// <summary>Tab order, Enter and Space.</summary>
    Keyboard,

    /// <summary>A switch that updates asynchronously, and a native checkbox.</summary>
    Checked,
}

/// <summary>
/// Driver parity: these play scripts run in bUnit, in Playwright and in the in-app driver (ComponentStoryTests,
/// CrossHostingTests, InAppPlayTests), so roles, names, text, keyboard behaviour and Check/Uncheck must agree everywhere,
/// and agree with what real browsers do. Test fixtures, not design-system components: no visual baselines.
/// </summary>
public sealed class DriverParityStories : Stories<DriverParityPanel>
{
    public override string Title => "Testing/Driver parity";

    protected override void Define(StoryCollectionBuilder<DriverParityPanel> stories)
    {
        stories.Defaults(s => s.NoVisual());

        stories.Add("Roles and names", s => s
            .Arg(p => p.Section, DriverParitySection.Landmarks)
            .Play(async ctx =>
            {
                await ctx.StepAsync("implicit roles follow HTML-AAM", async () =>
                {
                    await ctx.Expect(ctx.GetByRole("banner")).ToContainTextAsync("Driver parity");
                    await ctx.Expect(ctx.GetByRole("CONTENTINFO")).ToHaveTextAsync("Rendered by every driver");
                    await ctx.Expect(ctx.Locate("section header")).ToHaveRoleAsync("generic");
                    await ctx.Expect(ctx.GetByRole("region", "Account")).ToHaveCountAsync(1);
                    await ctx.Expect(ctx.GetByRole("rowheader", "Plan")).ToHaveTextAsync("Plan");
                    await ctx.Expect(ctx.GetByRole("meter", "Storage used")).ToHaveCountAsync(1);
                    await ctx.Expect(ctx.GetByRole("search")).ToHaveCountAsync(1);
                    await ctx.Expect(ctx.Locate("svg")).ToHaveRoleAsync("graphics-document");
                });

                await ctx.StepAsync("accessible names follow accname", async () =>
                {
                    await ctx.Expect(ctx.GetByRole("heading", "Upgrade", exact: true)).ToHaveCountAsync(1);
                    await ctx.Expect(ctx.GetByRole("searchbox")).ToHaveAccessibleNameAsync("Search people");
                    await ctx.Expect(ctx.GetByRole("button", "Save")).ToHaveAccessibleNameAsync("Save");
                    await ctx.Expect(ctx.GetByRole("button", "Send", exact: true)).ToHaveCountAsync(1);
                    await ctx.Expect(ctx.GetByRole("textbox", "Nickname")).ToHaveCountAsync(1);
                    await ctx.Expect(ctx.GetByRole("checkbox", "Remember me")).ToHaveCountAsync(1);
                });

                await ctx.StepAsync("queries see the story, not the host page", async () =>
                {
                    await ctx.Expect(ctx.GetByText("An unhandled error has occurred")).ToHaveCountAsync(0);
                    await ctx.Expect(ctx.Locate("#blazor-error-ui")).ToHaveCountAsync(0);
                });
            }));

        stories.Add("Text", s => s
            .Arg(p => p.Section, DriverParitySection.Text)
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByTestId("blocks")).ToHaveTextAsync("Alpha Beta");
                await ctx.Expect(ctx.GetByText("Alpha Beta")).ToHaveAttributeAsync("data-testid", "blocks");
                await ctx.Expect(ctx.GetByTestId("lines")).ToHaveTextAsync("One Two");
                await ctx.Expect(ctx.GetByTestId("styled")).ToHaveTextAsync("Visible");
                await ctx.Expect(ctx.GetByRole("list", "Steps")).ToHaveTextAsync("First Second");
                await ctx.Expect(ctx.Locate("[data-testid]").Filter("One Two")).ToHaveCountAsync(1);
            }));

        stories.Add("Keyboard", s => s
            .Arg(p => p.Section, DriverParitySection.Keyboard)
            .Play(async ctx =>
            {
                var newsletter = ctx.GetByRole("checkbox", "Send me the newsletter");
                await ctx.StepAsync("Tab skips disabled fieldsets, inert content and extra summaries, not aria-disabled", async () =>
                {
                    await ctx.GetByRole("button", "Before").FocusAsync();
                    await ctx.ExpectTabOrderAsync(
                        ctx.GetByRole("button", "Archived"),
                        ctx.GetByRole("button", "Legend action"),
                        ctx.GetByText("More", exact: true),
                        newsletter,
                        ctx.GetByRole("button", "After"),
                        ctx.GetByLabel("Email"));
                });

                await ctx.StepAsync("Enter does not toggle a checkbox, Space does", async () =>
                {
                    await newsletter.PressAsync(Keys.Enter);
                    await ctx.ExpectAction("OnNewsletterChanged").NotToHaveBeenCalledAsync();
                    await ctx.Expect(newsletter).Not.ToBeCheckedAsync();
                    await newsletter.PressAsync(Keys.Space);
                    await ctx.Expect(newsletter).ToBeCheckedAsync();
                    await ctx.ExpectAction("OnNewsletterChanged").ToHaveBeenLastCalledWithAsync(true);
                });

                await ctx.StepAsync("Enter and Space activate an aria-disabled button", async () =>
                {
                    await ctx.GetByRole("button", "Archived").PressAsync(Keys.Enter);
                    await ctx.ExpectAction("OnArchivedClicked").ToHaveBeenCalledAsync(times: 1);
                    await ctx.PressAsync(Keys.Space);
                    await ctx.ExpectAction("OnArchivedClicked").ToHaveBeenCalledAsync(times: 2);
                });

                await ctx.StepAsync("Enter submits the form from a text field or a checkbox", async () =>
                {
                    await ctx.GetByLabel("Email").PressAsync(Keys.Enter);
                    await ctx.ExpectAction("OnSubscribed").ToHaveBeenCalledAsync(times: 1);
                    var terms = ctx.GetByRole("checkbox", "I accept the terms");
                    await terms.PressAsync(Keys.Enter);
                    await ctx.ExpectAction("OnSubscribed").ToHaveBeenCalledAsync(times: 2);
                    await ctx.Expect(terms).Not.ToBeCheckedAsync();
                });
            }));

        stories.Add("Check and uncheck", s => s
            .Arg(p => p.Section, DriverParitySection.Checked)
            .Description("CheckAsync clicks once, then waits for the state, which this switch updates after an await.")
            .Play(async ctx =>
            {
                var darkMode = ctx.GetByRole("switch", "Dark mode");
                await darkMode.CheckAsync();
                await ctx.Expect(darkMode).ToBeCheckedAsync();
                await ctx.ExpectAction("OnDarkModeChanged").ToHaveBeenCalledAsync(times: 1);
                await darkMode.UncheckAsync();
                await ctx.Expect(darkMode).Not.ToBeCheckedAsync();
                await ctx.ExpectAction("OnDarkModeChanged").ToHaveBeenCalledAsync(times: 2);

                var alerts = ctx.GetByRole("checkbox", "Email alerts");
                await alerts.CheckAsync();
                await ctx.Expect(alerts).ToBeCheckedAsync();
                await alerts.UncheckAsync();
                await ctx.Expect(alerts).Not.ToBeCheckedAsync();
            }));
    }
}
