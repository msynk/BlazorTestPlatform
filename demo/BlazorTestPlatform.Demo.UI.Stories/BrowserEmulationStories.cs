using BlazorTestPlatform;
using BlazorTestPlatform.Drivers;

namespace BlazorTestPlatform.Demo.UI.Stories;

/// <summary>
/// Browser emulation: geolocation, offline mode and network latency, emulated by the Playwright driver and read back
/// through real browser APIs. bUnit and the in-app driver lack <c>BrowserEmulation</c> and report these stories as
/// skipped. Test fixtures, not design-system components: no visual baselines, and no static SSR (the panel needs
/// interactivity).
/// </summary>
public sealed class BrowserEmulationStories : Stories<BrowserEmulationPanel>
{
    public override string Title => "Testing/Browser emulation";

    protected override void Define(StoryCollectionBuilder<BrowserEmulationPanel> stories)
    {
        stories.Defaults(s => s.NoVisual().Matrix(m => m.ExcludedHostingModels.Add(HostingModel.StaticServer)));

        stories.Add("Geolocation", s => s
            .Geolocation(52.37, 4.89)
            .BrowserBudget(b =>
            {
                b.MaxLayoutShift = 0.1;
                b.MaxDomNodes = 20;
            })
            .Play(async ctx =>
            {
                var position = ctx.GetByRole("status", "Position");
                await ctx.GetByRole("button", "Locate me").ClickAsync();
                await ctx.Expect(position).ToHaveTextAsync("52.37, 4.89");

                await ctx.SetGeolocationAsync(new GeoPosition(-33.87, 151.21));
                await ctx.GetByRole("button", "Locate me").ClickAsync();
                await ctx.Expect(position).ToHaveTextAsync("-33.87, 151.21");
            }));

        // WebAssembly only: under Interactive Server, going offline would also cut the circuit.
        stories.Add("Offline", s => s
            .Requires(DriverCapabilities.BrowserEmulation, "goes offline from play")
            .Matrix(m => m.HostingModels.Add(HostingModel.InteractiveWebAssembly))
            .Play(async ctx =>
            {
                var connection = ctx.GetByRole("status", "Connection");
                await ctx.Expect(connection).ToHaveTextAsync("Online");
                await ctx.SetOfflineAsync();
                await ctx.Expect(connection).ToHaveTextAsync("Offline");
                await ctx.SetOfflineAsync(false);
                await ctx.Expect(connection).ToHaveTextAsync("Online");
            }));

        stories.Add("Slow network", s => s
            .NetworkLatency(TimeSpan.FromMilliseconds(400))
            .Matrix(m => m.HostingModels.Add(HostingModel.InteractiveServer))
            .Play(async ctx =>
            {
                await ctx.GetByRole("button", "Measure a request").ClickAsync();
                await ctx.Expect(ctx.GetByRole("status", "Network")).ToHaveTextAsync("Slow");
            }));
    }
}
