using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorTestPlatform.Core.Tests;

// ImpactPanel renders ImpactCard, which renders Greeter: a change to Greeter affects the panel's stories too.
public sealed class ImpactCard : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Greeter>(0);
        builder.CloseComponent();
    }
}

public sealed class ImpactPanel : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddContent(1, (RenderFragment)(inner =>
        {
            inner.OpenComponent<ImpactCard>(0);
            inner.CloseComponent();
        }));
        builder.CloseElement();
    }
}

public sealed class ImpactLeaf : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "leaf");
}

public sealed class ImpactGrid<TItem> : ComponentBase
{
    [Parameter]
    public IEnumerable<TItem> Items { get; set; } = [];
}

// Renders ImpactGrid with an inferred type argument, through Razor's generated TypeInference helper.
public sealed class ImpactDirectory : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        global::__Blazor.BlazorTestPlatform.Core.Tests.ImpactDirectory.TypeInference.CreateImpactGrid_0(builder, 0, 1, new[] { "Ada" });
}

public sealed class StoryImpactTests
{
    private const string ThisFile = "tests/BlazorTestPlatform.Core.Tests/StoryImpactTests.cs";

    // Not discovered by the assembly catalog (private), so other tests keep their Greeter-only catalog.
    private sealed class PanelStories() : Stories<ImpactPanel>()
    {
        public override string Title => "Impact/Panel";

        protected override void Define(StoryCollectionBuilder<ImpactPanel> stories) => stories.Add("Default");
    }

    private sealed class LeafStories() : Stories<ImpactLeaf>()
    {
        public override string Title => "Impact/Leaf";

        protected override void Define(StoryCollectionBuilder<ImpactLeaf> stories) => stories.Add("Default");
    }

    private sealed class GreeterImpactStories : Stories<Greeter>
    {
        public override string Title => "Impact/Greeter";

        protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Default");
    }

    private sealed class DirectoryStories : Stories<ImpactDirectory>
    {
        public override string Title => "Impact/Directory";

        protected override void Define(StoryCollectionBuilder<ImpactDirectory> stories) => stories.Add("Default");
    }

    private static readonly StoryCatalog Catalog = StoryCatalog.FromCollections([new PanelStories(), new LeafStories(), new GreeterImpactStories(), new DirectoryStories()]);

    [Fact]
    public void A_component_change_affects_its_stories_and_the_stories_of_components_that_render_it()
    {
        var impact = StoryImpact.Analyze(Catalog, ["samples/Ui/Greeter.razor.css"]);

        Assert.False(impact.RunsEverything);
        Assert.Equal(["impact-greeter--default", "impact-panel--default"], impact.Ids.Order(StringComparer.Ordinal));
        Assert.Equal(["samples/Ui/Greeter.razor.css"], impact.Reasons["impact-panel--default"]);
    }

    [Fact]
    public void A_story_file_change_affects_the_collections_it_declares()
    {
        var impact = StoryImpact.Analyze(Catalog, [ThisFile.Replace('/', '\\')]);

        Assert.False(impact.RunsEverything);
        Assert.Equal(["impact-leaf--default", "impact-panel--default"], impact.Ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Files_that_carry_a_story_id_affect_that_story()
    {
        var impact = StoryImpact.Analyze(Catalog, ["samples/__snapshots__/impact-leaf--default.chromium.desktop-light.png"]);

        Assert.Equal(["impact-leaf--default"], impact.Ids);
    }

    [Fact]
    public void Baselines_stored_under_the_story_id_affect_that_story()
    {
        // SnapshotNaming: {story-id}/{viewport}.{theme}.{browser}-{platform}.png
        var impact = StoryImpact.Analyze(Catalog, ["samples/__snapshots__/impact-leaf--default/desktop.light.chromium-linux.png"]);

        Assert.False(impact.RunsEverything);
        Assert.Equal(["impact-leaf--default"], impact.Ids);
    }

    [Fact]
    public void Generic_components_rendered_with_inferred_type_arguments_are_dependencies()
    {
        var impact = StoryImpact.Analyze(Catalog, ["samples/Ui/ImpactGrid.razor"]);

        Assert.False(impact.RunsEverything);
        Assert.Equal(["impact-directory--default"], impact.Ids);
    }

    [Fact]
    public void Documentation_changes_affect_nothing()
    {
        var impact = StoryImpact.Analyze(Catalog, ["docs/stories.md", "assets/logo.svg"]);

        Assert.False(impact.RunsEverything);
        Assert.Empty(impact.Stories);
    }

    [Theory]
    [InlineData("src/Shared/Formatting.cs")]
    [InlineData("samples/Ui/_Imports.razor")]
    [InlineData("Directory.Packages.props")]
    [InlineData("samples/Ui/wwwroot/site.css")]
    public void Changes_that_cannot_be_placed_run_everything(string file)
    {
        var impact = StoryImpact.Analyze(Catalog, ["samples/Ui/ImpactLeaf.razor", file]);

        Assert.True(impact.RunsEverything);
        Assert.Equal([file], impact.UnmappedFiles);
        Assert.Equal(Catalog.Count, impact.Stories.Count);
        Assert.Equal(["samples/Ui/ImpactLeaf.razor"], impact.Reasons["impact-leaf--default"]);
    }

    [Fact]
    public void Unknown_git_references_mean_run_everything()
    {
        Assert.Null(StoryImpact.ChangedFilesSince("refs/heads/does-not-exist-" + Guid.NewGuid().ToString("N")));
        Assert.Throws<ArgumentException>(() => StoryImpact.ChangedFilesSince("--output=/tmp/x"));
    }

    [Fact]
    public void Changes_since_a_reference_include_uncommitted_files()
    {
        var changed = StoryImpact.ChangedFilesSince("HEAD");
        Assert.SkipWhen(changed is null, "Not a git checkout (or git is not installed).");

        // Relative to the repository root, like the paths story impact matches against.
        Assert.All(changed!, file => Assert.False(Path.IsPathRooted(file)));
        if (!IsCommitted())
        {
            // An uncommitted (or untracked) change to this very file is part of the list.
            Assert.Contains(ThisFile, changed!);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")) && !File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static bool IsCommitted()
    {
        using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "status --porcelain -- " + ThisFile)
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var status = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return status.Length == 0;
    }
}
