using System.Reflection;
using System.Text.Json;

namespace BlazorTestPlatform.Cli;

/// <summary>The test frameworks <c>btp init</c> can scaffold for.</summary>
internal enum TestFramework
{
    Xunit,
    NUnit,
    MSTest,
    TUnit,
}

/// <summary>The CI systems <c>btp init</c> can write a pipeline for.</summary>
internal enum CiSystem
{
    None,
    GitHub,
    Azure,
    GitLab,
}

/// <summary>What <c>btp init</c> writes.</summary>
internal sealed record ScaffoldOptions
{
    public required string ProjectDirectory { get; init; }

    public required string Namespace { get; init; }

    public TestFramework Framework { get; init; } = TestFramework.Xunit;

    public CiSystem Ci { get; init; } = CiSystem.None;

    /// <summary>A component to write a first story for (type name, optionally namespace-qualified).</summary>
    public string? Component { get; init; }

    /// <summary>Where CI files go (the repository root); defaults to the project directory.</summary>
    public string? RepositoryRoot { get; init; }

    /// <summary>
    /// <c>dotnet test</c> runs in Microsoft.Testing.Platform mode (global.json <c>"test": { "runner": "Microsoft.Testing.Platform" }</c>),
    /// which takes the project as <c>--project</c>; VSTest mode, the default, takes it as an argument.
    /// </summary>
    public bool TestingPlatform { get; init; }
}

