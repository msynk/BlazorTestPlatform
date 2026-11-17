using BlazorTestPlatform;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>A WAI-ARIA widget tested by keyboard: roving tabindex, arrow keys, Home and End, and focus moved by the component.</summary>
public sealed class TabsStories : Stories<Tabs>
{
    private static readonly IReadOnlyList<Tabs.TabItem> Items =
    [
        new("Profile", Text("Your public profile and avatar.")),
        new("Security", Text("Passwords, passkeys and two-factor authentication.")),
        new("Billing", Text("Invoices and payment methods.")),
    ];

    public override string Title => "Navigation/Tabs";

    protected override void Define(StoryCollectionBuilder<Tabs> stories)
    {
        stories.Defaults(s => s
            .Arg(t => t.Label, "Account settings")
            .Arg(t => t.Items, Items)
            .Bind(t => t.SelectedIndex, 0));

        stories.Add("Default", s => s.Description("Items is a list of records with RenderFragment content: any parameter type works as an argument."));
        stories.Add("Second tab selected", s => s
            .Description("SelectedIndex is bound, so clicking a tab in the frame updates the story's value.")
            .Bind(t => t.SelectedIndex, 1));

        stories.Add("Keyboard navigation", s => s
            .Description("WAI-ARIA tabs: one tab stop, arrows move between tabs, Home/End jump, Tab enters the panel.")
            .Play(async ctx =>
            {
                var profile = ctx.GetByRole("tab", "Profile");
                var security = ctx.GetByRole("tab", "Security");
                var billing = ctx.GetByRole("tab", "Billing");

                await ctx.StepAsync("single tab stop on the selected tab", async () =>
                {
                    await ctx.TabAsync();
                    await ctx.Expect(profile).ToBeFocusedAsync();
                });

                await ctx.StepAsync("arrow keys move and select", async () =>
                {
                    await ctx.PressAsync(Keys.ArrowRight);
                    await ctx.Expect(security).ToBeFocusedAsync();
                    await ctx.Expect(security).ToHaveAttributeAsync("aria-selected", "true");
                    await ctx.Expect(ctx.GetByRole("tabpanel")).ToContainTextAsync("two-factor");
                    await ctx.PressAsync(Keys.ArrowLeft);
                    await ctx.Expect(profile).ToBeFocusedAsync();
                });

                await ctx.StepAsync("Home and End jump", async () =>
                {
                    await ctx.PressAsync(Keys.End);
                    await ctx.Expect(billing).ToBeFocusedAsync();
                    await ctx.PressAsync(Keys.Home);
                    await ctx.Expect(profile).ToBeFocusedAsync();
                });

                await ctx.StepAsync("Tab moves into the active panel", async () =>
                {
                    await ctx.TabAsync();
                    await ctx.Expect(ctx.GetByRole("tabpanel", "Profile")).ToBeFocusedAsync();
                });

                await ctx.ExpectAction("SelectedIndexChanged").ToHaveBeenLastCalledWithAsync(0);
            }));

        stories.Add("States in play", s => s
            .Description("Named screenshots of the states play passes through (ExpectScreenshotAsync), computed styles (ToHaveCssAsync), the viewport (ToBeInViewportAsync), an accessibility scan in the middle of play (ExpectAccessibleAsync) and a file attached to the run (Attach). bUnit runs the scan and records the browser-only checks as skipped; the in-app driver checks styles and the viewport.")
            .Play(async ctx =>
            {
                await ctx.ExpectScreenshotAsync("profile");

                await ctx.GetByRole("tab", "Billing").ClickAsync();
                var panel = ctx.GetByRole("tabpanel", "Billing");
                await ctx.Expect(panel).ToBeVisibleAsync();
                await ctx.Expect(panel).ToHaveCssAsync("paddingTop", "16px");
                await ctx.Expect(panel).ToBeInViewportAsync(ratio: 1);
                await ctx.ExpectAccessibleAsync("billing selected");
                await ctx.ExpectScreenshotAsync("billing", ctx.GetByRole("tablist"));

                ctx.Attach("selected-tab.txt", "Billing");
            }));
    }

    private static RenderFragment Text(string text) => builder => builder.AddContent(0, text);
}
