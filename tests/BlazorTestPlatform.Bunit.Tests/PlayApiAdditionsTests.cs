using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class PlayApiAdditionsStories : Stories<RevealsImage>
{
    public override string Title => "Tests/Play API additions";

    protected override void Define(StoryCollectionBuilder<RevealsImage> stories)
    {
        stories.Add("Accessible only while closed", s => s.Tags("fails-on-purpose").Play(async ctx =>
        {
            await ctx.ExpectAccessibleAsync("closed");
            await ctx.GetByRole("button", "Show picture").ClickAsync();
            await ctx.ExpectAccessibleAsync("picture shown");
        }));

        stories.Add("Browser-only checks", s => s.Play(async ctx =>
        {
            await ctx.Expect(ctx.GetByRole("button")).ToHaveCssAsync("backgroundColor", "rgb(0, 0, 0)");
            await ctx.Expect(ctx.GetByRole("button")).ToBeInViewportAsync(ratio: 0.5);
            await ctx.ExpectScreenshotAsync("closed");
            ctx.Log("checks done");
        }));

        stories.Add("Attachments", s => s.Play(ctx =>
        {
            ctx.Attach("state.json", """{ "open": false }""");
            ctx.Attach("raw.bin", [1, 2, 3]);
            return Task.CompletedTask;
        }));

        stories.Add("Slow step", s => s.Tags("fails-on-purpose").Play(ctx => ctx.StepAsync("load the picture", () => Task.Delay(TimeSpan.FromSeconds(5), ctx.CancellationToken), TimeSpan.FromMilliseconds(50))));

        stories.Add("Duplicate screenshot names", s => s.Tags("fails-on-purpose").Play(async ctx =>
        {
            await ctx.ExpectScreenshotAsync("open");
            await ctx.ExpectScreenshotAsync("Open");
        }));

        stories.Add("Reserved screenshot name", s => s.Tags("fails-on-purpose").Play(ctx => ctx.ExpectScreenshotAsync("initial")));

        stories.Add("Outdated accessibility snapshot", s => s.Tags("fails-on-purpose").Play(async ctx =>
        {
            await ctx.ExpectAriaSnapshotAsync("""
                - button "Show image"
                """, timeout: TimeSpan.FromMilliseconds(50));
        }));
    }
}

public sealed class PlayApiAdditionsTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new PlayApiAdditionsStories()]);

    private static string ResultsDirectory() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-bunit-additions", Guid.NewGuid().ToString("N"))).FullName;

    private static BunitStoryOptions Options(string? results = null, bool write = false) => new()
    {
        Settings = BtpSettings.Load(results ?? Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?> { ["BTP_RESULTS_DIR"] = results is null ? null : Path.Combine(results, "out") }),
        WriteResults = write,
    };

    [Fact]
    public async Task Accessibility_can_be_checked_in_the_middle_of_play()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-play-api-additions--accessible-only-while-closed"), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("image-alt", result.Error, StringComparison.Ordinal);
        Assert.Contains("(picture shown)", result.Error, StringComparison.Ordinal);
        var steps = result.Steps.Single(s => s.Title == "play").Children;
        Assert.False(steps.Single(s => s.Title == "expect accessible (closed)").Failed);
        Assert.True(steps.Single(s => s.Title == "expect accessible (picture shown)").Failed);
    }

    [Fact]
    public async Task Checks_bunit_cannot_make_are_recorded_as_skipped_and_play_goes_on()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-play-api-additions--browser-only-checks"), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        var titles = Flatten(result.Steps).Select(s => s.Title).ToArray();
        Assert.Contains(titles, t => t.Contains("to have CSS background-color \"rgb(0, 0, 0)\": skipped, the bunit driver has no Css", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("to be in the viewport (at least 50%): skipped, the bunit driver has no Layout", StringComparison.Ordinal));
        Assert.Contains("screenshot skipped: the bunit driver cannot take screenshots", titles);
        Assert.Contains("checks done", titles);
        Assert.Empty(result.Screenshots);
    }

    [Fact]
    public async Task Play_attachments_are_written_with_the_results_and_attached_to_test_results()
    {
        var directory = ResultsDirectory();
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-play-api-additions--attachments"), Options(directory, write: true), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Passed, result.Outcome);
        var files = result.Artifacts.Where(a => a.Kind == PlayAttachment.ArtifactKind).ToArray();
        Assert.Equal(["state.json", "raw.bin"], files.Select(f => f.Description));
        Assert.Equal("""{ "open": false }""", File.ReadAllText(files[0].Path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(files[1].Path));

        // A clean pass attaches nothing else, but the script's own files always go to the test framework.
        var attachments = StoryAttachments.For(result);
        Assert.Equal(["state.json", "raw.bin"], attachments.Select(a => a.Name));
        Assert.Equal("application/json", attachments[0].MediaType);
    }

    [Fact]
    public async Task A_step_that_overruns_its_timeout_fails_at_that_step()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-play-api-additions--slow-step"), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Equal("Step 'load the picture' did not finish within 50 ms.", result.Error);
    }

    [Theory]
    [InlineData("tests-play-api-additions--duplicate-screenshot-names", "already took a screenshot named 'Open'")]
    [InlineData("tests-play-api-additions--reserved-screenshot-name", "'initial' cannot name a screenshot")]
    public async Task Screenshot_names_must_be_unique_and_not_reserved(string id, string message)
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get(id), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outdated_accessibility_snapshots_are_rewritten_in_the_story_source()
    {
        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-play-api-additions--outdated-accessibility-snapshot"), Options(), TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        var mismatch = Assert.Single(result.AriaSnapshotMismatches);
        Assert.EndsWith("PlayApiAdditionsTests.cs", mismatch.SourceFile, StringComparison.Ordinal);
        Assert.Equal("- button \"Show image\"", mismatch.Expected);
        Assert.Equal("- button \"Show picture\"", mismatch.Actual);

        // Applied to a copy: the test must not edit its own source.
        var copy = Path.Combine(ResultsDirectory(), "PlayApiAdditionsTests.cs");
        File.Copy(mismatch.SourceFile, copy);
        var update = Assert.Single(SourceSnapshots.Update([mismatch with { SourceFile = copy }], Path.GetDirectoryName(copy)!));

        Assert.True(update.Updated, update.Message);
        var text = File.ReadAllText(copy);
        Assert.Contains("- button \"Show picture\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("- button \"Show image\"", text, StringComparison.Ordinal);
        Assert.Contains("\"\"\", timeout: TimeSpan.FromMilliseconds(50));", text, StringComparison.Ordinal);
    }

    private static IEnumerable<StepRecord> Flatten(IEnumerable<StepRecord> steps) => steps.SelectMany(s => Flatten(s.Children).Prepend(s));
}
