using BlazorTestPlatform;
using Microsoft.AspNetCore.Components;

namespace Contoso.UI.Stories;

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

        stories.Add("Default");
        stories.Add("Second tab selected", s => s.Bind(t => t.SelectedIndex, 1));

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
    }

    private static RenderFragment Text(string text) => builder => builder.AddContent(0, text);
}
