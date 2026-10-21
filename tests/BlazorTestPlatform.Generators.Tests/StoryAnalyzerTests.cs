using System.Collections.Immutable;
using BlazorTestPlatform.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorTestPlatform.Generators.Tests;

public sealed class StoryAnalyzerTests
{
    private const string Components = """
        using System.Collections.Generic;
        using Microsoft.AspNetCore.Components;
        using BlazorTestPlatform;

        public enum Tone { Calm, Loud }

        public class BaseButton : ComponentBase
        {
            [Parameter] public bool Disabled { get; set; }
        }

        public sealed class Button : BaseButton
        {
            [Parameter] public string? Title { get; set; }
            [Parameter] public Tone Tone { get; set; }
            [Parameter] public string? Value { get; set; }
            [Parameter] public EventCallback<string?> ValueChanged { get; set; }
            [Parameter] public int Count { get; set; }
            [Parameter] public EventCallback<long> CountChanged { get; set; }
            [Parameter] public EventCallback OnClick { get; set; }
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }
            public string? NotAParameter { get; set; }
        }

        public sealed class Badge : ComponentBase
        {
            [Parameter] public string? Text { get; set; }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string stories)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Stories",
            [CSharpSyntaxTree.ParseText(Components, path: "Components.cs"), CSharpSyntaxTree.ParseText(stories, path: "Stories.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // The samples must compile: the analyzer is tested on valid C#, as users write it.
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return await compilation.WithAnalyzers([new StoryAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    private static string Stories(string body) => $$"""
        using BlazorTestPlatform;

        public sealed class ButtonStories : Stories<Button>
        {
            protected override void Define(StoryCollectionBuilder<Button> stories)
            {
                {{body}}
            }
        }

        public sealed class BadgeStories : Stories<Badge>
        {
            protected override void Define(StoryCollectionBuilder<Badge> stories)
            {
                stories.Add("Plain", s => s.Arg(b => b.Text, "New"));
            }
        }
        """;

    [Fact]
    public async Task Valid_stories_produce_no_diagnostics()
    {
        var diagnostics = await AnalyzeAsync(Stories("""
            stories.Defaults(s => s.Arg(b => b.Disabled, false));
            stories.Add("Primary", s => s
                .Arg(b => b.Title, "Save")
                .Bind(b => b.Value, "draft")
                .Action(b => b.OnClick)
                .Fragment(b => b.ChildContent, builder => builder.AddContent(0, "x"))
                .Attribute("data-track", "cta"));
            stories.AddVariants(v => v.Vary(b => b.Tone, Tone.Calm, Tone.Loud));
            stories.Add("Secondary");
            """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Non_parameters_are_reported_for_args_and_variants()
    {
        var diagnostics = await AnalyzeAsync(Stories("""
            stories.Add("A", s => s.Arg(b => b.NotAParameter, "x"));
            stories.AddVariants(v => v.Vary(b => b.NotAParameter, "a", "b"));
            """));

        Assert.Equal(["BTP001", "BTP001"], diagnostics.Select(d => d.Id));
        Assert.Equal("'Button.NotAParameter' is not a component parameter. Add [Parameter] to the property, or use Attribute(\"name\", value) for an unmatched attribute.", diagnostics[0].GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    public async Task Bindings_need_a_changed_callback_of_the_same_type()
    {
        var diagnostics = await AnalyzeAsync(Stories("""
            stories.Add("Title", s => s.Bind(b => b.Title, "x"));
            stories.Add("Count", s => s.Bind(b => b.Count, 1));
            """));

        Assert.Equal(["BTP002", "BTP002"], diagnostics.Select(d => d.Id));
        Assert.Contains("'Button' has no 'TitleChanged' parameter of type EventCallback<string?>", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'CountChanged' parameter of type EventCallback<int>", diagnostics[1].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attributes_are_checked_against_declared_and_captured_parameters()
    {
        var diagnostics = await AnalyzeAsync(Stories("""
            stories.Add("A", s => s.Attribute("title", "x").Attribute("disabled", true));
            """).Replace("""stories.Add("Plain", s => s.Arg(b => b.Text, "New"));""", """stories.Add("Plain", s => s.Attribute("data-x", 1));""", StringComparison.Ordinal));

        Assert.Equal(["BTP003", "BTP003", "BTP004"], diagnostics.OrderBy(d => d.Location.SourceSpan.Start).Select(d => d.Id));
        Assert.Contains("'Title' is a parameter of 'Button'. Use Arg(c => c.Title, value)", diagnostics.Single(d => d.GetMessage().Contains("Title", StringComparison.Ordinal)).GetMessage(), StringComparison.Ordinal);
        Assert.Contains(diagnostics, d => d.Id == "BTP003" && d.GetMessage().StartsWith("'Disabled' is a parameter", StringComparison.Ordinal)); // inherited
        Assert.Equal("'Badge' has no [Parameter(CaptureUnmatchedValues = true)] property, so Attribute(\"data-x\", ...) fails when the story renders", diagnostics.Single(d => d.Id == "BTP004").GetMessage());
    }

    [Fact]
    public async Task Duplicate_story_ids_are_reported_once_per_extra_declaration()
    {
        var diagnostics = await AnalyzeAsync(Stories("""
            stories.Add("Primary");
            stories.Add("primary!");
            stories.Add("Primary", s => s.Arg(b => b.Title, "x"));
            stories.Add("Secondary");
            """));

        Assert.Equal(["BTP005", "BTP005"], diagnostics.Select(d => d.Id));
        Assert.Equal("Story 'primary!' is declared twice in this collection: both get the id 'primary'. Rename one of them.", diagnostics[0].GetMessage());
    }

    [Theory]
    [InlineData("Click raises OnClick", "click-raises-onclick")]
    [InlineData("Café   déjà-vu", "cafe-deja-vu")]
    [InlineData("!!!", "story")]
    public void Slugs_match_story_ids(string name, string slug)
    {
        Assert.Equal(slug, StoryAnalyzer.Slugify(name));
        Assert.Equal(StoryId.Slugify(name), StoryAnalyzer.Slugify(name));
    }
}
