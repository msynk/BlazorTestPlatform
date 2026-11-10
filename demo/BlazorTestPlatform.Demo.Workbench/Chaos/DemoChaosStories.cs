using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.Workbench.Chaos;

/// <summary>
/// Stories that fail on purpose in some hub runs (see <see cref="DemoChaos"/>), so the hub's stability, failure
/// grouping, accessibility baseline and performance trends have real results to work with. They live in the demo host,
/// not in the story assembly: the workbench and the demo's test suite never see them, and CI never runs them.
/// </summary>
public sealed class DemoChaosStories : Stories<Composition>
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    public override string Title => "Demo/Chaos";

    protected override void Define(StoryCollectionBuilder<Composition> stories)
    {
        stories.Defaults(s => s.Tags("chaos", "hub-only"));

        stories.Add("Save toast", s => s
            .Description("Intermittent: the confirmation is late every third run. Quarantined in the demo's quarantine.json, so it reports without failing the build.")
            .ChildContent(ChaosFragments.Toast)
            .Play(async ctx =>
            {
                ctx.DefaultTimeout = Short;
                await ctx.GetByRole("button", "Save").ClickAsync();
                await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Saved");
            }));

        stories.Add("Checkout total", s => s
            .Description("A regression in runs 3 and 4 (a rounding change), fixed since.")
            .ChildContent(ChaosFragments.Total)
            .Play(async ctx =>
            {
                ctx.DefaultTimeout = Short;
                await ctx.Expect(ctx.GetByTestId("total")).ToHaveTextAsync("$42.00");
            }));

        stories.Add("Report export", s => s
            .Description("Broken since run 8: the button was renamed and the play script still looks for the old name.")
            .ChildContent(ChaosFragments.Export)
            .Play(async ctx => await ctx.GetByRole("button", "Export CSV").ClickAsync(timeout: Short)));

        stories.Add("People search", s => s
            .Description("Had no label until run 10 added one: an accessibility fix.")
            .ChildContent(ChaosFragments.Search));

        stories.Add("Avatar menu", s => s
            .Description("The latest seeded run dropped the button's accessible name: a new accessibility failure.")
            .ChildContent(ChaosFragments.Avatar));

        stories.Add("Notification bell", s => s
            .Description("Hangs in some runs: a flaky timeout.")
            .Timeout(TimeSpan.FromSeconds(1))
            .ChildContent(ChaosFragments.Bell)
            .Play(async ctx =>
            {
                if (DemoChaos.BellHangs)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ctx.CancellationToken);
                }

                await ctx.Expect(ctx.GetByRole("button", "Notifications")).ToBeVisibleAsync();
            }));

        stories.Add("Activity feed", s => s
            .Description("Grows by 60 items a run, so it gets slower; skips a heading level (moderate accessibility debt that does not fail the gate).")
            .ChildContent(ChaosFragments.Feed)
            .Performance(p =>
            {
                p.TrackBaseline = false;
                p.Iterations = 2;
                p.WarmupIterations = 0;
            }));
    }
}
