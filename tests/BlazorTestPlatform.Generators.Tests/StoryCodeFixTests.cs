using System.Collections.Immutable;
using BlazorTestPlatform.CodeFixes;
using BlazorTestPlatform.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorTestPlatform.Generators.Tests;

public sealed class StoryCodeFixTests
{
    private const string Components = """
        using System.Collections.Generic;
        using Microsoft.AspNetCore.Components;

        public sealed class Button : ComponentBase
        {
            [Parameter] public string? Title { get; set; }
            [Parameter] public string? Value { get; set; }
            [Parameter] public int Count { get; set; }
            [Parameter] public EventCallback<long> CountChanged { get; set; }
            public string? Hint { get; set; }
            [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? Attributes { get; set; }
        }
        """;

    private static string Stories(string body) => $$"""
        using BlazorTestPlatform;

        public sealed class ButtonStories : Stories<Button>
        {
            protected override void Define(StoryCollectionBuilder<Button> stories)
            {
                {{body}}
            }
        }
        """;

    // Applies the first fix offered for the first diagnostic with the id; returns the changed sources and the remaining diagnostics.
    private static async Task<(string Stories, string Components, ImmutableArray<Diagnostic> Remaining)> FixAsync(string stories, string id)
    {
        using var workspace = new AdhocWorkspace();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var project = workspace.AddProject("Stories", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .WithMetadataReferences(references);
        var components = project.AddDocument("Components.cs", Components);
        var document = components.Project.AddDocument("Stories.cs", stories);

        var diagnostic = (await DiagnosticsAsync(document.Project)).First(d => d.Id == id);
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new StoryCodeFixProvider().RegisterCodeFixesAsync(context);

        var operation = (await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
        var solution = operation.ChangedSolution;
        var fixedStories = (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString();
        var fixedComponents = (await solution.GetDocument(components.Id)!.GetTextAsync()).ToString();
        return (fixedStories, fixedComponents, await DiagnosticsAsync(solution.GetProject(project.Id)!));
    }

    private static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(Project project)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        return await compilation.WithAnalyzers([new StoryAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task Attribute_for_a_declared_parameter_becomes_a_typed_arg()
    {
        var (stories, _, remaining) = await FixAsync(Stories("""stories.Add("Default", s => s.Attribute("title", "Save"));"""), "BTP003");

        Assert.Contains("""stories.Add("Default", s => s.Arg(c => c.Title, "Save"));""", stories, StringComparison.Ordinal);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task The_lambda_parameter_does_not_shadow_an_enclosing_name()
    {
        var (stories, _, remaining) = await FixAsync(Stories("""stories.Add("Default", c => c.Attribute(value: "Save", name: "Title"));"""), "BTP003");

        Assert.Contains("""c.Arg(component => component.Title, "Save")""", stories, StringComparison.Ordinal);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task A_binding_without_a_matching_callback_becomes_a_one_way_arg()
    {
        var (stories, _, remaining) = await FixAsync(Stories("""stories.Add("Default", s => s.Bind(b => b.Count, initialValue: 3));"""), "BTP002");

        Assert.Contains("""s.Arg(b => b.Count, value: 3)""", stories, StringComparison.Ordinal);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task A_duplicate_story_gets_the_next_free_name()
    {
        var (stories, _, remaining) = await FixAsync(Stories("""
            stories.Add("Default");
            stories.Add("Default 2");
            stories.Add("default");
            """), "BTP005");

        Assert.Contains("""stories.Add("default 3");""", stories, StringComparison.Ordinal);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task A_property_that_is_not_a_parameter_gets_the_attribute_in_its_declaring_file()
    {
        var (_, components, remaining) = await FixAsync(Stories("""stories.Add("Default", s => s.Arg(b => b.Hint, "Tip"));"""), "BTP001");

        Assert.Matches(@"\[Parameter\]\s*public string\? Hint", components);
        Assert.Empty(remaining);
    }
}
