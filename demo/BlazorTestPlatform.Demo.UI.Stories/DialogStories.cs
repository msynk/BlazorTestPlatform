using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Focus management. The dialog moves focus with <c>ElementReference.FocusAsync()</c>; bUnit captures those calls and
/// moves its emulated focus, so the same play scripts check focus in memory, in real browsers and in the workbench.
/// The backdrop is fixed to the viewport, so snapshots capture the full page.
/// </summary>
public sealed class DialogStories : Stories<Dialog>
{
    public override string Title => "Overlays/Dialog";

    protected override void Define(StoryCollectionBuilder<Dialog> stories)
    {
        stories.Defaults(s => s
            .Arg(d => d.Title, "Publish changes?")
            .Arg(d => d.TriggerText, "Publish…")
            .Arg(d => d.ConfirmText, "Publish")
            .ChildContent("<p>Your edits go live for every visitor.</p>")
            .Bind(d => d.Open, false)
            .Visual(v => v.FullPage = true));

        stories.Add("Closed", s => s.Description("Open is bound (Bind), so the Controls panel can open the dialog and the dialog can close itself."));

        stories.Add("Open", s => s
            .Description("Opening moves focus to the least destructive action. The play script waits for it, so the snapshot always shows the focused state.")
            .Bind(d => d.Open, true)
            .Play(ctx => ctx.Expect(ctx.GetByRole("button", "Cancel")).ToBeFocusedAsync()));

        stories.Add("Destructive", s => s
            .Description("An accessibility tree snapshot pins what a screen reader gets: a named modal dialog with a heading and two buttons. The page behind is inert.")
            .Arg(d => d.Destructive, true)
            .Arg(d => d.Title, "Delete project?")
            .Arg(d => d.TriggerText, "Delete project…")
            .Arg(d => d.ConfirmText, "Delete")
            .ChildContent("<p>This permanently deletes <strong>Apollo</strong> and its 12 environments.</p>")
            .Bind(d => d.Open, true)
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByRole("button", "Cancel")).ToBeFocusedAsync();
                await ctx.Expect(ctx.GetByRole("dialog")).ToHaveAccessibleNameAsync("Delete project?");
                await ctx.ExpectAriaSnapshotAsync("""
                    - dialog "Delete project?":
                      - heading "Delete project?" [level=2]
                      - paragraph:
                        - strong
                      - button "Cancel"
                      - button "Delete"
                    """);
            }));

        stories.Add("Keyboard", s => s
            .Description("Keyboard only: Enter opens, Tab moves inside the dialog, Escape cancels and focus returns to the trigger.")
            .Play(async ctx =>
            {
                var trigger = ctx.GetByRole("button", "Publish…", exact: true);
                var dialog = ctx.GetByRole("dialog", "Publish changes?");

                await ctx.StepAsync("Enter on the trigger opens the dialog and focuses Cancel", async () =>
                {
                    await ctx.TabAsync();
                    await ctx.Expect(trigger).ToBeFocusedAsync();
                    await ctx.PressAsync(Keys.Enter);
                    await ctx.Expect(dialog).ToBeVisibleAsync();
                    await ctx.Expect(ctx.GetByRole("button", "Cancel")).ToBeFocusedAsync();
                });

                await ctx.StepAsync("Tab reaches the confirm button", () =>
                    ctx.ExpectTabOrderAsync(ctx.GetByRole("button", "Publish", exact: true)));

                await ctx.StepAsync("Escape cancels and focus returns to the trigger", async () =>
                {
                    await ctx.PressAsync(Keys.Escape);
                    await ctx.Expect(dialog).ToHaveCountAsync(0);
                    await ctx.Expect(trigger).ToBeFocusedAsync();
                });

                await ctx.ExpectAction("OnConfirm").NotToHaveBeenCalledAsync();
                await ctx.ExpectAction("OpenChanged").ToHaveBeenLastCalledWithAsync(false);
            }));

        stories.Add("Confirm", s => s
            .Description("Confirming raises OnConfirm once, closes the dialog and returns focus to the trigger.")
            .Play(async ctx =>
            {
                await ctx.GetByRole("button", "Publish…", exact: true).ClickAsync();
                await ctx.GetByRole("button", "Publish", exact: true).ClickAsync();

                await ctx.ExpectAction("OnConfirm").ToHaveBeenCalledAsync(times: 1);
                await ctx.Expect(ctx.GetByRole("dialog")).ToHaveCountAsync(0);
                await ctx.Expect(ctx.GetByRole("button", "Publish…", exact: true)).ToBeFocusedAsync();
            }));
    }
}
