using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class TimeZoneStories : Stories<LocalClock>
{
    public override string Title => "Tests/Time zones";

    protected override void Define(StoryCollectionBuilder<LocalClock> stories)
    {
        // Winter: Berlin is UTC+1. The fake clock carries the zone.
        stories.Add("Berlin at noon UTC", s => s.UseFakeTime(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)).TimeZone("Europe/Berlin").Play(async ctx =>
            await ctx.Expect(ctx.GetByTestId("local")).ToHaveTextAsync("13:00")));

        stories.Add("Tokyo on the system clock", s => s.TimeZone("Asia/Tokyo").Play(async ctx =>
            await ctx.Expect(ctx.GetByTestId("zone")).ToHaveTextAsync("Asia/Tokyo")));
    }
}

public sealed class TimeZoneTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new TimeZoneStories()]);

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Stories_render_local_times_in_their_time_zone()
    {
        foreach (var id in Catalog.Ids)
        {
            var result = await BunitStoryRunner.RunAsync(Catalog.Get(id), Options, TestContext.Current.CancellationToken);
            Assert.True(result.Outcome == StoryOutcome.Passed, $"{id}: {result.Error}");
        }

        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-time-zones--berlin-at-noon-utc"), Options, TestContext.Current.CancellationToken);
        Assert.Contains(">13:00<", session.Markup, StringComparison.Ordinal);
        Assert.Equal("Europe/Berlin", session.Story.TimeZone);
    }

    [Theory]
    [InlineData("Europe/Berlin", "Europe/Berlin")]
    [InlineData("W. Europe Standard Time", "Europe/Berlin")]
    [InlineData("utc", "UTC")]
    [InlineData(" America/New_York ", "America/New_York")]
    public void Time_zones_are_stored_as_iana_ids(string id, string expected) => Assert.Equal(expected, StoryTimeZone.Normalize(id));

    [Fact]
    public void Unknown_time_zones_are_rejected_when_the_story_is_built() =>
        Assert.Throws<ArgumentException>(() => StoryTimeZone.Normalize("Mars/Olympus_Mons"));
}
