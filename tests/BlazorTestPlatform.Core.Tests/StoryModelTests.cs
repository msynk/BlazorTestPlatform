using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform.Core.Tests;

public sealed class StoryModelTests
{
    private static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<StoryModelTests>();

    [Theory]
    [InlineData("Inputs/Button", "inputs-button")]
    [InlineData("  Héllo,  Wörld!  ", "hello-world")]
    [InlineData("Variant=Primary, Size=Small", "variant-primary-size-small")]
    [InlineData("C# & .NET 10", "c-net-10")]
    [InlineData("!!!", "story")]
    public void Slugify_is_stable_and_url_safe(string input, string expected) => Assert.Equal(expected, StoryId.Slugify(input));

    [Fact]
    public void Story_ids_combine_title_and_name() =>
        Assert.Equal("inputs-button--primary", StoryId.Create("Inputs/Button", "Primary"));

    [Fact]
    public void Catalog_discovers_collections_and_global_configurations()
    {
        var story = Catalog.Get("samples-greeter--default");

        Assert.Equal(typeof(Greeter), story.ComponentType);
        Assert.Contains("global", story.Tags);
        Assert.Contains("default-tag", story.Tags);
        Assert.Equal("Ada", Assert.Single(story.Args, a => a.Name == "Name").CreateValue(null!));
    }

    [Fact]
    public void Story_configuration_overrides_defaults_and_removes_tags()
    {
        var story = Catalog.Get("samples-greeter--excited");

        Assert.Equal("Grace!", story.Args.Single(a => a.Name == "Name").CreateValue(null!));
        Assert.Contains("smoke", story.Tags);
        Assert.DoesNotContain("default-tag", story.Tags);
    }

    [Fact]
    public void Unset_event_callbacks_become_auto_actions()
    {
        var story = Catalog.Get("samples-greeter--default");

        Assert.Contains(story.Args, a => a.Name == "OnPing" && a.Kind == StoryArgKind.Action);
        Assert.Contains(story.Args, a => a.Name == "OnRename" && a.Kind == StoryArgKind.Action);
    }

    [Fact]
    public void Variants_produce_the_cartesian_product()
    {
        var variants = Catalog.Stories.Where(s => s.Name.StartsWith("Count=", StringComparison.Ordinal)).Select(s => s.Name).ToArray();

        Assert.Equal(["Count=1, Name=A", "Count=1, Name=B", "Count=2, Name=A", "Count=2, Name=B"], variants);
    }

    [Fact]
    public void Default_seed_derives_from_the_story_id()
    {
        var a = Catalog.Get("samples-greeter--default");
        var b = Catalog.Get("samples-greeter--excited");

        Assert.NotEqual(a.Seed, b.Seed);
        Assert.Equal(a.Seed, StoryCatalog.FromCollections([new GreeterStories()], [new GlobalTags()]).Get(a.Id).Seed);
    }

    [Fact]
    public void Duplicate_ids_are_rejected_with_both_locations()
    {
        var error = Assert.Throws<InvalidOperationException>(() => StoryCatalog.FromCollections([new GreeterStories(), new GreeterStories()]));
        Assert.Contains("samples-greeter--default", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_ids_suggest_close_matches()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => Catalog.Get("samples-greeter--defaul"));
        Assert.Contains("samples-greeter--default", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_filters_by_tag_glob_and_component()
    {
        Assert.Single(Catalog.WithTag("smoke").Stories);
        Assert.Equal(Catalog.Count, Catalog.ForComponent<Greeter>().Count);
        Assert.Equal(4, Catalog.Matching("samples-greeter--count-*").Count);
        var group = Assert.Single(Catalog.Groups, g => g.Title == "Samples/Greeter");
        Assert.Equal(["Samples", "Greeter"], group.Segments);
    }

    [Fact]
    public void Expressions_must_target_parameters()
    {
        var collection = new BadStories();
        var error = Assert.Throws<ArgumentException>(() => StoryCatalog.FromCollections([collection]));
        Assert.Contains("is not a component parameter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_requires_a_matching_changed_callback()
    {
        var error = Assert.Throws<ArgumentException>(() => StoryCatalog.FromCollections([new BadBindStories()]));
        Assert.Contains("CountChanged", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Action_log_records_and_serializes_arguments()
    {
        var log = new ActionLog();
        log.Record("OnRename", "Ada");
        log.Record("OnPing", null);

        var parsed = ActionLog.ParseJson(log.ToJson());

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Ada", parsed[0].ArgumentAs<string>());
        Assert.Equal(2, parsed[1].Sequence);
        Assert.Single(log.For("OnPing"));
    }

    [Fact]
    public void Render_context_creates_fake_authentication_for_users()
    {
        var story = StoryCatalog.FromCollections([new UserStories()]).Stories[0];
        var context = new StoryRenderContext(story, new EmptyServices());

        Assert.NotNull(context.Authentication);
        Assert.Equal("Ada", context.Authentication!.User.Name);
    }

    // These stories are wrong on purpose: they check the run-time errors that BTP001/BTP002 report at compile time.
#pragma warning disable BTP001, BTP002
    private sealed class BadStories : Stories<Greeter>
    {
        protected override void Define(StoryCollectionBuilder<Greeter> stories) =>
            stories.Add("Bad", s => s.Arg(g => g.NotAParameter, "x"));
    }

    private sealed class BadBindStories : Stories<Greeter>
    {
        protected override void Define(StoryCollectionBuilder<Greeter> stories) =>
            stories.Add("Bad", s => s.Bind(g => g.Count, 1));
    }
#pragma warning restore BTP001, BTP002

    private sealed class UserStories : Stories<Greeter>
    {
        protected override void Define(StoryCollectionBuilder<Greeter> stories) =>
            stories.Add("Signed in", s => s.AuthenticatedAs("Ada", "Admin"));
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
