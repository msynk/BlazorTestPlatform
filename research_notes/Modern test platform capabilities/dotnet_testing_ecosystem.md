# .NET testing ecosystem: tooling and user-facing experiences (2025-2026), with gaps and integration opportunities for BlazorTestPlatform

Research date: 2026-11-10. Most sources are official docs updated between mid-2026 and early November 2026. Version notes: several Microsoft Learn pages already describe .NET 11 RC/preview and even ".NET 12 Preview 1" SDK behaviour, so features are marked with the SDK or MTP version that introduced them. BlazorTestPlatform (BTP) targets .NET 10, so .NET 11+ SDK-only features are flagged as such.

## Q1. What do .NET developers already get in the IDE and CI for running, viewing and analyzing tests, coverage and mutation results, and what is missing compared with JS tools (Storybook, Chromatic, Cypress Cloud, Playwright UI mode)?

### Takeaway
.NET developers now get a strong first-party execution and reporting layer. Microsoft.Testing.Platform (MTP) 2.x ships TRX, HTML, JUnit and CTRF reports, GitHub Actions and Azure DevOps reporters with flaky and slow-test history, retry, crash and hang dumps, OpenTelemetry, coverage and hot reload. `dotnet test` in .NET 10 has a native MTP mode. On top of that sit mature IDE continuous-testing tools (NCrunch, dotCover, VS Live Unit Testing), snapshot testing (Verify), coverage reports (ReportGenerator) and mutation testing (Stryker.NET). What .NET still lacks is a component-centric visual workbench: there is no first-party equivalent of Storybook, Chromatic visual review, Cypress Cloud, or Playwright UI mode for .NET. The closest is the community preview project BlazingStory. bUnit has no real browser, and Verify has no hosted review flow for visual baselines.

### Cited Findings

