using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// The story clock: <c>SetSystemTimeAsync</c> moves the wall clock without firing timers (Playwright's
/// <c>clock.setSystemTime</c>), <c>AdvanceTimeToAsync</c> runs time to an instant, firing the timers due (<c>clock.pauseAt</c>).
/// Both work in every driver through the story's <see cref="StoryClock"/>. A test fixture: no visual baselines.
/// </summary>
public sealed class ClockStories : Stories<ClockPanel>
{
    public override string Title => "Testing/Clock";

    protected override void Define(StoryCollectionBuilder<ClockPanel> stories) =>
        stories.Add("Set system time and run to", s => s
            .Description("The wall clock jumps to the next morning without running the one-minute timer; running to 08:03 then fires it three times.")
            .NoVisual()
            .UseFakeTime(new DateTimeOffset(2026, 3, 1, 23, 59, 0, TimeSpan.Zero))
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByTestId("today")).ToHaveTextAsync("Today: 2026-03-01 23:59");

                await ctx.SetSystemTimeAsync(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));
                await ctx.GetByRole("button", "Refresh").ClickAsync();
                await ctx.Expect(ctx.GetByTestId("today")).ToHaveTextAsync("Today: 2026-03-02 08:00");
                await ctx.Expect(ctx.GetByTestId("ticks")).ToHaveTextAsync("Minutes waited: 0");

                await ctx.AdvanceTimeToAsync(new DateTimeOffset(2026, 3, 2, 8, 3, 0, TimeSpan.Zero));
                await ctx.Expect(ctx.GetByTestId("ticks")).ToHaveTextAsync("Minutes waited: 3");
                await ctx.Expect(ctx.GetByTestId("today")).ToHaveTextAsync("Today: 2026-03-02 08:03");
                if (await ctx.GetTimeAsync() != new DateTimeOffset(2026, 3, 2, 8, 3, 0, TimeSpan.Zero))
                {
                    throw new StoryAssertionException($"Expected the clock at 08:03, it reads {await ctx.GetTimeAsync():O}.");
                }
            }));
}
