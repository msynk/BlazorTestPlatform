# Test frameworks

Stories run under any test framework: a test receives a story id (or a matrix cell key), runs it with a runner, and
reports the result. The framework packages add the two things that differ between frameworks: data rows with readable
names, categories and skip reasons, and a `Passed` call that reports a skipped run as skipped instead of letting it pass.

| Framework | Package | Data source | Skipped runs are reported as |
|---|---|---|---|
| xUnit v3 | `BlazorTestPlatform.Xunit` | `StoryTheoryData.From(...)` with `[MemberData]` | skipped (`Assert.Skip`) |
| NUnit 4 | `BlazorTestPlatform.NUnit` | `StoryTestCases.From(...)` with `[TestCaseSource]` | ignored (`Assert.Ignore`) |
| MSTest 4 | `BlazorTestPlatform.MSTest` | `StoryDataRows.From(...)` with `[DynamicData]` | inconclusive (`Assert.Inconclusive`) |
| TUnit 1.x | `BlazorTestPlatform.TUnit` | `StoryDataRows.From(...)` with `[MethodDataSource]` | skipped (`Skip.Test`) |

Every `From` overload takes a `StoryCatalog`, any `IEnumerable<Story>` (a filtered catalog, an impact analysis, a shard)
or matrix cells (`IEnumerable<MatrixCase>`, keyed by `MatrixCase.Key`). Rows are named after the story path
(`Forms/Button/Disabled`) or the cell (`Forms/Button/Disabled [desktop, light, server, chromium]`), carry the story's
tags as categories (`Category` traits in xUnit), and stories marked `.Skip("why")` are skipped at discovery.

`Passed(result)` throws `StoryFailedException` (with the end of the step timeline) for a failed result, reports a
skipped result as the framework's skip, and returns passed results. A run is skipped when its driver lacks a capability
the story declares (`Requires(...)`, or browser emulation on bUnit) or when play calls `ctx.Require(...)`.

## xUnit v3

```csharp
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.Xunit;

public sealed class StoryTests
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ButtonStories>();

    public static TheoryData<string> Stories => StoryTheoryData.From(Catalog);

    [Theory]
    [MemberData(nameof(Stories))]
    public async Task Story_passes_in_memory(string id) =>
        StoryTheoryData.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id), cancellationToken: TestContext.Current.CancellationToken));
}
```

Filter by tag with `--filter-trait "Category=smoke"`.

## NUnit

```csharp
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.NUnit;
using NUnit.Framework;

public sealed class StoryTests
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ButtonStories>();

    public static IEnumerable<TestCaseData> Stories => StoryTestCases.From(Catalog);

    [TestCaseSource(nameof(Stories))]
    public async Task Story_passes_in_memory(string id) =>
        StoryTestCases.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id), cancellationToken: TestContext.CurrentContext.CancellationToken));
}
```

## MSTest

```csharp
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class StoryTests
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ButtonStories>();

    public static IEnumerable<TestDataRow<string>> Stories => StoryDataRows.From(Catalog);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DynamicData(nameof(Stories))]
    public async Task Story_passes_in_memory(string id) =>
        StoryDataRows.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id), cancellationToken: TestContext.CancellationToken));
}
```

The MSTest package adds a global `using Microsoft.VisualStudio.TestTools.UnitTesting;` (MSTest's own build files do);
alias `Assert` if the project also uses another framework's assertions.

## TUnit

```csharp
using BlazorTestPlatform.Bunit;
using BlazorTestPlatform.TUnit;

public sealed class StoryTests
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ButtonStories>();

    public static IEnumerable<TestDataRow<string>> Stories() => StoryDataRows.From(Catalog);

    [Test]
    [MethodDataSource(nameof(Stories))]
    public async Task Story_passes_in_memory(string id) =>
        StoryDataRows.Passed(await BunitStoryRunner.RunAsync(Catalog.Get(id)));
}
```

## Other frameworks

`StoryTestCase.From(stories)` and `StoryTestCase.From(cells)` give the framework-neutral description each package maps
(key, story, display name, skip reason, categories). Report a skipped `StoryResult` with the framework's own skip; never
let it pass silently.

## Scaffolding: `btp init`

```
btp init [-p <project>] [-f xunit|nunit|mstest|tunit] [--ci github|azure|gitlab|none] [--component <Type>] [--namespace <ns>] [--force]
```

writes into the test project:

* `btp.json` with the default gates (accessibility failing on `serious`, visual threshold 0.1)
* `StoryTests.cs`: every story as an in-memory test for the chosen framework, as above
* with `--component Contoso.UI.Button`: `ButtonStories.cs`, a first story collection for the component
* with `--ci`: a pipeline for GitHub Actions (`.github/workflows/blazor-ui-tests.yml`), Azure Pipelines
  (`azure-pipelines.blazor-ui-tests.yml`) or GitLab (`.gitlab/blazor-ui-tests.gitlab-ci.yml`) at the repository root,
  running on the Playwright image that matches the Playwright version BlazorTestPlatform uses, with JUnit results,
  SARIF and story impact analysis on pull requests ([CI](ci.md))

Existing files are kept unless `--force` is given. The command prints the `dotnet add package` lines for the packages
the generated code uses; it never edits the project file.
