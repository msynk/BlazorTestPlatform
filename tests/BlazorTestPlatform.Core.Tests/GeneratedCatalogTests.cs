namespace BlazorTestPlatform.Core.Tests;

// Discovery edge cases: the source generator and the reflection scan must agree on every one of them.
internal sealed class InternalStories : Stories<Greeter>
{
    public override string Title => "Edge/Internal";

    protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Included");
}

public static class Outer
{
    public sealed class NestedPublicStories : Stories<Greeter>
    {
        public override string Title => "Edge/Nested public";

        protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Included");
    }
}

public sealed class Container
{
    private sealed class PrivateStories : Stories<Greeter>
    {
        public override string Title => "Edge/Private";

        protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Excluded");
    }

    private static class PrivateScope
    {
        public sealed class InsidePrivateStories : Stories<Greeter>
        {
            public override string Title => "Edge/Inside private";

            protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Excluded");
        }
    }

    public string Touch() => nameof(PrivateStories) + nameof(PrivateScope.InsidePrivateStories);
}

public sealed class WithParameterStories(string title) : Stories<Greeter>
{
    public override string Title => title;

    protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Excluded");
}

public abstract class AbstractStories : Stories<Greeter>
{
    public override string Title => "Edge/Abstract";

    protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Excluded");
}

public sealed class GenericStories<T> : Stories<Greeter>
{
    public override string Title => "Edge/Generic " + typeof(T).Name;

    protected override void Define(StoryCollectionBuilder<Greeter> stories) => stories.Add("Excluded");
}

public sealed class GeneratedCatalogTests
{
    [Fact]
    public void Assembly_has_a_generated_provider()
    {
        Assert.True(StoryCatalog.HasGeneratedProvider(typeof(GeneratedCatalogTests).Assembly));
    }

    [Fact]
    public void Generated_and_reflected_catalogs_are_identical()
    {
        var generated = StoryCatalog.FromAssemblies(typeof(GeneratedCatalogTests).Assembly);
        var reflected = StoryCatalog.FromAssembliesByReflection(typeof(GeneratedCatalogTests).Assembly);

        Assert.Equal(reflected.Ids, generated.Ids);
        Assert.Equal(reflected.Stories.Select(s => string.Join(",", s.Tags.Order(StringComparer.Ordinal))), generated.Stories.Select(s => string.Join(",", s.Tags.Order(StringComparer.Ordinal))));
    }

    [Fact]
    public void Selection_rules_include_and_exclude_the_same_types()
    {
        var ids = StoryCatalog.FromAssemblies(typeof(GeneratedCatalogTests).Assembly).Ids.ToArray();

        Assert.Contains("edge-internal--included", ids);
        Assert.Contains("edge-nested-public--included", ids);
        Assert.DoesNotContain(ids, id => id.EndsWith("--excluded", StringComparison.Ordinal));
        Assert.Contains("global", StoryCatalog.FromAssemblies(typeof(GeneratedCatalogTests).Assembly).Get("edge-internal--included").Tags);
    }
}
