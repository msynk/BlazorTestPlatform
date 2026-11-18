using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class ModeTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ModeTests>();

    private static BunitStoryOptions Options(ThemeDefinition theme) => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
        Theme = theme,
    };

    [Fact]
    public void Modes_are_themes_named_after_what_they_change_and_look_like_their_base()
    {
        var german = ThemeDefinition.Dark.WithCulture("de-de");
        Assert.Equal("dark-de-de", german.Name);
        Assert.Equal("de-DE", german.Culture);
        Assert.Equal("dark", german.FrameTheme);
        Assert.Equal(ColorScheme.Dark, german.ColorScheme);

        var tokyo = german.WithTimeZone("Asia/Tokyo");
        Assert.Equal("dark-de-de-asia-tokyo", tokyo.Name);
        Assert.Equal(("de-DE", "Asia/Tokyo", "dark"), (tokyo.Culture, tokyo.TimeZone, tokyo.FrameTheme));
        Assert.Equal("light-fr", ThemeDefinition.Light.WithCulture("fr-FR", "light-fr").Name);
        Assert.Throws<System.Globalization.CultureNotFoundException>(() => ThemeDefinition.Light.WithCulture("xx-NOPE"));
        Assert.Throws<ArgumentException>(() => ThemeDefinition.Light.WithTimeZone("Mars/Base"));
    }

    [Fact]
    public void Matrix_cells_with_modes_round_trip_through_their_keys()
    {
        var german = ThemeDefinition.Light.WithCulture("de-DE");
        var matrix = new TestMatrix().WithThemes(ThemeDefinition.Light, german).WithHostingModels(HostingModel.InteractiveServer);
        var cells = matrix.Expand(Catalog.Where(s => s.Title == "Tests/Formatted")).ToArray();

        Assert.Contains(cells, c => c.Theme == german);
        foreach (var cell in cells)
        {
            Assert.Equal(cell, matrix.Resolve(Catalog, cell.Key));
        }
    }

    [Fact]
    public async Task A_culture_mode_formats_every_story_that_does_not_pin_its_own()
    {
        var mode = ThemeDefinition.Light.WithCulture("de-DE");

        await using (var invariant = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-formatted--invariant"), Options(mode), TestContext.Current.CancellationToken))
        {
            Assert.Contains(">1.234,5<", invariant.Markup, StringComparison.Ordinal);
        }

        // The story's own culture wins over the mode's.
        var french = ThemeDefinition.Light.WithCulture("fr-FR");
        var german = await BunitStoryRunner.RunAsync(Catalog.Get("tests-formatted--german"), Options(french), TestContext.Current.CancellationToken);
        Assert.True(german.Outcome == StoryOutcome.Passed, german.Error);
        Assert.Equal("light-fr-fr", german.Theme);
    }

    [Fact]
    public async Task A_time_zone_mode_reaches_the_story_time_provider()
    {
        var mode = ThemeDefinition.Light.WithTimeZone("America/New_York");
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-time-zones--tokyo-on-the-system-clock"), Options(mode), TestContext.Current.CancellationToken);

        // The story pins Tokyo: it wins.
        Assert.Contains(">Asia/Tokyo<", session.Markup, StringComparison.Ordinal);

        await using var newYork = await BunitStoryRunner.RenderAsync(StoryCatalog.FromCollections([new UnzonedClockStories()]).Stories.Single(), Options(mode), TestContext.Current.CancellationToken);
        Assert.Contains(">America/New_York<", newYork.Markup, StringComparison.Ordinal);
        Assert.Contains(">07:00<", newYork.Markup, StringComparison.Ordinal);
    }

    private sealed class UnzonedClockStories : Stories<Components.LocalClock>
    {
        public override string Title => "Tests/Unzoned clock";

        protected override void Define(StoryCollectionBuilder<Components.LocalClock> stories) =>
            stories.Add("Noon UTC", s => s.UseFakeTime(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)));
    }
}
