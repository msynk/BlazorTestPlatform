using BlazorTestPlatform;

namespace Contoso.UI.Stories;

public sealed class ButtonStories : Stories<Button>
{
    public override string Title => "Inputs/Button";

    protected override void Define(StoryCollectionBuilder<Button> stories)
    {
        stories.Defaults(s => s.ChildContent("Save changes").Layout(StoryLayout.Centered));

        stories.Add("Primary", s => s.Description("The default call to action. Use one per view."));
        stories.Add("Secondary", s => s.Arg(b => b.Variant, ButtonVariant.Secondary));
        stories.Add("Danger", s => s.Arg(b => b.Variant, ButtonVariant.Danger).ChildContent("Delete project"));
        stories.Add("Disabled", s => s.Arg(b => b.Disabled, true));
        stories.Add("Loading", s => s.Arg(b => b.Loading, true).ChildContent("Saving…"));

        // A visual state matrix: every variant at every size.
        stories.AddVariants(v => v
            .Vary(b => b.Variant, ButtonVariant.Primary, ButtonVariant.Secondary, ButtonVariant.Danger)
            .Vary(b => b.Size, ButtonSize.Small, ButtonSize.Large),
            s => s.Tags("visual-matrix"));

        stories.Add("Click raises OnClick", s => s
            .Performance(p => p.MaxRootRenders = 2)
            .Play(async ctx =>
            {
                await ctx.GetByRole("button", "Save changes").ClickAsync();
                await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 1);
            }));

        stories.Add("Keyboard activation", s => s.Play(async ctx =>
        {
            var button = ctx.GetByRole("button", "Save changes");
            await ctx.StepAsync("Tab moves focus to the button", async () =>
            {
                await ctx.TabAsync();
                await ctx.Expect(button).ToBeFocusedAsync();
            });

            await ctx.StepAsync("Enter and Space activate it", async () =>
            {
                await ctx.PressAsync(Keys.Enter);
                await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 1);
                await ctx.PressAsync(Keys.Space);
                await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 2);
            });
        }));

        stories.Add("Disabled ignores clicks", s => s
            .Arg(b => b.Disabled, true)
            .Play(async ctx =>
            {
                var button = ctx.GetByRole("button", "Save changes");
                await ctx.Expect(button).ToBeDisabledAsync();
                await button.ClickAsync(force: true);
                await ctx.ExpectAction("OnClick").NotToHaveBeenCalledAsync();
            }));
    }
}
