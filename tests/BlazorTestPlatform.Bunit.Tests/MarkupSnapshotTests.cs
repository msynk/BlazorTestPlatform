using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;
using BlazorTestPlatform.Results;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class OrderCardStories : Stories<OrderCard>
{
    public override string Title => "Tests/Markup snapshots";

    protected override void Define(StoryCollectionBuilder<OrderCard> stories)
    {
        stories.Add("Card", s => s
            .ScrubMarkup(@"\d{4}-\d{2}-\d{2}", "{date}")
            .Play(async ctx =>
            {
                await ctx.ExpectMarkupSnapshotAsync();
                await ctx.GetByRole("button", "Add").ClickAsync();
                await ctx.ExpectMarkupSnapshotAsync("after-add", ctx.GetByText("Count:"));
            }));
        stories.Add("Fails after the snapshot", s => s.Tags("fails-on-purpose").Play(async ctx =>
        {
            await ctx.ExpectMarkupSnapshotAsync("written");
            await ctx.Expect(ctx.GetByRole("heading")).ToHaveTextAsync("Invoices", TimeSpan.Zero);
        }));
    }
}

public sealed class MarkupSnapshotTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new OrderCardStories()]);

    private static (BunitStoryOptions Options, string Snapshots) Options(string? directory = null, SnapshotUpdateMode? mode = null)
    {
        directory ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-markup", Guid.NewGuid().ToString("N"))).FullName;
        var environment = new Dictionary<string, string?> { ["BTP_CI"] = "false" };
        if (mode is { } update)
        {
            environment["BTP_UPDATE_SNAPSHOTS"] = update.ToString().ToLowerInvariant();
        }

        var settings = BtpSettings.Load(directory, environment);
        return (new BunitStoryOptions { Settings = settings, WriteResults = false }, settings.SnapshotDirectoryPath);
    }

    [Fact]
    public async Task The_first_run_writes_normalized_scrubbed_markup_and_the_next_matches()
    {
        var (options, snapshots) = Options();

        var first = await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--card"), options, TestContext.Current.CancellationToken);
        var second = await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--card"), options, TestContext.Current.CancellationToken);

        Assert.True(first.Outcome == StoryOutcome.Passed, first.Error);
        Assert.Equal(StoryOutcome.Passed, second.Outcome);
        var story = File.ReadAllText(Path.Combine(snapshots, "tests-markup-snapshots--card", "story.markup.html"));
        Assert.Equal(
            """
            <section class="card primary" id="{guid}">
              <h2>Orders</h2>
              <button>Add</button>
              <p>Count: 0</p>
              <span data-btp-ignore>…</span>
              <input aria-label="Paid" checked disabled type="checkbox">
              <p>Placed on {date}</p>
            </section>

            """.ReplaceLineEndings("\n"),
            story);
        Assert.Equal("<p>Count: 1</p>\n", File.ReadAllText(Path.Combine(snapshots, "tests-markup-snapshots--card", "after-add.markup.html")));
    }

    [Fact]
    public async Task A_change_fails_with_the_changed_lines_and_can_be_accepted()
    {
        var (options, snapshots) = Options();
        var directory = Path.GetDirectoryName(snapshots)!;
        options.Args["Title"] = "Invoices";
        var (ci, _) = Options(directory, SnapshotUpdateMode.None);
        ci.Args["Title"] = "Invoices";

        // Arg overrides are a variant: their own file, written on the first local run.
        var variant = await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--card"), options, TestContext.Current.CancellationToken);
        Assert.Equal(StoryOutcome.Passed, variant.Outcome);
        var variantFile = Directory.GetFiles(Path.Combine(snapshots, "tests-markup-snapshots--card"), "story.args-*.markup.html").Single();

        // Edit the committed variant to differ, then run as CI: a mismatch, with a diff and a received file.
        File.WriteAllText(variantFile, File.ReadAllText(variantFile).Replace("Invoices", "Receipts", StringComparison.Ordinal));
        var changed = await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--card"), ci, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, changed.Outcome);
        Assert.Contains("Markup snapshot 'story' changed", changed.Error, StringComparison.Ordinal);
        Assert.Contains("-   <h2>Receipts</h2>", changed.Error, StringComparison.Ordinal);
        Assert.Contains("+   <h2>Invoices</h2>", changed.Error, StringComparison.Ordinal);
        Assert.Contains("btp snapshots accept", changed.Error, StringComparison.Ordinal);
        var received = variantFile.Replace(MarkupSnapshots.Suffix, MarkupSnapshots.ReceivedSuffix, StringComparison.Ordinal);
        Assert.True(File.Exists(received));

        var accepted = MarkupSnapshots.Accept(snapshots);

        Assert.Equal([variantFile], accepted);
        Assert.False(File.Exists(received));
        Assert.Equal(StoryOutcome.Passed, (await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--card"), ci, TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task A_missing_snapshot_fails_on_CI()
    {
        var (ci, snapshots) = Options(mode: SnapshotUpdateMode.None);

        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--card"), ci, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.Contains("No markup snapshot 'story'", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(snapshots, "tests-markup-snapshots--card", "story.markup.html")));
        Assert.True(File.Exists(Path.Combine(snapshots, "tests-markup-snapshots--card", "story" + MarkupSnapshots.ReceivedSuffix)));
    }

    [Fact]
    public async Task A_snapshot_written_by_a_failed_run_is_not_kept()
    {
        var (options, snapshots) = Options();

        var result = await BunitStoryRunner.RunAsync(Catalog.Get("tests-markup-snapshots--fails-after-the-snapshot"), options, TestContext.Current.CancellationToken);

        Assert.Equal(StoryOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(Path.Combine(snapshots, "tests-markup-snapshots--fails-after-the-snapshot", "written.markup.html")));
        Assert.Contains(result.Steps, s => s.Title == "markup snapshot 'written.markup.html' was not kept because the run failed");
    }
}
