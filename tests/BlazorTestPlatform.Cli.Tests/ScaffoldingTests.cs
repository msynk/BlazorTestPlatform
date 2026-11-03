using Assert = Xunit.Assert;
using BlazorTestPlatform.Cli;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorTestPlatform.Cli.Tests;

public sealed class ScaffoldingTests
{
    private const string Component = """
        using Microsoft.AspNetCore.Components;

        namespace Scaffolded.Ui;

        public sealed class Widget : ComponentBase
        {
        }
        """;

    [Theory]
    [InlineData("Xunit")]
    [InlineData("NUnit")]
    [InlineData("MSTest")]
    [InlineData("TUnit")]
    public void Generated_test_and_story_classes_compile_against_the_framework_adapters(string name)
    {
        // The CLI's types are internal: test data passes their names.
        var framework = Enum.Parse<TestFramework>(name);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Scaffolded.Tests",
            [
                CSharpSyntaxTree.ParseText(Component, path: "Widget.cs"),
                CSharpSyntaxTree.ParseText(Scaffolding.TestClass(framework, "Scaffolded.Tests"), path: "StoryTests.cs"),
                CSharpSyntaxTree.ParseText(Scaffolding.StoryClass("Scaffolded.Tests", "Scaffolded.Ui.Widget"), path: "WidgetStories.cs"),
                CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Threading; global using System.Threading.Tasks;", path: "Usings.cs"),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Theory]
    [InlineData("GitHub", ".github/workflows/blazor-ui-tests.yml", "${{ secrets.GITHUB_TOKEN }}")]
    [InlineData("Azure", "azure-pipelines.blazor-ui-tests.yml", "${{ if eq(variables['Build.Reason'], 'PullRequest') }}:")]
    [InlineData("GitLab", ".gitlab/blazor-ui-tests.gitlab-ci.yml", "BTP_CHANGED_SINCE: origin/$CI_MERGE_REQUEST_TARGET_BRANCH_NAME")]
    public void Pipelines_run_the_project_on_the_matching_playwright_image(string system, string file, string expression)
    {
        var ci = Enum.Parse<CiSystem>(system);
        var repository = Path.Combine(Path.GetTempPath(), "repo");
        var plan = Scaffolding.Plan(new ScaffoldOptions
        {
            ProjectDirectory = Path.Combine(repository, "tests", "Ui.Tests"),
            Namespace = "Ui.Tests",
            Ci = ci,
            RepositoryRoot = repository,
        });

        var (_, pipeline) = Assert.Single(plan, f => f.Path == Path.Combine(repository, file.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains($"mcr.microsoft.com/playwright/dotnet:v{PinnedPlaywrightVersion()}-noble", pipeline, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Ui.Tests", pipeline, StringComparison.Ordinal);
        Assert.Contains("btp report -p tests/Ui.Tests", pipeline, StringComparison.Ordinal);
        Assert.Contains(expression, pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("{project}", pipeline, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "dotnet test tests/Ui.Tests")]
    [InlineData(true, "dotnet test --project tests/Ui.Tests")]
    public void Pipelines_pass_the_project_the_way_the_runner_mode_takes_it(bool testingPlatform, string command)
    {
        var repository = Path.Combine(Path.GetTempPath(), "repo");
        var plan = Scaffolding.Plan(new ScaffoldOptions
        {
            ProjectDirectory = Path.Combine(repository, "tests", "Ui.Tests"),
            Namespace = "Ui.Tests",
            Ci = CiSystem.GitHub,
            RepositoryRoot = repository,
            TestingPlatform = testingPlatform,
        });

        var pipeline = plan.Single(f => f.Path.EndsWith("blazor-ui-tests.yml", StringComparison.Ordinal)).Content;
        Assert.Contains($"run: {command}\n", pipeline.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_runner_mode_comes_from_the_nearest_global_json()
    {
        var repository = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "btp-scaffold", Guid.NewGuid().ToString("N"))).FullName;
        var project = Directory.CreateDirectory(Path.Combine(repository, "tests", "Ui.Tests")).FullName;

        File.WriteAllText(Path.Combine(repository, "global.json"), """{ "sdk": { "version": "10.0.100" } }""");
        Assert.False(Scaffolding.UsesTestingPlatform(project));

        File.WriteAllText(Path.Combine(repository, "global.json"), """
            {
              // comments are allowed
              "test": { "runner": "Microsoft.Testing.Platform" },
            }
            """);
        Assert.True(Scaffolding.UsesTestingPlatform(project));
    }

    // The version Directory.Packages.props pins, which the browser tests and their baselines use.
    private static string PinnedPlaywrightVersion()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        var props = File.ReadAllText(Path.Combine(directory.FullName, "Directory.Packages.props"));
        return System.Text.RegularExpressions.Regex.Match(props, @"Include=""Microsoft\.Playwright"" Version=""([^""]+)""").Groups[1].Value;
    }

    [Fact]
    public void The_plan_writes_settings_tests_and_a_first_story()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ui.Tests");
        var plan = Scaffolding.Plan(new ScaffoldOptions { ProjectDirectory = directory, Namespace = "Ui.Tests", Component = "Contoso.UI.Button" });

        Assert.Equal(
            [Path.Combine(directory, "btp.json"), Path.Combine(directory, "StoryTests.cs"), Path.Combine(directory, "ButtonStories.cs")],
            plan.Select(f => f.Path));
        Assert.Contains("public sealed class ButtonStories : Stories<Contoso.UI.Button>", plan[2].Content, StringComparison.Ordinal);
        Assert.Contains("\"failOn\": \"serious\"", plan[0].Content, StringComparison.Ordinal);
    }
}