#### Microsoft.Testing.Platform (MTP) and `dotnet test` in .NET 10
- **`dotnet test` modes:** It has two modes. VSTest mode is the default and was the only mode before .NET 10. MTP mode was introduced in the .NET 10 SDK and runs only MTP test apps. Opt in through `global.json`: `{"test": {"runner": "Microsoft.Testing.Platform"}}`. From .NET 11 Preview 6 the `DOTNET_TEST_RUNNER` environment variable also works. MTP mode needs MTP 1.7 or later. — [Testing with dotnet test](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- **Migration from the old VSTest-mode bridge (`TestingPlatformDotnetTestSupport`):**
  - Remove the property, and also `TestingPlatformCaptureOutput` and `TestingPlatformShowTestsFailure`.
  - The extra `--` becomes optional.
  - `dotnet test X.sln` becomes `--solution X.sln`, `X.csproj` becomes `--project`, and a dll becomes `--test-modules`, which supports globbing.
  - Running MTP under VSTest mode is "legacy", and that support "will be removed in MTP version 2 if run with .NET 10 SDK".
  - — [Testing with dotnet test](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- **Mixed solutions:** Solutions mixing frameworks or extensions fail with exit code 5 when an option is unrecognized. For example, xUnit uses `--filter-trait` while MSTest uses `--filter`. Microsoft's guidance is to route arguments per project using conditional `TestingPlatformCommandLineArguments` in `Directory.Build.props`. — [Testing with dotnet test](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
- **`dotnet test` MTP-mode options:**
  - `--max-parallel-test-modules` (default: processor count), `--results-directory`, `--results-directory-layout flat|per-module` (.NET 11 RC1 and SDK 10.0.402+), `--minimum-expected-tests` (exit code 9).
  - `--maximum-failed-tests` (exit 13) and `--timeout` (exit 3), both .NET 11 Preview 7 with MTP 2.4.
  - `--list-tests [text|json]`. JSON (.NET 11 Preview 7) groups tests by assembly, TFM and architecture with source locations and traits.
  - `--no-artifact-post-processing`, `--device`/`--list-devices` for mobile, and file-based test apps (.NET 12 Preview 1).
  - .NET 11 Preview 6 adds an ANSI live view of currently running tests with per-assembly counts.
  - — [dotnet test (MTP)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- **Default results directory:** With SDK 10.0.200+ and MTP 2.3.1+, the default is `TestResults` beneath the invocation directory. Previously it was per test app output. — [dotnet test (MTP)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- **Experimental test impact analysis in `dotnet test`:** `--collect-test-map` and `--affected-tests` arrived in .NET 11 RC1. They need a separately distributed MTP extension and `DOTNET_CLI_ENABLE_AFFECTED_TESTS=1`. They are incompatible with device testing, parallel test modules and minimum-test policies. — [dotnet test (MTP)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp); [dotnet/sdk PR #55574](https://github.com/dotnet/sdk/pull/55574). The PR says repository analysis and test-map storage live in the external extension, and the SDK only orchestrates.
- **Extension model:** Extensions auto-register when their NuGet package is installed, through Microsoft.Testing.Platform.MSBuild. Experimental APIs carry the `TPEXP` diagnostic. There is a built-in JSON-RPC "server mode" for IDE integration, and dynamic extension loading from JSON manifests. — [MTP features](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-features)
- **Built-in terminal reporter:**
  - Output modes are `--output Minimal|Normal|Detailed` (Minimal needs MTP 2.4).
  - Other options: `--show-test-results passed,failed,...` (2.4), `--show-stdout`/`--show-stderr All|Failed|None` (2.2.1), `--show-flaky-tests` (2.4) and `--show-slowest-tests N` (2.4).
  - The ANSI progress bar redraws every 500 ms. Non-ANSI output prints every 3 s.
  - From MTP 2.3, MTP detects LLM or AI-tool environments, suppresses the banner and defaults stdout/stderr display to `Failed`.
  - — [MTP terminal output](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-terminal-output)
- **MTP report extensions** — all from [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports):
  - **TRX** (`Microsoft.Testing.Extensions.TrxReport`, `--report-trx`):
    - Since MTP 2.3, TRX streams to disk, so it survives host crashes, and the default name is the deterministic `{asm}_{tfm}_{arch}.trx`.
    - The extension adds `merge-trx` and `ms-trxcompare` commands (version 2.5.1).
    - From 2.4 it preserves `[WorkItem]`/`[GitHubWorkItem]`.
  - **HTML** (`Microsoft.Testing.Extensions.HtmlReport`, `--report-html`): an "interactive, self-contained HTML file". Experimental, MTP 2.3.0.
  - **JUnit** (`Microsoft.Testing.Extensions.JUnitReport`, `--report-junit`): experimental, MTP 2.3.0.
  - **CTRF** (`Microsoft.Testing.Extensions.CtrfReport`, `--report-ctrf`): Common Test Report Format JSON. Retried tests get `retryAttempts`, `retries`, and `flaky: true` when a later attempt passes. From MTP 2.5 preview, `testId` derives from the full MTP test node UID (SHA-256 for long UIDs), each execution has an `executionId`, and each attempt has an `attemptId`.
  - **File-name placeholders** (MTP 2.3): `{asm}`, `{tfm}`, `{arch}`, `{pname}`, `{pid}`, `{time}`.
  - **Report consolidation** (MTP 2.4, experimental): TRX, JUnit, CTRF and HTML are merged into a `merged/` folder across modules and retries through the `IArtifactPostProcessor` API.
- **Azure DevOps reporter** (`Microsoft.Testing.Extensions.AzureDevOpsReport`) — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports):
  - Basic options: `--report-azdo` and `--report-azdo-severity` (1.9).
  - From 2.3:
    - Flakiness history and quarantine: `--report-azdo-flaky-history <1-90 days>`, `--report-azdo-demote-known-flaky` (default threshold 25%) and `--report-azdo-quarantine-file`.
    - Slow-test history: `--report-azdo-slow-test-history`, where the threshold is the lower of 60 s and p99 × multiplier (default 3, minimum 10 samples).
    - Summary and uploads: `--report-azdo-summary` (Markdown job summary), `--report-azdo-upload-artifacts` and `--publish-azdo-test-results` (live streaming to a test run, with a run URL printed).
  - From 2.4, summaries aggregate all modules and include coverage. Failed-result attachments are uploaded, and retries are published as sub-results.
  - From 2.5 preview, summaries add pass rates, flaky history, duration comparisons and "test dependency edges".
  - When code is on GitHub but CI runs on Azure Pipelines, failure annotations appear in the GitHub pull request.
- **GitHub Actions reporter** (`Microsoft.Testing.Extensions.GitHubActionsReport`, `--report-gh`, MTP 2.3; stable entry points in 2.4) — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports):
  - Per-assembly log groups.
  - Failed and skipped annotations, which show on the PR "Files changed" diff.
  - A Markdown job summary written to `GITHUB_STEP_SUMMARY`. Options: `--report-gh-step-summary on|off|on-failure` and `--report-gh-step-summary-sections test-results|slow-tests|coverage|all`.
  - Slow-test notices (default `60s`).
  - `--report-gh-history <file>` with `--report-gh-history-window` (1-90 days, default 30). It keeps a local history snapshot that the workflow must download and upload as an artifact.
  - Coverage counts and thresholds appear in the summary.
- **Retry** (`Microsoft.Testing.Extensions.Retry`, restrictive "MTP Tools" license) — [MTP retry](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-retry):
  - Options: `--retry-failed-tests N`, `--retry-failed-tests-max-percentage`, `--retry-failed-tests-max-tests` and `--retry-failed-tests-delay` (2.3).
  - Each retry starts a new process filtered with `--filter-uid`, so stable test node UIDs are required.
  - From 2.4 the summary shows separate "flaky" and "retried" counts and lists the recovered tests.
  - Artifacts for each attempt go under `Retries/`.
  - Not supported in the browser or in hot reload mode.
- **Crash and hang dumps:**
  - `--crashdump` with `--crashdump-type Mini|Heap|Triage|Full`.
  - `--crash-report`, which writes a JSON crash report on Linux and macOS (2.3).
  - `--crash-sequence` (2.3, default on), which writes a sequence file listing tests that started and ended so you can find the test that crashed.
  - `--hangdump` with `--hangdump-timeout` (default 30m) and `--hangdump-type`.
  - — [MTP crash/hang dumps](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-crash-hang-dumps)
- **Hot Reload** (`Microsoft.Testing.Extensions.HotReload`, restrictive license): enabled with `TESTINGPLATFORM_HOTRELOAD_ENABLED=1`. It supports "console mode" only, with "no support for hot reload in Test Explorer for Visual Studio or Visual Studio Code". — [MTP Hot Reload](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-hot-reload)
- **Other extensions:** experimental Diagnostics with a screen video recorder (MTP 2.3.0), AI chat-client integration (experimental), Microsoft.Extensions hosting and logging integration, and test-host deployment for packaged, UWP and AppContainer apps. — [MTP features](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-features)

#### Coverage
- **Microsoft code coverage** (`Microsoft.Testing.Extensions.CodeCoverage`):
  - Options: `--coverage`, `--coverage-output-format coverage|xml|cobertura` and `--coverage-settings`.
  - `IncludeTestAssembly` now defaults to false.
  - The license is closed-source but free.
  - With the `flat` layout, apps that share an output path overwrite each other "without a warning".
  - — [MTP code coverage](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)
- **First-class coverage result model (MTP 2.4):** Collectors publish `TestCoverageMessage`, `TestCoverageThresholdMessage` and `TestCoverageReportMessage`. The terminal and reporters render percentages without parsing collector files. A failed threshold exits with code 14. — [MTP code coverage](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)
- **Coverlet for MTP** (`coverlet.MTP`): a native MTP extension.
  - Enable it with `--coverlet`.
  - Output formats via `--coverlet-output-format json|lcov|opencover|cobertura|teamcity`.
  - Filters: `--coverlet-include`/`--coverlet-exclude`, `--coverlet-exclude-by-file` and `--coverlet-exclude-by-attribute`.
  - The default output is `coverage.json`.
  - — [MTP code coverage](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)
- **ReportGenerator:**
  - Inputs: OpenCover, Cobertura, dotCover, Visual Studio, lcov, JaCoCo, Clover, gcov and others.
  - Outputs: Html (plus Light, Dark and BlueRed themes), HtmlInline_AzurePipelines, HtmlChart, HtmlSummary, Badges (SVG), MarkdownSummaryGithub, MarkdownDeltaSummary, TextDeltaSummary, JsonSummary, SonarQube, TeamCitySummary, lcov, CodeClimate and others.
  - `-historydir` builds history and trend charts.
  - Risk hotspots use thresholds on cyclomatic complexity, CRAP score and NPath complexity.
  - Minimum thresholds can be set for line, branch, method and full-method coverage.
  - Ships as a dotnet global tool, an Azure DevOps extension, a GitHub Action and an MSBuild task.
  - Some features are PRO or sponsor-only.
  - — [ReportGenerator README](https://github.com/danielpalme/ReportGenerator)

#### Mutation testing (Stryker.NET)
- **Reporters:** `html` and `progress` are on by default. Others: `dashboard` (uploads JSON to Stryker Dashboard), `json`, `cleartext`, `cleartexttree`, `dots` (CI-friendly `.`/`S`/`T`), `markdown` (`mutation-report.md` with a per-file table and score) and `gitlab` (Code Quality JSON). — [Stryker.NET reporters](https://stryker-mutator.io/docs/stryker-net/reporters/)
- **Configuration:**
  - `--open-report` opens the html or dashboard report "and keeps them updated during the run".
  - Thresholds: high 80, low 60 and break 0 by default. Below break, the exit code is 1.
  - `--since:<committish>` runs incrementally on changed code. `--with-baseline` stores the baseline on Disk, Dashboard, Azure File Storage or S3.
  - `coverage-analysis perTest` is the default.
  - `test-runner: vstest` (default) or `mtp`, where "MTP test runner is currently in preview". TUnit and xUnit v3 filtering is done by Stryker itself.
  - — [Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)
- **Mutant states and score:**
  - States: Pending, Killed, Survived, NoCoverage, Timeout, RuntimeError, CompileError and Ignored.
  - Detected = killed + timeout. Undetected = survived + no coverage.
  - Mutation score = detected / valid × 100. There is also a "score based on covered code".
  - The report web components and JSON schema are shared across Stryker variants in the `mutation-testing-elements` monorepo, which offers "a schema for mutation testing results with the web components to visualise it".
  - — [Mutant states and metrics](https://stryker-mutator.io/docs/mutation-testing-elements/mutant-states-and-metrics/); [mutation-testing-elements](https://github.com/stryker-mutator/mutation-testing-elements)

#### IDE experiences
- **Visual Studio Live Unit Testing:** runs impacted tests automatically as you edit. It "graphically depicts code coverage in real time", showing how many tests cover each line and which lines are uncovered. It is Visual Studio Enterprise only. — [Live Unit Testing intro](https://learn.microsoft.com/en-us/visualstudio/test/live-unit-testing-intro)
- **VS Test Explorer and MTP:** VS integrates with MTP through a JSON-RPC protocol. Sources disagree on the minimum VS version for MTP support (17.10 preview, 17.12, or 17.14 are each cited on different Microsoft pages). — [MTP vs VSTest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-platform-vs-vstest), search snippet
- **NCrunch** (v5.24, released 9 Nov 2026):
  - Continuous concurrent testing in VS and Rider.
  - Inline coverage markers that update in real time.
  - Per-line timing with "yellow hot spots" on markers.
  - Runtime data inspection, which records variables, return values and control flow without a debugger.
  - IL-based impact detection and test prioritisation.
  - Distributed processing on grid or cloud nodes.
  - Engine modes, and an MCP server so AI agents can run tests and analyse metrics.
  - — [NCrunch home](https://www.ncrunch.net/)
- **dotCover / ReSharper continuous testing:**
  - Impacted tests run in the background, triggered on save, build or shortcut.
  - Modes include "Cover New and Affected Tests".
  - A separate Continuous Testing session, editor coverage highlighting, and a status-bar toggle.
  - — [dotCover continuous testing](https://www.jetbrains.com/help/dotcover/Work_with_Continuous_Testing.html)
- **Rider:**
  - Frameworks: NUnit, MSTest, xUnit, and "custom VSTest and Microsoft.Testing.Platform frameworks, such as TUnit" (opt-in setting). bUnit is explicitly listed. Coverage of Testing Platform tests needs a dotCover NuGet package.
  - Unit test sessions with "Rerun Failed Tests" and "Run Tests Until Failure".
  - Coverage, profiling and dotMemory Unit.
  - — [Rider unit testing](https://www.jetbrains.com/help/rider/Unit_Testing__Index.html)

#### Component and browser testing
- **bUnit v2:**
  - `bunit.core` and `bunit.web` merged into a single `bunit` package.
  - `TestContext` became `BunitContext`, which now implements `IAsyncDisposable`. `Fake*` became `Bunit*`, for example `BunitNavigationManager` and `BunitJSRuntime`.
  - `RenderComponent`/`Render`/`SetParametersAndRender` are unified into `Render<T>()`. `IRenderedFragment` is removed in favour of `IRenderedComponent<T>`.
  - `DisposeComponents` became `DisposeComponentsAsync`. `ComponentParameterFactory` and `IRefreshableElementCollection` are removed.
  - — [bUnit 1→2 migration](https://bunit.dev/docs/migrations/1to2.html)
- **bUnit 2.x releases** (dates shown without a year on the releases page, likely 2026):
  - 2.5.3 added `FindByTestId`, 2.6.2 added .NET 11 support, and 2.7.2 added `InvokeConstructorAsync` for .NET 10+.
  - 2.8.6 added async `WaitFor` overloads and `AddAsset` for `ComponentBase.Assets`.
  - Latest is 2.14.0 (06 Nov), on AngleSharp 1.10.
  - — [bUnit releases](https://github.com/bUnit-dev/bUnit/releases)
- **bUnit render modes:**
  - Set the mode with `SetAssignedRenderMode(IComponentRenderMode?)` or `@rendermode` in .razor tests. Values: `RenderMode.InteractiveServer`, `InteractiveWebAssembly` and `InteractiveAuto`. Null means static.
  - `SetRendererInfo(RendererInfo?)` controls `ComponentBase.RendererInfo`. The renderer throws if a component reads `RendererInfo` without one set.
  - The docs give no examples for WebAssembly or Auto.
  - bUnit emulates these flags. It does not run real SSR, prerendering or WebAssembly.
  - — [bUnit render modes](https://bunit.dev/docs/interaction/render-modes.html)
- **Verify:**
  - Each run writes `*.received.*`, which is compared to the committed `*.verified.*`.
  - When there is a difference, DiffEngine launches a diff tool. It supports 27 tools, including VS, VS Code, Rider, Beyond Compare, WinMerge, Meld, P4Merge, Kaleidoscope and Cursor.
  - Snapshots can be accepted through DiffEngineTray, ReSharper or Rider plugins, a clipboard command, the Verify.Terminal tool, or a rename.
  - Snapshot files are nested in the IDE, and Blazor is supported.
  - Frameworks: xUnit v3, NUnit, TUnit, MSTest (with `[UsesVerify]`), Expecto and Fixie. Requires net462 through net10 and SDK 9.0.301+.
  - Extensions: Verify.Bunit; Verify.HeadlessBrowsers (Playwright, Puppeteer Sharp, Selenium); Verify.AngleSharp; and image comparers ImageMagick, ImageSharp.Compare, ImageHash, Phash and ssimulacra2.
  - Licensing: SponsorCheck from v33. An Open Source Maintenance Fee applies to revenue-generating organisations for versions released after 1 Sept 2026.
  - — [Verify README](https://github.com/VerifyTests/Verify); [DiffEngine](https://github.com/VerifyTests/DiffEngine)
- **DiffEngine behaviour:** `BuildServerDetector` recognises GitHub Actions, Azure DevOps, Jenkins, GitLab, TeamCity and others. Diff launch can be disabled with `DiffEngine_Disabled`. DiffEngine also detects AI CLIs, such as Claude Code, Aider and Copilot CLI, and does not launch diffs for them. — [DiffEngine](https://github.com/VerifyTests/DiffEngine)
- **Verify.Bunit:** `VerifyBunit.Initialize()` in a module initializer. It writes `.verified.html` (markup) and `.verified.txt` (component fields, parameters and node count). Sample scrubbers remove `<!--!-->` Blazor comments and the `dotnet.*.js` integrity line. — [Verify.Bunit](https://github.com/VerifyTests/Verify.Bunit)
- **Playwright for .NET:**
  - Packages: `Microsoft.Playwright.NUnit`, `.MSTest`, `.Xunit` and `.Xunit.v3`.
  - Base classes: `PageTest`, `ContextTest`, `BrowserTest` and `PlaywrightTest`.
  - Configuration through runsettings: `Playwright.BrowserName`, `Playwright.LaunchOptions.Headless` and `Playwright.ExpectTimeout`.
  - Parallelism follows the framework (NUnit by file, MSTest by class, xUnit by class).
  - — [Playwright .NET test runners](https://playwright.dev/dotnet/docs/test-runners)
- **Playwright .NET traces:** Recording is manual: `Context.Tracing.StartAsync({Screenshots, Snapshots, Sources})` in setup and `StopAsync({Path})` in teardown. View traces with `pwsh bin/Debug/netX/playwright.ps1 show-trace` or trace.playwright.dev. The .NET docs mention no UI mode. — [Playwright .NET trace viewer](https://playwright.dev/dotnet/docs/trace-viewer-intro)
- **TUnit:**
  - Source-generated discovery, Native AOT and trimming support, built on MTP.
  - Parallel by default, with `[DependsOn]`, `[NotInParallel]` and `[ParallelLimiter<T>]`.
  - Data sources: `[Arguments]`, `[MethodDataSource]`, `[MatrixDataSource]` and `[ClassDataSource<T>]` with scopes.
  - Hooks at Test, Class, Assembly and TestSession scope. `[Retry]`, `[Repeat]` and `[Timeout]`.
  - Async fluent assertions, Roslyn analyzers and `TUnit.Mocks` (source-generated).
  - Integrations: `TUnit.AspNetCore`, `TUnit.Aspire` (with OTel capture), `TUnit.Playwright` and `TUnit.OpenTelemetry`.
  - IDEs: VS 17.13+, Rider with Testing Platform support enabled, VS Code with C# Dev Kit.
  - Self-reported benchmarks: about 327 ms for TUnit against 565-682 ms for the others on data-driven tests, and about 18 ms under AOT, at TUnit 1.81.10.
  - The README mentions no HTML report or dashboard.
  - — [TUnit README](https://github.com/thomhurst/TUnit); [TUnit docs intro](https://tunit.dev/docs/intro)
- **BlazingStory** (closest Storybook analogue for Blazor):
  - A community "Storybook clone" built almost entirely in Blazor, with no npm.
  - Stories are `.stories.razor` files in a separate app project, and it ships project templates.
  - Includes "integrated visual regression testing using Playwright".
  - Offers an MCP server package so AI assistants can query the UI catalog.
  - Still in `1.0.0-preview.*` versions.
  - Not an official Storybook product; Storybook add-ons and services such as Chromatic are unavailable.
  - — [BlazingStory DEV article](https://dev.to/j_sakamoto/storybook-doesnt-support-blazor-so-i-reimplemented-it-in-c-411p); [BlazingStory NuGet](https://www.nuget.org/packages/BlazingStory/); [BlazingStory.McpServer](https://nuget.org/packages/blazingstory.mcpserver)

#### CI platforms
- **Azure DevOps flaky test management (Services only):**
  - System detection reruns failed tests within the VSTest task or with "rerun failed jobs". A test that fails then passes is marked flaky for every pipeline on that branch.
  - Custom detection posts flakiness through the Result Meta Data Update REST API.
  - Teams can mark or unmark flaky tests manually. Flaky failures can be excluded from pass percentage and shown as "Tests not reported".
  - Switching detection system erases history.
  - The summary update works only with the VSTest and Publish Test Results tasks.
  - — [ADO flaky tests](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/flaky-test-management)
- **Azure DevOps Test Analytics:**
  - A top failing tests report with pass rate and outcome distribution. The default window is 14 days.
  - A distinct failing-test count and a daily trend chart of failures against average pass rate.
  - Group by test file and other dimensions. Drill into one test for a stacked daily outcome chart and a list of every instance.
  - Data comes from published test results.
  - — [ADO Test Analytics](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/test-analytics)
- **Azure Pipelines Test Impact Analysis:**
  - Works only through VSTest task v2. It selects impacted, previously failing and new tests, and falls back to running all tests for unknown file types such as HTML or CSS.
  - Configuration: `TIA_IncludePathFilters`, custom XML dependency maps (`TIA.UserMapFile`) and `DisableTestImpactAnalysis`.
  - **Not supported**: .NET Core, data-driven tests, multi-machine topologies, and adapter-specific parallelism. The doc dates from 2018.
  - — [ADO TIA](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/test-impact-analysis)
- **dorny/test-reporter (GitHub Actions):**
  - .NET formats are `dotnet-trx` and `dotnet-nunit`. Also supports JUnit, Jest, Mocha and others.
  - Produces a Check Run or job summary with up to 50 annotations.
  - Limitations: Markdown only with no HTML or screenshots ("upload as artifacts"), a 65,535-byte cap, and forked PRs need a two-workflow `workflow_run` pattern.
  - Examples use `@v3`.
  - — [dorny/test-reporter](https://github.com/dorny/test-reporter)

### Inferences
- First-party MTP now covers much of what BTP lists as its own features: an HTML report, JUnit, flaky detection through retry and CTRF `flaky: true`, a history file for GitHub Actions, Azure DevOps flaky and slow history, and OpenTelemetry. BTP's value therefore has to be component- and story-centric, layered on top of MTP rather than replacing it. That means:
  - visual diffs;
  - a matrix across render modes and browsers;
  - axe violations tied to a story;
  - render-performance budgets;
  - a catalog-plus-results workbench.
- The biggest experience gap against JS is a Storybook-style catalog joined with test status, a Chromatic-style hosted visual-review and approval flow, and a Playwright UI mode-style watch, time-travel and pick-locator loop. BlazingStory covers part of the catalog (preview, community), and Verify plus DiffEngine covers local snapshot approval. Nothing in .NET unites catalog, bUnit, real-browser runs, visual and a11y results, and history in one UI.
- bUnit cannot verify real render-mode behaviour such as prerender then interactive handoff, streaming SSR or WebAssembly loading. It only fakes `RendererInfo` and `AssignedRenderMode`. BTP's combination of bUnit and Playwright across real render modes addresses a documented blind spot.
- Test impact analysis for modern .NET is underserved. Azure DevOps TIA excludes .NET Core. NCrunch and dotCover offer it only inside the IDE. The SDK's `--affected-tests` is experimental, .NET 11 RC1+, and needs an external extension. BTP's TIA for .NET 10 Blazor projects, ideally Razor-aware (`.razor`, `.css` and `.js` changes that ADO TIA cannot reason about), fills a real gap.
- Continuous-testing users expect inline per-line feedback in the editor (NCrunch, Live Unit Testing, dotCover). A web dashboard is complementary; IDE-level glyphs are table stakes for continuous testing.
- AI-agent integration is now normal in .NET testing tools: NCrunch's MCP server, BlazingStory's MCP server, MTP's AI-environment detection and DiffEngine's AI-CLI detection. An MCP server or agent-friendly JSON output for `btp` would match the trend.

### Gaps
- Visual Studio 2026 Test Explorer specifics: new features, MTP version support, and whether hot reload for tests or Live Unit Testing support MTP. No authoritative 2026 release-notes source was found; the VS-version minimums conflict across Microsoft pages.
- Whether Playwright for .NET has gained a UI mode or an HTML reporter: none is mentioned in the .NET docs fetched, but absence was not confirmed against release notes.
- Azure Test Plans (manual or exploratory testing) was not researched in depth.
- What the MTP HTML report (`--report-html`) actually looks like (layout, filtering, history) is not documented beyond "interactive, self-contained".
- The experimental `--affected-tests` extension's name, distribution and granularity (file or method) were not found in official docs.
- Year attribution for bUnit release dates (assumed 2026).

## Q2. Which UX patterns from ReportGenerator, Stryker's mutation report, the Aspire dashboard and Azure DevOps test analytics are worth emulating (risk hotspots, history charts, trace waterfalls, structured logs)?

### Takeaway
Four patterns recur across these tools and are worth copying:
- drill-down from aggregate score to file to line, with colour-coded state (ReportGenerator, Stryker);
- history and trend views with "top offenders" ranking (ReportGenerator `-historydir`, ADO Test Analytics, MTP flaky and slow history);
- risk scoring that blends complexity and coverage (CRAP, NPath);
- the Aspire dashboard's tightly cross-linked resources, logs, traces and metrics views, with span waterfalls, exemplars, per-page pause, keyboard navigation, export and import, and run history.

### Cited Findings
- **ReportGenerator:** a risk hotspots view using cyclomatic complexity, CRAP score and NPath thresholds, plus coverage history charts (`-historydir`). It also offers delta summaries (`MarkdownDeltaSummary`, `TextDeltaSummary`) for PR diffs, SVG badges, themed HTML (Light, Dark, BlueRed), an Azure Pipelines inline HTML variant, and line, branch and method thresholds. — [ReportGenerator README](https://github.com/danielpalme/ReportGenerator)
- **Stryker:** a single HTML report that "visually shows your project and all mutations" and stays readable with many mutations. `--open-report` keeps the browser report updated live during the run. Dashboard upload gives badges and baselines. The markdown reporter gives a per-file table with states as columns (Killed, Survived, Timeout, No Coverage, Ignored, Compile Errors) and the overall score. — [Stryker reporters](https://stryker-mutator.io/docs/stryker-net/reporters/); [Stryker config](https://stryker-mutator.io/docs/stryker-net/configuration/)
- **Stryker metrics:** Stryker defines clear metrics (detected, undetected, valid, invalid, score, and score based on covered code), with explicit exclusion rules for compile and runtime errors and ignored mutants. Mutants still appear in reports even when excluded. — [Mutant states and metrics](https://stryker-mutator.io/docs/mutation-testing-elements/mutant-states-and-metrics/)
- **Aspire dashboard** (Aspire CLI 13.6.0, Sept 29 2026; dashboard compiled Native AOT on Fluent UI v5) — all from [Aspire dashboard explore](https://aspire.dev/dashboard/explore/):
  - **Resources:** a table view (Type, Name, State, Start time, Source, Endpoints, Logs, Actions) and a dependency graph view with zoom. Each row has an error-count badge that deep-links to structured logs filtered to errors.
  - **Structured logs:** a Level dropdown and an advanced filter on any property, a details pane, and trace links.
  - **Traces:**
    - A list with a "radial icon" comparing each duration with the others.
    - A detail view with summary stats (duration, resources, depth, total spans) and per-resource colours.
    - Error icons on the trace and on the span.
    - A span details pane with events, a span filter that keeps context, and "View Logs" filtered to the trace.
  - **Metrics:** charts with a table toggle and dimension filters. Exemplars appear as dots that open the linked trace.
  - **Telemetry controls:**
    - Each page can be paused on its own.
    - Data can be removed for one resource or all.
    - Export creates a timestamped zip. Import accepts zip or JSON up to 100 MB.
    - A Runs selector keeps 10 unpinned past runs read-only. Pinned runs are exempt.
  - **Other UX:**
    - A notification centre.
    - A text visualizer with JSON and XML formatting.
    - Keyboard shortcuts: R/C/S/T/M for pages, ? for help, and Shift+R/T/X for panels.
    - Theme options System, Light and Dark, and a time-format setting.
- **Azure DevOps Test Analytics:** a pass rate plus outcome distribution, the distinct failing-test count against total failures (example: "986 test failures originated from 124 tests"), and a daily trend chart. Results can be grouped by test file. A drill-down shows a stacked daily outcome chart and a list of every instance, so you can "infer hidden patterns" such as time-clustered failures. — [ADO Test Analytics](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/test-analytics)
- **Azure DevOps flaky workflow:** detection, marking, reporting (the "Tests not reported" bucket), resolution and close-the-loop reset. "Marked flaky" tags apply on future runs only. — [ADO flaky tests](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/flaky-test-management)
- **MTP Azure DevOps and GitHub summaries:**
  - Slow-test thresholds derived from historical p99 × multiplier.
  - Flakiness demotion above a 25% threshold.
  - A quarantine file of names or glob patterns.
  - Bounded failure details with explicit truncation notices.
  - Coverage counts and thresholds in the job summary.
  - — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports)
- **NCrunch:** per-line coverage markers with "yellow hot spots" for slow lines, and runtime data inspection in place of the debugger. — [NCrunch](https://www.ncrunch.net/)

### Inferences
- **Story risk hotspots:** BTP could rank stories and components ReportGenerator-style, by CRAP-like signals: low coverage × high complexity, plus flake rate, a11y violations, perf budget headroom and visual-diff churn.
- **Per-test-run waterfall:** BTP's OpenTelemetry data could drive an Aspire-style span view for each story run: render, then lifecycle methods, JS interop, network, axe scan and screenshot. Spans would be colour-coded by phase, with error icons and "view logs for this run" links. MSTest 4.5 with MTP 2.5 already makes the test activity the parent of HttpClient, SQL and EF spans, which proves the pattern.
- **History UX:** emulate ADO's "N failures from M tests" headline, a daily stacked outcome chart per story, group by component or file, and the flaky-lifecycle states (detected, marked, quarantined, resolved). Use Aspire's pinned versus rolling run history.
- **Report the mutation-score way:** for visual and a11y results, show an explicit metric algebra (for example, a11y score = passed rules / applicable rules, with "excluded" rules still shown), modelled on Stryker's detected and undetected definitions. That builds credibility.
- **Live-updating local report:** the equivalent of Stryker `--open-report` for `btp` runs, so a browser page updates as stories complete.
- **Keyboard-first navigation, dark mode, export and import of a run bundle (zip):** these are low-cost polish items that .NET developers have now seen in Aspire.

### Gaps
- No documentation was found of the Stryker HTML report's in-page UX: file tree, code view, mutant popovers, state filters, test view. The repo README fetched was mostly metadata.
- No primary source described ReportGenerator's PRO-only features or exact risk hotspot thresholds and defaults.
- No visual or design documentation of the MTP `--report-html` output.

## Q3. Which standard formats and integration points matter (TRX, JUnit, Cobertura, OpenTelemetry, SARIF, MTP extensions, `dotnet test` with MTP in .NET 10)?

### Takeaway
For a .NET 10 platform on MTP, the integration points to support are:
- **Run inside the platform:** be an MTP extension (or a set of them) that `dotnet test` in MTP mode can drive.
- **Write the standard result files:** TRX for Azure DevOps and Visual Studio, JUnit for generic CI, and CTRF, now first-party with retry and flaky semantics.
- **Coverage:** Cobertura (and lcov or OpenCover through coverlet.MTP) for ReportGenerator and CI, plus MTP 2.4's first-party coverage messages.
- **Telemetry:** OpenTelemetry using MTP's `Microsoft.Testing.Platform` activity source and its test semantic conventions.
- **Native CI integration:** GitHub Actions and Azure DevOps annotations and job summaries.
- **Mutation results:** Stryker's mutation-testing-elements JSON schema.

SARIF was not observed in any of the .NET testing tools researched, so BTP's SARIF output is a differentiator for code-scanning surfaces, not an ecosystem expectation.

### Cited Findings
- **MTP report packages and file naming:** `--report-trx`, `--report-junit`, `--report-ctrf` and `--report-html` each require their own package. `--results-directory` is shared. File names support placeholders. `IArtifactPostProcessor` (experimental) lets custom report extensions merge across modules (`TestModules` mode) and retries (`RetryAttempts` mode). — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports)
- **Framework authors:** implement `ITrxReportCapability` to supply class, category, failure and output metadata to TRX. — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports)
- **OpenTelemetry extension** (`Microsoft.Testing.Extensions.OpenTelemetry`, from MTP 2.1, non-experimental from 2.4) — all from [MTP OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-open-telemetry):
  - **Telemetry names:** the activity source and meter are both named `Microsoft.Testing.Platform`.
  - **Semantic-convention attributes:** `test.case.name`, `test.case.result.status`, `test.suite.name`, `code.function.name`, `code.file.path`, `code.line.number`, `code.stacktrace` and `error.type`. Failed spans set status Error with an exception event, and there are extra states: skipped, error, timeout, cancelled and unknown.
  - **Metrics:** `test.case.duration` (histogram, s), `test.case.result.count`, `test.case.active`, `test.run.duration` and `test.case.retry.count`.
  - **Trace context:** honours `TRACEPARENT`/`TRACESTATE`, so a test run nests under a CI or parent trace.
  - **Configuration:** `AddOpenTelemetryProviderFromEnvironment()` reads `OTEL_*` variables and the OTLP exporter. `TESTINGPLATFORM_OTEL_CAPTURE_TEST_OUTPUT` (default 1) attaches stdout to spans.
  - **Resource detection:** `AddTestingPlatformResource` detects CI provider, pipeline, branch and commit for GitHub Actions, Azure Pipelines, GitLab and Jenkins.
  - **Host-owned providers** (MTP 2.5 preview): `AddTestingPlatformDiagnostics` and `<TestingPlatformOpenTelemetryMode>HostOwned</...>` let Aspire ServiceDefaults own the providers.
- **Coverage formats:** Microsoft CodeCoverage outputs `coverage`, `xml` or `cobertura`. coverlet.MTP outputs `json`, `lcov`, `opencover`, `cobertura` or `teamcity`. MTP 2.4 adds `ITestCoverageCapabilities` and `ITestCoverageResult` services, and exit code 14 on a threshold failure. — [MTP code coverage](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)
- **Coverage inside the Azure DevOps reporter:** the reporter uploads run-level `.coverage`, `.cobertura.xml` and `.opencover.xml` as coverage attachments. GitHub and Azure DevOps summaries include coverage when available. — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports)
- **ReportGenerator:** accepts Cobertura, OpenCover, lcov, dotCover and others. It outputs SonarQube, Cobertura, lcov, JsonSummary, MarkdownSummaryGithub, badges and more. — [ReportGenerator README](https://github.com/danielpalme/ReportGenerator)
- **dorny/test-reporter:** consumes `dotnet-trx` and `dotnet-nunit`, plus JUnit for other stacks. — [dorny/test-reporter](https://github.com/dorny/test-reporter)
- **Azure DevOps analytics and flaky detection:** both depend on published test results through the Publish Test Results task. Flaky metadata can be posted through the REST API (Result Meta Data - Update), which gives an integration hook for external flake detectors such as BTP's flake history. — [ADO flaky tests](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/flaky-test-management); [ADO Test Analytics](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/test-analytics)
- **Stryker:** JSON and dashboard formats follow the shared `mutation-testing-report-schema`. A GitLab Code Quality JSON reporter also exists. — [mutation-testing-elements](https://github.com/stryker-mutator/mutation-testing-elements); [Stryker reporters](https://stryker-mutator.io/docs/stryker-net/reporters/)
- **MTP server mode:** a JSON-RPC protocol that IDEs (VS Test Explorer, Rider, VS Code C# Dev Kit) use to discover and run tests. TUnit relies on it in Rider ("Testing Platform support") and VS Code ("Use Testing Platform Protocol"). — [MTP features](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-features); [TUnit README](https://github.com/thomhurst/TUnit)
- **Machine-readable discovery:** `dotnet test --list-tests json` (.NET 11 Preview 7) emits a versioned JSON document of tests with source locations and traits. — [dotnet test (MTP)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- **Exit codes BTP should respect:** 3 (aborted or timeout), 5 (invalid arguments), 8 (zero tests), 9 (minimum expected tests), 13 (max failed tests) and 14 (coverage threshold). — [dotnet test (MTP)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp); [MTP code coverage](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)

### Inferences
- **Recommended format set:** TRX (Azure DevOps and Visual Studio), JUnit (generic CI), CTRF (modern JSON with retry and flaky semantics), Cobertura (coverage), OTel (traces and metrics using MTP's semantic conventions) and Markdown job summaries.
- **CTRF over a bespoke JSON schema:** CTRF is now emitted first-party with `flaky`, `retryAttempts` and attachments. BTP should consider emitting or consuming CTRF instead of, or alongside, a custom JSON format, attaching screenshots, diffs and axe JSON as CTRF attachments.
- **Reuse MTP's OTel conventions:** BTP's telemetry should reuse the `Microsoft.Testing.Platform` activity source and `test.case.*` attributes, adding BTP-specific attributes such as story id, render mode, browser, viewport and perf budget. Parenting spans under the MTP test activity lets Aspire and other OTel backends show BTP runs without custom tooling. Honouring `TRACEPARENT` links CI traces to test spans.
- **Report merging:** BTP's report merging across projects and render modes could implement `IArtifactPostProcessor` so `dotnet test` produces merged BTP reports natively. That API is experimental (`TPEXP`).
- **Avoid exit-5 failures:** extension-specific options fail with exit code 5 when a project lacks the extension, so BTP options should be documented with the conditional `TestingPlatformCommandLineArguments` pattern for mixed solutions.
- **Version targeting:** MTP mode `dotnet test` needs MTP 1.7 or later; MTP 2.x features (HTML, JUnit, CTRF and GitHub reports, retry delay, flaky history) need MTP 2.3 or 2.4. BTP should state minimum MTP package versions explicitly, since many 2026 features land in MTP 2.3, 2.4 or 2.5 rather than in the .NET 10 SDK.

### Gaps
- No .NET test tool researched emits SARIF. Whether GitHub code scanning or Azure DevOps SARIF viewers are a good host for test, a11y or visual findings was not researched here.
- The CTRF JSON schema itself (ctrf.io) was not fetched.
- No source confirmed whether Rider, VS or VS Code render custom MTP properties or artifacts, such as screenshots attached to a test node, in their test UIs.

## Q4. Which features would make a .NET/Blazor test platform's demo website most compelling and credible to .NET teams?

### Takeaway
Credibility with .NET teams comes from showing that the platform plugs into tools they already trust. That means:
- `dotnet test` in MTP mode, running unchanged across xUnit v3, NUnit, MSTest and TUnit;
- output that already renders in Azure DevOps, GitHub Actions, VS, Rider and ReportGenerator;
- OTel that opens in the Aspire dashboard.

Excitement comes from demonstrating what nothing else in .NET offers in one place: a live story catalog with real-browser, render-mode-matrix results, visual diffs with approval, axe findings, perf budgets, flake history and impact analysis.

### Cited Findings
- **The first-party baseline has become strong**, so a demo that only shows pass/fail, an HTML report, JUnit or basic retries looks generic:
  - MTP terminal and HTML reports.
  - GitHub and Azure DevOps annotations with history-based flaky and slow-test detection.
  - OTel.
  - Crash and hang dumps with a crash sequence file.
  - — [MTP test reports](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports); [MTP crash/hang dumps](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-crash-hang-dumps)
- **Aspire sets the bar for a polished .NET dashboard:** cross-linking ("move from a symptom to its cause"), trace waterfalls, exemplars, a resource graph, keyboard shortcuts, theming, export and import, and run history. — [Aspire dashboard](https://aspire.dev/dashboard/explore/)
- **Stryker's dashboard pattern:** hosted reports, badges, and baselines per branch through SourceLink project name and version. This shows that .NET teams accept a hosted quality dashboard plus badge model. — [Stryker reporters](https://stryker-mutator.io/docs/stryker-net/reporters/); [Stryker config](https://stryker-mutator.io/docs/stryker-net/configuration/)
- **BlazingStory has a public live demo** and is the existing reference point for a "Storybook for Blazor". It offers a stories catalog, Playwright visual regression and an MCP server, but is still in preview. A BTP demo will be compared against it. — [BlazingStory DEV article](https://dev.to/j_sakamoto/storybook-doesnt-support-blazor-so-i-reimplemented-it-in-c-411p); [BlazingStory NuGet](https://www.nuget.org/packages/BlazingStory/)
- **bUnit's render-mode support is emulation only** (`SetAssignedRenderMode`, `SetRendererInfo`, a throw if not set). Real per-render-mode browser evidence is a visible differentiator. — [bUnit render modes](https://bunit.dev/docs/interaction/render-modes.html)
- **Verify's approval workflow is familiar** to .NET developers (received/verified files, DiffEngine, Rider and ReSharper plugins, DiffEngineTray). Visual snapshot approval that reuses these conventions lowers adoption friction. — [Verify README](https://github.com/VerifyTests/Verify)
- **Playwright .NET tracing is manual and per test**, with no UI mode in the .NET docs. Automatically attaching traces to failed stories and opening them from the report is a concrete convenience. — [Playwright .NET trace viewer](https://playwright.dev/dotnet/docs/trace-viewer-intro)
- **Impact analysis is limited elsewhere:** Azure DevOps TIA does not support .NET Core, and SDK affected-tests is experimental and .NET 11+. A working TIA demo on .NET 10 Blazor fills a gap. — [ADO TIA](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/test-impact-analysis); [dotnet test (MTP)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- **AI and agent hooks are spreading** across .NET test tools: NCrunch's MCP server, BlazingStory.McpServer, MTP's AI-environment output tuning and the `mtp-hot-reload` .NET agent skill. — [NCrunch](https://www.ncrunch.net/); [BlazingStory.McpServer](https://nuget.org/packages/blazingstory.mcpserver); [MTP terminal output](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-terminal-output); [MTP Hot Reload](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-hot-reload)
- **Licensing sensitivity:** Verify's maintenance fee for organisations (after 1 Sept 2026), the restrictive "MTP Tools" license on Retry and Hot Reload, ReportGenerator PRO, and Visual Studio Enterprise-only Live Unit Testing. Teams notice license terms; a clear, permissive license statement on the demo site helps. — [Verify README](https://github.com/VerifyTests/Verify); [MTP retry](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-retry); [ReportGenerator README](https://github.com/danielpalme/ReportGenerator); [Live Unit Testing](https://learn.microsoft.com/en-us/visualstudio/test/live-unit-testing-intro)

### Inferences
Suggested demo website features, roughly in priority order:
1. **"Works with what you have" strip:** show one `global.json` with `"runner": "Microsoft.Testing.Platform"` and `dotnet test --report-btp ...`, running unchanged across xUnit v3, NUnit, MSTest and TUnit projects. Include badges for TRX, JUnit, CTRF, Cobertura, OTel and SARIF, plus screenshots of the outputs inside Azure DevOps, a GitHub PR job summary with annotations, Rider or VS Test Explorer, and the Aspire dashboard.
2. **Story catalog joined with results:** each story shows status per render mode (Static SSR, Interactive Server, WebAssembly, Auto) × browser × viewport. This matrix is the single most differentiating view against bUnit, BlazingStory and Verify.
3. **Visual diff review:** side-by-side, overlay and slider comparison with accept and reject, using Verify-familiar received/verified naming, and a "changed since main" filter in the spirit of Chromatic.
4. **Accessibility panel:** axe violations per story, with impact level, element highlight on the snapshot, rule links and trend over time.
5. **Render-perf budgets:** a per-story budget bar (actual against budget) and a history sparkline. A ReportGenerator-style "risk hotspots" table ranks components by perf regressions, flake rate, a11y debt and coverage.
6. **Run waterfall:** an Aspire-style span timeline for a story run (render, then lifecycle, JS interop, screenshot, then axe), with logs linked to spans, built from OTel data that uses MTP semantic conventions.
7. **Flake and history analytics:** an Azure DevOps-style "N failures from M tests" headline, a daily stacked outcome chart, flaky states (detected, marked, quarantined, resolved) and a CTRF-compatible retry history.
8. **Test impact analysis:** show changing one `.razor` file or its CSS and the subset of stories and tests selected, with fallback rules. Contrast with ADO TIA, which does not support .NET Core and falls back on HTML or CSS changes.
9. **Live local mode:** `btp` opens a local report that updates as tests finish (like Stryker `--open-report`), with Playwright traces one click away for failures.
10. **Polish signals:** dark and light themes, keyboard shortcuts, a downloadable run bundle (zip) and a sample public "dashboard" with real numbers from a non-trivial component library. Credibility rises sharply when the demo runs on a real OSS Blazor component library rather than toy components.
11. **Agent-friendly surface (optional):** an MCP server or JSON CLI output so coding agents can list stories, run them and fetch diffs. This matches NCrunch and BlazingStory.

### Gaps
- No survey data was found on which features .NET teams value most in test dashboards. The prioritisation above is inferred from ecosystem patterns, not measured demand.
- Chromatic, Cypress Cloud and Playwright UI mode (JS) features were not fetched in this .NET-focused pass; comparisons rely on the absence of equivalents in the .NET sources.
- Adoption numbers for TUnit, MTP mode and bUnit v2 (downloads, market share) were not collected.
