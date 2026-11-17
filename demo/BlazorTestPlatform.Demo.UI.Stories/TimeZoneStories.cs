using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Time zones: <c>TimeZone(...)</c> gives the story a <see cref="TimeProvider"/> in that zone in every driver (here the fake
/// clock, which carries it), and runs browsers in that zone, which WebAssembly's <c>TimeZoneInfo.Local</c> follows. A test
/// fixture: no visual baselines.
/// </summary>
public sealed class TimeZoneStories : Stories<TimeZonePanel>
{
    public override string Title => "Testing/Time zone";

    protected override void Define(StoryCollectionBuilder<TimeZonePanel> stories) =>
        stories.Add("Tokyo", s => s
            .Description("TimeZone(\"Asia/Tokyo\") with a fake clock at 12:00 UTC: 21:00 local in every driver; under WebAssembly the runtime's own zone is Tokyo too, because the browser runs in it.")
            .NoVisual()
            .UseFakeTime(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero))
            .TimeZone("Asia/Tokyo")
            .Play(async ctx =>
            {
                await ctx.Expect(ctx.GetByTestId("provider-zone")).ToHaveTextAsync("Asia/Tokyo");
                await ctx.Expect(ctx.GetByTestId("local-time")).ToHaveTextAsync("2026-03-01 21:00");
                if (ctx.HostingModel == HostingModel.InteractiveWebAssembly)
                {
                    await ctx.Expect(ctx.GetByTestId("runtime-zone")).ToHaveTextAsync("Asia/Tokyo");
                }
            }));
}
