using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Core.Tests;

public sealed class TagOptionsTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new TaggedStories()]);

    private static BtpSettings Settings(string json)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-tag-options", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(directory, "btp.json"), json);
        return BtpSettings.Load(directory, new Dictionary<string, string?>());
    }

    private const string Options = """
        {
          "tagOptions": {
            "network": { "retries": 3, "timeoutMilliseconds": 90000 },
            "Flaky": { "retries": 5 },
            "cold": { "slow": true },
            "parked": { "skip": "tracked in #123" }
          }
        }
        """;

    [Fact]
    public void Tags_set_timeouts_slowness_and_retries()
    {
        var settings = Settings(Options);

        Assert.Equal(TimeSpan.FromSeconds(90), settings.TimeoutFor(Catalog.Get("tests-tagged--network"), TimeSpan.FromSeconds(60)));
        Assert.Equal(3, settings.RetriesFor(Catalog.Get("tests-tagged--network")));

        // Several tags: the longest timeout and the most retries; tag names are case insensitive; a slow tag triples.
        var both = Catalog.Get("tests-tagged--network-and-flaky-and-cold");
        Assert.Equal(5, settings.RetriesFor(both));
        Assert.Equal(TimeSpan.FromSeconds(270), settings.TimeoutFor(both, TimeSpan.FromSeconds(60)));

        // Without tag options, the runner's default and the run's retries apply.
        var plain = Catalog.Get("tests-tagged--plain");
        Assert.Equal(TimeSpan.FromSeconds(60), settings.TimeoutFor(plain, TimeSpan.FromSeconds(60)));
        Assert.Null(settings.RetriesFor(plain));
        Assert.Null(settings.TagSkipReason(plain));
    }

    [Fact]
    public void A_story_timeout_wins_over_its_tags()
    {
        var settings = Settings(Options);

        Assert.Equal(TimeSpan.FromSeconds(6), settings.TimeoutFor(Catalog.Get("tests-tagged--own-timeout"), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void A_tag_can_skip_its_stories()
    {
        var settings = Settings(Options);

        Assert.Equal("Skipped by tag 'parked': tracked in #123", settings.TagSkipReason(Catalog.Get("tests-tagged--parked")));
    }

    [Theory]
    [InlineData("""{ "tagOptions": { "network": { "retries": -1 } } }""", "tagOptions.network.retries")]
    [InlineData("""{ "tagOptions": { "network": { "timeoutMilliseconds": 0 } } }""", "tagOptions.network.timeoutMilliseconds")]
    public void Invalid_tag_options_are_reported(string json, string name)
    {
        var error = Assert.ThrowsAny<Exception>(() => Settings(json));

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    private sealed class TaggedStories : Stories<Greeter>
    {
        public override string Title => "Tests/Tagged";

        protected override void Define(StoryCollectionBuilder<Greeter> stories)
        {
            stories.Add("Plain", s => s.Tags("smoke"));
            stories.Add("Network", s => s.Tags("network"));
            stories.Add("Network and flaky and cold", s => s.Tags("network", "flaky", "cold"));
            stories.Add("Own timeout", s => s.Tags("network", "cold").Timeout(TimeSpan.FromSeconds(2)));
            stories.Add("Parked", s => s.Tags("parked", "smoke"));
        }
    }
}