/// <summary>
/// <c>btp init</c>: writes <c>btp.json</c>, a story test class for the chosen test framework (every story as an in-memory
/// test, with skips reported as skips), an optional first story and an optional CI pipeline. Existing files are kept
/// unless forced.
/// </summary>
internal static class Scaffolding
{
    /// <summary>The Playwright version BlazorTestPlatform.Playwright depends on (its Docker image renders the baselines).</summary>
    public static string PlaywrightVersion { get; } =
        typeof(Scaffolding).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "PlaywrightVersion")?.Value is { Length: > 0 } version
            ? version
            : throw new InvalidOperationException("The btp tool was built without its PlaywrightVersion metadata.");

    /// <summary>The package that integrates <paramref name="framework"/>.</summary>
    public static string AdapterPackage(TestFramework framework) => framework switch
    {
        TestFramework.Xunit => "BlazorTestPlatform.Xunit",
        TestFramework.NUnit => "BlazorTestPlatform.NUnit",
        TestFramework.MSTest => "BlazorTestPlatform.MSTest",
        _ => "BlazorTestPlatform.TUnit",
    };

    /// <summary>The files to write, as paths relative to the current directory and their content.</summary>
    public static IReadOnlyList<(string Path, string Content)> Plan(ScaffoldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var files = new List<(string, string)>
        {
            (Path.Combine(options.ProjectDirectory, "btp.json"), BtpJson),
            (Path.Combine(options.ProjectDirectory, "StoryTests.cs"), TestClass(options.Framework, options.Namespace)),
        };

        if (!string.IsNullOrWhiteSpace(options.Component))
        {
            var name = options.Component.Split('.')[^1];
            files.Add((Path.Combine(options.ProjectDirectory, name + "Stories.cs"), StoryClass(options.Namespace, options.Component.Trim())));
        }

        var repository = options.RepositoryRoot ?? options.ProjectDirectory;
        var project = Path.GetRelativePath(repository, options.ProjectDirectory).Replace('\\', '/');
        var test = TestCommand(project, options.TestingPlatform);
        switch (options.Ci)
        {
            case CiSystem.GitHub:
                files.Add((Path.Combine(repository, ".github", "workflows", "blazor-ui-tests.yml"), GitHubWorkflow(project, test)));
                break;
            case CiSystem.Azure:
                files.Add((Path.Combine(repository, "azure-pipelines.blazor-ui-tests.yml"), AzurePipeline(project, test)));
                break;
            case CiSystem.GitLab:
                files.Add((Path.Combine(repository, ".gitlab", "blazor-ui-tests.gitlab-ci.yml"), GitLabJob(project, test)));
                break;
        }

        return files;
    }

    /// <summary>The <c>dotnet test</c> command for the project in the runner mode the repository uses.</summary>
    public static string TestCommand(string project, bool testingPlatform) =>
        testingPlatform ? $"dotnet test --project {project}" : $"dotnet test {project}";

    /// <summary>
    /// True when the global.json that applies in <paramref name="directory"/> (the nearest one, as the SDK finds it)
    /// selects Microsoft.Testing.Platform as the <c>dotnet test</c> runner.
    /// </summary>
    public static bool UsesTestingPlatform(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, "global.json");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("test", out var test)
                    && test.ValueKind == JsonValueKind.Object
                    && test.TryGetProperty("runner", out var runner)
                    && runner.ValueKind == JsonValueKind.String
                    && string.Equals(runner.GetString(), "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    private const string BtpJson = """
        {
          "accessibility": {
            "failOn": "serious"
          },
          "visual": {
            "threshold": 0.1
          }
        }

        """;

    public static string TestClass(TestFramework framework, string ns) => framework switch
    {
        TestFramework.Xunit => $$"""
            using BlazorTestPlatform;
            using BlazorTestPlatform.Bunit;
            using BlazorTestPlatform.Xunit;
            using Xunit;

            namespace {{ns}};

            /// <summary>Every story as an in-memory test (bUnit): render, play function, accessibility rules and budgets.</summary>
            public sealed class StoryTests
            {
                // Stories declared in another assembly? Use StoryCatalog.FromAssemblyOf<SomeStories>().
                public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<StoryTests>();

                public static TheoryData<string> Stories => StoryTheoryData.From(Catalog);

                [Theory]
                [MemberData(nameof(Stories))]
                public async Task Story_passes_in_memory(string id) =>
                    StoryTheoryData.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id), cancellationToken: TestContext.Current.CancellationToken));
            }

            """,
        TestFramework.NUnit => $$"""
            using BlazorTestPlatform;
            using BlazorTestPlatform.Bunit;
            using BlazorTestPlatform.NUnit;
            using NUnit.Framework;

            namespace {{ns}};

            /// <summary>Every story as an in-memory test (bUnit): render, play function, accessibility rules and budgets.</summary>
            public sealed class StoryTests
            {
                // Stories declared in another assembly? Use StoryCatalog.FromAssemblyOf<SomeStories>().
                public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<StoryTests>();

                public static IEnumerable<TestCaseData> Stories => StoryTestCases.From(Catalog);

                [TestCaseSource(nameof(Stories))]
                public async Task Story_passes_in_memory(string id) =>
                    StoryTestCases.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id), cancellationToken: TestContext.CurrentContext.CancellationToken));
            }

            """,
        TestFramework.MSTest => $$"""
            using BlazorTestPlatform;
            using BlazorTestPlatform.Bunit;
            using BlazorTestPlatform.MSTest;
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            namespace {{ns}};

            /// <summary>Every story as an in-memory test (bUnit): render, play function, accessibility rules and budgets.</summary>
            [TestClass]
            public sealed class StoryTests
            {
                // Stories declared in another assembly? Use StoryCatalog.FromAssemblyOf<SomeStories>().
                public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<StoryTests>();

                public static IEnumerable<TestDataRow<string>> Stories => StoryDataRows.From(Catalog);

                public TestContext TestContext { get; set; } = null!;

                [TestMethod]
                [DynamicData(nameof(Stories))]
                public async Task Story_passes_in_memory(string id) =>
                    StoryDataRows.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id), cancellationToken: TestContext.CancellationToken));
            }

            """,
        _ => $$"""
            using BlazorTestPlatform;
            using BlazorTestPlatform.Bunit;
            using BlazorTestPlatform.TUnit;
            using TUnit.Core;

            namespace {{ns}};

            /// <summary>Every story as an in-memory test (bUnit): render, play function, accessibility rules and budgets.</summary>
            public sealed class StoryTests
            {
                // Stories declared in another assembly? Use StoryCatalog.FromAssemblyOf<SomeStories>().
                public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<StoryTests>();

                public static IEnumerable<TestDataRow<string>> Stories() => StoryDataRows.From(Catalog);

                [Test]
                [MethodDataSource(nameof(Stories))]
                public async Task Story_passes_in_memory(string id) =>
                    StoryDataRows.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id)));
            }

            """,
    };

    public static string StoryClass(string ns, string component)
    {
        var name = component.Split('.')[^1];
        return $$"""
            using BlazorTestPlatform;

            namespace {{ns}};

            /// <summary>The states of {{name}}: each story is a workbench entry and a test under every driver.</summary>
            public sealed class {{name}}Stories : Stories<{{component}}>
            {
                protected override void Define(StoryCollectionBuilder<{{component}}> stories)
                {
                    stories.Add("Default");

                    // More states, with a play function that runs in bUnit, real browsers and the workbench:
                    // stories.Add("Clicked", s => s.Play(async ctx =>
                    // {
                    //     await ctx.GetByRole("button").ClickAsync();
                    //     await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("Saved");
                    // }));
                }
            }

            """;
    }

    public static string GitHubWorkflow(string project, string test) => $$$"""
        # Blazor UI tests with BlazorTestPlatform: stories in memory (bUnit) and in real browsers, a report, JUnit results,
        # a pull-request summary and SARIF for code scanning. Generated by `btp init`.
        name: Blazor UI tests

        on:
          push:
            branches: [main]
          pull_request:

        permissions:
          contents: read

        jobs:
          test:
            runs-on: ubuntu-latest
            permissions:
              contents: read
              pull-requests: write    # btp report --pr-comment
              security-events: write  # SARIF upload
            # The Playwright image: browsers and fonts match the Linux baselines rendered in it.
            container:
              image: mcr.microsoft.com/playwright/dotnet:v{{{PlaywrightVersion}}}-noble
              options: --ipc=host
            steps:
              - uses: actions/checkout@v4
                with:
                  fetch-depth: 0   # story impact analysis (BTP_CHANGED_SINCE) needs the base branch
              - uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: 10.0.x
              - name: Install btp
                run: |
                  dotnet tool install --global BlazorTestPlatform.Cli
                  echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"
              - name: Test
                run: {{{test}}}
                env:
                  BTP_CHANGED_SINCE: ${{ github.event_name == 'pull_request' && format('origin/{0}', github.base_ref) || '' }}
              - name: Report
                if: always()
                run: >
                  btp report -p {{{project}}} --junit {{{project}}}/TestResults/junit.xml --sarif {{{project}}}/TestResults/btp.sarif
                  --github-summary --pr-comment --fail-on-failures
                env:
                  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
              - uses: actions/upload-artifact@v4
                if: always()
                with:
                  name: btp-results
                  path: {{{project}}}/TestResults/btp
              - uses: github/codeql-action/upload-sarif@v3
                if: always() && hashFiles('{{{project}}}/TestResults/btp.sarif') != ''
                with:
                  sarif_file: {{{project}}}/TestResults/btp.sarif
                  category: blazor-ui-tests

        """;

    public static string AzurePipeline(string project, string test) => $$$"""
        # Blazor UI tests with BlazorTestPlatform on Azure Pipelines. Generated by `btp init`.
        trigger:
          branches:
            include: [main]
        pr:
          branches:
            include: [main]

        pool:
          vmImage: ubuntu-latest

        # The Playwright image: browsers and fonts match the Linux baselines rendered in it.
        container: mcr.microsoft.com/playwright/dotnet:v{{{PlaywrightVersion}}}-noble

        variables:
          ${{ if eq(variables['Build.Reason'], 'PullRequest') }}:
            BTP_CHANGED_SINCE: origin/$(System.PullRequest.TargetBranchName)

        steps:
          - checkout: self
            fetchDepth: 0   # story impact analysis needs the target branch
          - task: UseDotNet@2
            inputs:
              packageType: sdk
              version: 10.0.x
          - script: |
              dotnet tool install --global BlazorTestPlatform.Cli
              echo "##vso[task.prependpath]$HOME/.dotnet/tools"
            displayName: Install btp
          - script: {{{test}}}
            displayName: Test
          - script: >
              btp report -p {{{project}}} --junit $(Build.ArtifactStagingDirectory)/junit.xml
              --markdown $(Build.ArtifactStagingDirectory)/summary.md --sarif $(Build.ArtifactStagingDirectory)/btp.sarif --fail-on-failures
            displayName: Report
            condition: always()
          - task: PublishTestResults@2
            condition: always()
            inputs:
              testResultsFormat: JUnit
              testResultsFiles: $(Build.ArtifactStagingDirectory)/junit.xml
          - task: PublishPipelineArtifact@1
            condition: always()
            inputs:
              targetPath: {{{project}}}/TestResults/btp
              artifact: btp-results

        """;

    public static string GitLabJob(string project, string test) => $$"""
        # Blazor UI tests with BlazorTestPlatform on GitLab CI. Generated by `btp init`; include it from .gitlab-ci.yml:
        #   include:
        #     - local: .gitlab/blazor-ui-tests.gitlab-ci.yml
        blazor-ui-tests:
          # The Playwright image: browsers and fonts match the Linux baselines rendered in it.
          image: mcr.microsoft.com/playwright/dotnet:v{{PlaywrightVersion}}-noble
          variables:
            GIT_DEPTH: 0   # story impact analysis needs the target branch
          rules:
            - if: $CI_PIPELINE_SOURCE == "merge_request_event"
              variables:
                BTP_CHANGED_SINCE: origin/$CI_MERGE_REQUEST_TARGET_BRANCH_NAME
            - if: $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH
          before_script:
            - curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
            - export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
            - dotnet tool install --global BlazorTestPlatform.Cli
          script:
            - {{test}}
          after_script:
            - export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
            - btp report -p {{project}} --junit junit.xml --markdown summary.md --sarif gl-btp.sarif
          artifacts:
            when: always
            paths:
              - {{project}}/TestResults/btp
              - summary.md
            reports:
              junit: junit.xml

        """;
}
