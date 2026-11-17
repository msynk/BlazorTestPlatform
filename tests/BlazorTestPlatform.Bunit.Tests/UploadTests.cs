using BlazorTestPlatform.Bunit.Tests.Components;
using BlazorTestPlatform.Configuration;

namespace BlazorTestPlatform.Bunit.Tests;

public sealed class UploaderStories : Stories<Uploader>
{
    public override string Title => "Tests/Uploader";

    protected override void Define(StoryCollectionBuilder<Uploader> stories) =>
        stories.Add("Default", s => s.Accessibility(a => a.Skip = true));
}

public sealed class UploadTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<UploadTests>();

    private static BunitStoryOptions Options => new()
    {
        Settings = BtpSettings.Load(Path.Combine(Path.GetTempPath(), "btp-bunit-tests"), new Dictionary<string, string?>()),
        WriteResults = false,
    };

    [Fact]
    public async Task Input_file_components_receive_the_chosen_files_even_when_hidden()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-uploader--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;

        await ctx.GetByLabel("Attachments").SetInputFilesAsync(
            StoryFile.FromText("notes.txt", "hello"),
            StoryFile.FromText("data.csv", "a,b", "text/csv"));

        await ctx.Expect(ctx.GetByRole("listitem")).ToHaveTextAsync(["notes.txt text/plain 5 hello", "data.csv text/csv 3 a,b"]);
        Assert.Contains(ctx.Steps, s => s.Title.StartsWith("set input files notes.txt, data.csv on label=Attachments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inputs_that_cannot_take_the_files_fail_with_the_reason()
    {
        await using var session = await BunitStoryRunner.RenderAsync(Catalog.Get("tests-uploader--default"), Options, TestContext.Current.CancellationToken);
        var ctx = session.StoryContext;

        var multiple = await Assert.ThrowsAsync<ElementNotActionableException>(() =>
            ctx.GetByLabel("Avatar").SetInputFilesAsync(StoryFile.FromText("a.txt", "a"), StoryFile.FromText("b.txt", "b")));
        Assert.Contains("does not accept multiple files", multiple.Message, StringComparison.Ordinal);

        var plain = await Assert.ThrowsAsync<ElementNotActionableException>(() => ctx.GetByLabel("Plain").SetInputFilesAsync(StoryFile.FromText("a.txt", "a")));
        Assert.Contains("only choose files for Blazor's InputFile component", plain.Message, StringComparison.Ordinal);

        var notFile = await Assert.ThrowsAsync<ElementNotActionableException>(() => ctx.GetByRole("list").SetInputFilesAsync(StoryFile.FromText("a.txt", "a")));
        Assert.Contains("element is not a file input", notFile.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_know_their_content_type_from_the_extension()
    {
        Assert.Equal("image/png", StoryFile.ContentTypeOf("A.PNG"));
        Assert.Equal("application/octet-stream", StoryFile.ContentTypeOf("archive.unknown"));
        Assert.Equal("set input files a.txt on", new Drivers.ElementAction(Drivers.ElementActionKind.SetInputFiles) { Files = [StoryFile.FromText("a.txt", "x")] }.Describe());
    }
}
