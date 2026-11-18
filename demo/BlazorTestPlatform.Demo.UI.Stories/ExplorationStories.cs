using System.Text.RegularExpressions;
using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Stateful exploration: <c>ctx.ExploreAsync</c> takes seeded random steps and checks invariants after each. The stepper's
/// removal bug is found, and every runner shrinks the sequence (on fresh renders in bUnit, fresh pages in browsers) to the
/// two steps that cause it. The failure is declared with <c>ExpectFailure</c>. A test fixture: no visual baselines.
/// </summary>
public sealed class ExplorationStories : Stories<StepperPanel>
{
    public override string Title => "Testing/Exploration";

    protected override void Define(StoryCollectionBuilder<StepperPanel> stories) =>
        stories.Add("Stepper", s => s
            .Description("Explores Add, Remove and Double for 30 steps with the invariant \"count is never negative\"; the failure is reported shrunk to add -> remove.")
            .NoVisual()
            // Static SSR cannot run play, so nothing fails there.
            .ExpectFailure("the stepper's removal bug, which exploration finds and shrinks", run => run.HostingModel != HostingModel.StaticServer)
            .Play(ctx => ctx.ExploreAsync("stepper", e => e
                .Action("add", c => c.GetByRole("button", "Add").ClickAsync(), code: "await ctx.GetByRole(\"button\", \"Add\").ClickAsync();")
                .Action("remove", c => c.GetByRole("button", "Remove").ClickAsync(), when: c => c.GetByRole("button", "Remove").IsEnabledAsync(), code: "await ctx.GetByRole(\"button\", \"Remove\").ClickAsync();")
                .Action("double", c => c.GetByRole("button", "Double").ClickAsync(), code: "await ctx.GetByRole(\"button\", \"Double\").ClickAsync();")
                .Invariant("count is never negative", c => c.Expect(c.GetByTestId("count")).Not.ToHaveTextAsync(new Regex("^-"), TimeSpan.Zero))
                .Steps(30))));
}
