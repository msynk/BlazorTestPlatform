using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Drivers;
using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class EmulationStories : Stories<Counter>
{
    public override string Title => "Tests/Emulation";

    protected override void Define(StoryCollectionBuilder<Counter> stories)
    {
        stories.Add("Plain");
        stories.Add("Located", s => s
            .Geolocation(52.37, 4.89, accuracy: 10)
            .GrantPermissions("notifications", "notifications", " ")
            .Play(_ => throw new InvalidOperationException("A driver without browser emulation must not run this play function.")));
        stories.Add("Slow", s => s.NetworkLatency(TimeSpan.FromMilliseconds(300)));
        stories.Add("Goes offline", s => s.Play(ctx => ctx.SetOfflineAsync()));
        stories.Add("Moves", s => s.Play(ctx => ctx.SetGeolocationAsync(new GeoPosition(48.85, 2.35))));
    }
}

public sealed class BrowserEmulationTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<BrowserEmulationTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public void Emulating_stories_require_browser_emulation_and_others_do_not()
    {
        var located = Catalog.Get("tests-emulation--located");

        Assert.Equal(new GeoPosition(52.37, 4.89, 10), located.Emulation.Geolocation);
        Assert.Equal(["notifications"], located.Emulation.Permissions);
        Assert.Equal(DriverCapabilities.BrowserEmulation, located.RequiredCapabilities);
        Assert.Equal(TimeSpan.FromMilliseconds(300), Catalog.Get("tests-emulation--slow").Emulation.Latency);
        Assert.True(Catalog.Get("tests-emulation--plain").Emulation.IsEmpty);
        Assert.Equal(DriverCapabilities.None, Catalog.Get("tests-emulation--plain").RequiredCapabilities);
    }

    [Fact]
    public void Real_browsers_can_run_emulating_stories_but_in_memory_drivers_cannot()
    {
        var located = Catalog.Get("tests-emulation--located");

        Assert.True(located.CanRunOn(DriverCapabilities.RealBrowser | DriverCapabilities.BrowserEmulation));
        Assert.False(located.CanRunOn(BunitStoryDriver.CapabilitiesFor(located)));
    }

    [Theory]
    [InlineData("tests-emulation--located", "BrowserEmulation (emulates browser APIs or network conditions)")]
    [InlineData("tests-emulation--goes-offline", "emulating offline mode")]
    [InlineData("tests-emulation--moves", "emulating geolocation")]
    public async Task bUnit_skips_emulation_with_the_reason(string id, string reason)
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get(id), Options);

        Assert.Equal(StoryOutcome.Skipped, result.Outcome);
        Assert.Contains(reason, result.SkipReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(91, 0, 0)]
    [InlineData(-91, 0, 0)]
    [InlineData(0, 181, 0)]
    [InlineData(0, double.NaN, 0)]
    [InlineData(0, 0, -1)]
    public void Invalid_positions_are_rejected_when_the_story_is_defined(double latitude, double longitude, double accuracy)
    {
        var collection = new InlineStories(s => s.Geolocation(latitude, longitude, accuracy));

        Assert.ThrowsAny<ArgumentException>(() => StoryCatalog.FromCollections([collection]));
    }

    [Fact]
    public void Negative_latency_is_rejected()
    {
        var collection = new InlineStories(s => s.NetworkLatency(TimeSpan.FromMilliseconds(-1)));

        Assert.ThrowsAny<ArgumentException>(() => StoryCatalog.FromCollections([collection]));
    }

    private sealed class InlineStories(Func<StoryBuilder<Counter>, StoryBuilder<Counter>> configure) : Stories<Counter>
    {
        public override string Title => "Tests/Inline emulation";

        protected override void Define(StoryCollectionBuilder<Counter> stories) => stories.Add("Story", s => configure(s));
    }
}
