using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// The basics: one story per state, a variant matrix, and play scripts that click and use the keyboard. Every story
/// here is also a bUnit test, a browser test in every render mode, a visual snapshot and an accessibility scan.
/// </summary>
public sealed class ButtonStories : Stories<Button>
{
    public override string Title => "Inputs/Button";

    protected override void Define(StoryCollectionBuilder<Button> stories)
    {
        stories.Defaults(s => s.ChildContent("Save changes").Layout(StoryLayout.Centered));

        stories.Add("Primary", s => s.Description("The default call to action. Use one per view."));
        stories.Add("Secondary", s => s
            .Description("A state is a story: one Arg(...) call. Try the Controls panel to change any parameter live; the URL keeps your edits.")
            .Arg(b => b.Variant, ButtonVariant.Secondary));
        stories.Add("Danger", s => s
            .Description("ChildContent(...) supplies the label as markup.")
            .Arg(b => b.Variant, ButtonVariant.Danger)
            .ChildContent("Delete project"));
        stories.Add("Disabled", s => s.Arg(b => b.Disabled, true));
        stories.Add("Loading", s => s
            .Description("Busy buttons are disabled and set aria-busy; the spinner is hidden from assistive technology.")
            .Arg(b => b.Loading, true)
            .ChildContent("Saving…"));

        // A visual state matrix: every variant at every size.
        stories.AddVariants(
            v => v
                .Vary(b => b.Variant, ButtonVariant.Primary, ButtonVariant.Secondary, ButtonVariant.Danger)
                .Vary(b => b.Size, ButtonSize.Small, ButtonSize.Large),
            s => s
                .Tags("visual-matrix")
                .Description("AddVariants(...) generates one story per combination: every variant at every size, each with its own visual snapshot."));

        stories.Add("Click raises OnClick", s => s
            .Description("Unset EventCallback parameters are logged as actions automatically; the play script asserts them in every driver. A render budget (MaxRootRenders) fails the story if a click renders twice, and the committed baseline catches drift.")
            .Performance(p => p.MaxRootRenders = 2)
            .Play(async ctx =>
            {
                await ctx.GetByRole("button", "Save changes").ClickAsync();
                await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 1);
            }));

        stories.Add("Keyboard activation", s => s
            .Description("StepAsync groups actions on the timeline that every run records. The keyboard behaves like a browser's, in bUnit too.")
            .Play(async ctx =>
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
            .Description("A forced click on a disabled button reaches no handler, in bUnit as in a browser.")
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
