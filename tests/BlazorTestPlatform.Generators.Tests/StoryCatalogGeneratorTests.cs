using BlazorTestPlatform.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorTestPlatform.Generators.Tests;

public sealed class StoryCatalogGeneratorTests
{
    private const string Source = """
        using System;
        using Microsoft.AspNetCore.Components;
        using BlazorTestPlatform;

        public sealed class Badge : ComponentBase
        {
            [Parameter] public string? Text { get; set; }
        }

        public sealed class BadgeStories : Stories<Badge>
        {
            protected override void Define(StoryCollectionBuilder<Badge> stories) => stories.Add("Default");
        }

        public sealed record RecordDefaults : IStoryConfiguration
        {
            public void Configure(IStoryBuilder story) => story.Tags("record");
        }

        public sealed class RequiredDefaults : IStoryConfiguration
        {
            public required string Tag { get; init; }

            public void Configure(IStoryBuilder story) => story.Tags(Tag ?? "none");
        }

        [Obsolete("Use BadgeStories", error: true)]
        public sealed class RetiredStories : Stories<Badge>
        {
            protected override void Define(StoryCollectionBuilder<Badge> stories) => stories.Add("Old");
        }

        [Obsolete("Soft deprecation")]
        public sealed class DeprecatedStories : Stories<Badge>
        {
            public override string Title => "Deprecated";

            protected override void Define(StoryCollectionBuilder<Badge> stories) => stories.Add("Still here");
        }

        file sealed class FileLocalStories : Stories<Badge>
        {
            public override string Title => "File local";

            protected override void Define(StoryCollectionBuilder<Badge> stories) => stories.Add("Hidden");
        }
        """;

    private static (Compilation Output, string Generated) Run(string assemblyName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(Source, path: "Stories.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new StoryCatalogGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        var generated = output.SyntaxTrees.Single(t => t.FilePath.EndsWith("StoryCatalog.g.cs", StringComparison.Ordinal)).ToString();
        return (output, generated);
    }

    [Theory]
    [InlineData("Contoso.Stories")]
    [InlineData("1Stories")]
    [InlineData("My-Stories")]
    public void Generated_catalog_compiles_for_any_assembly_name(string assemblyName)
    {
        var (output, _) = Run(assemblyName);

        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Id is "CS0612" or "CS0618"));
    }

    [Fact]
    public void Records_and_required_members_are_discovered_while_error_obsolete_and_file_local_types_are_not()
    {
        var (_, generated) = Run("Stories");

        Assert.Contains("new global::RecordDefaults()", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Activator.CreateInstance<global::RequiredDefaults>()", generated, StringComparison.Ordinal);
        Assert.Contains("new global::DeprecatedStories()", generated, StringComparison.Ordinal);
        Assert.Contains("new global::BadgeStories()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RetiredStories", generated, StringComparison.Ordinal);
        Assert.Contains("global::BlazorTestPlatform.StoryCollection.WithSource(new global::BadgeStories(), @\"Stories.cs\", 10)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("FileLocalStories", generated, StringComparison.Ordinal);
    }
}
