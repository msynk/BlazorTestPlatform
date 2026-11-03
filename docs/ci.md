# Diagnostics, reports and CI

## What every run records

Each story run writes one JSON file to `TestResults/btp/results/`. Files are independent, so parallel tests never contend for them. A result contains:

* the outcome, attempts and errors of earlier attempts
* the step timeline (the last steps also end every failure message)
* visual, accessibility and performance results, including the accessibility rules the story is excused from and why
* browser runs only: console messages, page errors, network requests (with status and timing) and WebSocket activity
* artifacts for failed runs (or for every run with `BTP_DIAGNOSTICS=always`): a full-page screenshot, a DOM snapshot and a **Playwright trace** (open it with `playwright.ps1 show-trace trace.zip`). A flaky run keeps the artifacts of its failed attempts too.

Tests can add run-wide files next to the results: the manual accessibility review gate writes `a11y-review-gate.json`, and a story coverage test writes `story-coverage.json` (see [quality gates](quality-gates.md#story-state-coverage)). `btp report` and `btp a11y status` pick them up.

## Flakiness

* `BTP_RETRIES` (default 2 on CI, 0 locally) retries failed browser runs in a fresh context. A run that passes after a failure is reported as **flaky**, not as passed, and keeps the errors and artifacts of its failed attempts.
* `btp report --history .btp/history.json` keeps a rolling window of 50 runs. Persist the file between CI runs, for example with `actions/cache`, and save it even when tests fail (failed runs are what the history needs). The report ranks result keys (story × browser × viewport × theme × render mode) that have at least two recorded runs by flake rate; keys that fail every run are treated as broken, not flaky.
* `btp flaky --history .btp/history.json` prints the ranking in the terminal, with each key's number of flips (a
  failing run followed by a passing one, or the reverse): a key that keeps flipping is flaky; one that failed once and
  then kept passing was a fixed regression. Keys with the same rate are ranked by flips.

## The `btp` CLI

Install it with `dotnet tool install -g BlazorTestPlatform.Cli --prerelease` while the packages are in preview.

```
btp report   [-r <results>...] [-o <dir>] [--junit <file>] [--markdown <file>] [--github-summary]
             [--history <file>] [--run-id <id>] [--fail-on-failures]
             [--pr-comment] [--pr <number>] [--repository <owner/name>] [--report-url <url>]
             [--sarif <file>] [--source-root <dir>]
btp init     [-f xunit|nunit|mstest|tunit] [--ci github|azure|gitlab|none] [--component <Type>] [--namespace <ns>] [--force]
btp approve  [-r <results>...] [-f|--filter <glob>] [--dry-run]
btp perf approve [-r <results>...] [-f|--filter <glob>] [--baseline <file>]
btp a11y init [-r <results>...] [--force]   |   btp a11y status [-r <results>...]
btp flaky --history <file>
btp clean
```

Exit codes: `report` returns 2 when there are no results, and with `--fail-on-failures` 1 when a story failed or a result file could not be read (a crashed run's file must not hide a failure; unreadable files are listed as warnings either way). `approve` returns 1 when some snapshot could not be approved. `clean` returns 1 when it refuses to delete the results directory (see [configuration](configuration.md#environment-variables)).

`btp approve` writes baselines only into the project's snapshot directory, named after the snapshot, and copies only PNG images found inside the results directory it read, so it is safe to run on results downloaded from CI (`btp approve -r path/to/downloaded/btp`). A corrupt flake history (for example a truncated cache entry) is reset with a warning instead of failing the report, and history and baseline files are written atomically.

Every command takes `-p <project>`, the test project directory with `btp.json`. It defaults to `BTP_PROJECT_DIR`, else the current directory, so run `btp` from the test project or pass `-p`. To build one report for several test projects, repeat `-r`.

## Pull-request comments

`btp report --pr-comment` posts the Markdown summary (outcomes, failures, flaky runs, the flakiest stories and story state coverage) on the pull request, and updates that same comment on every later run instead of adding a new one. It finds the comment by a hidden marker at the start of the body, posted by a bot account (or by the login in `BTP_PR_COMMENT_AUTHOR` when you post with a personal token), so other comments are never touched, even one that quotes the marker. Story text in the summary is escaped, so an error that mentions `@media` notifies nobody.

On GitHub Actions it reads the repository, the pull request number and the API address from the workflow's environment. It needs a token that can write pull-request comments, passed as `GITHUB_TOKEN`. Pull requests from forks get a read-only token: the command then prints a warning and the build continues. Outside Actions, pass `--pr` and `--repository`. The comment links to the workflow run, where the HTML report is attached; pass `--report-url` to link a published report instead.

## Sharding

Spread a large matrix over parallel CI jobs: each job runs one shard, and `btp report -r shard1 -r shard2 ...` merges their results.

```csharp
public static TheoryData<string> Cases => new(Matrix.Expand(Catalog).InShard(Shard.FromEnvironment()).Select(c => c.Key));
```

`BTP_SHARD=2/4` selects the second of four shards (`Shard.FromEnvironment()` returns the whole run when it is not set). A cell's shard comes from a stable hash of its key, so every machine computes the same split, and adding or removing stories never moves the others to another shard. `catalog.InShard(shard)` shards stories the same way, and `new Shard(i, n).Select(items, key)` shards anything else.

```yaml
strategy:
  matrix:
    shard: [1, 2, 3, 4]
steps:
  - run: dotnet test -c Release
    env:
      BTP_SHARD: ${{ matrix.shard }}/4
  - uses: actions/upload-artifact@v4
    with: { name: btp-shard-${{ matrix.shard }}, path: tests/MyDesignSystem.Tests/TestResults/btp }
```

## Code scanning (SARIF)

`btp report --sarif btp.sarif` writes a SARIF 2.1.0 log for GitHub code scanning, Azure DevOps (SARIF SAST Scans Tab)
or any SARIF viewer:

* accessibility violations, one result per rule and story, with the axe rule id (and help link) as the rule and the
  affected elements in the message; serious and critical ones are warnings, others notes, and those that failed the
  gate are errors
* failed stories (`btp/story-failed`), visual mismatches (`btp/visual-mismatch`) and performance regressions
  (`btp/performance-regression`)

Each result points at the file and line that declare the story's collection (relative to `--source-root`, default the
current directory, as `%SRCROOT%`), so code scanning annotates the story in pull requests, and carries a stable
fingerprint so a finding keeps its identity across runs.

```yaml
permissions:
  security-events: write
steps:
  - run: btp report -p tests/MyDesignSystem.Tests --sarif btp.sarif
  - uses: github/codeql-action/upload-sarif@v3
    with: { sarif_file: btp.sarif, category: blazor-ui-tests }
```

## Tracing and metrics (OpenTelemetry)

Every story run is an `Activity` named `btp.story` (tags: story id, component, driver, hosting model, viewport, theme,
outcome, attempts; status error when it failed), with one child `btp.step` per timeline step. The
`BlazorTestPlatform` meter counts runs (`btp.story.runs`, by outcome and driver) and records their duration
(`btp.story.duration`, in ms). Nothing is collected unless a listener subscribes; with the OpenTelemetry SDK:

```csharp
using var tracing = Sdk.CreateTracerProviderBuilder().AddSource(BtpTelemetry.Name).AddOtlpExporter().Build();
using var metrics = Sdk.CreateMeterProviderBuilder().AddMeter(BtpTelemetry.Name).AddOtlpExporter().Build();
```

(for example in an xUnit assembly fixture), then send them to Jaeger, Zipkin, Azure Monitor or any OTLP collector to
see slow stories, retry rates and failing steps across runs.

## Test impact analysis

On pull requests, run the stories a change can affect first, or only those:

```csharp
// BTP_CHANGED_SINCE=origin/main: stories affected since the merge base; unset: every story.
public static TheoryData<string> Stories => StoryTheoryData.From(StoryImpact.FromEnvironment(Catalog).Stories);
```

`StoryImpact.Analyze(catalog, changedFiles)` maps changed files to stories: the file that declares a story collection,
files named after a component (`Button.razor`, `.razor.cs`, `.razor.css`, `.razor.js`) affect its stories and the
stories of every component that renders it (found in the compiled render code), and files that carry a story id
(baselines) affect that story. Documentation and images are ignored. Any other source or build file can affect any
story, so `RunsEverything` turns true and every story runs: the analysis never narrows a run on a change it cannot
place. `StoryImpact.ChangedFilesSince("origin/main")` lists committed, staged, unstaged and untracked changes since the
merge base (fetch the base branch: `fetch-depth: 0`). Components picked at run time (`DynamicComponent`, fragments
passed by stories) are not traced, so keep running everything on the main branch.

## Azure Pipelines and GitLab

`btp init --ci azure` or `--ci gitlab` writes a pipeline for the test project (see [test frameworks](test-frameworks.md#scaffolding-btp-init)).
Both run on the Playwright image (`mcr.microsoft.com/playwright/dotnet:v<version>-noble`) so screenshots match Linux
baselines, set `BTP_CHANGED_SINCE` on pull/merge requests, and publish JUnit results:

* **Azure Pipelines**: `TF_BUILD` is detected as CI. `btp report --junit $(Build.ArtifactStagingDirectory)/junit.xml`
  feeds `PublishTestResults@2`, the results directory is published as a pipeline artifact, and the SARIF log can be
  shown by the SARIF SAST Scans Tab extension.
* **GitLab CI**: `GITLAB_CI` is detected as CI. `artifacts:reports:junit` shows the JUnit results in merge requests;
  the `--markdown` summary can be posted with a merge-request note by your own job.

Sharding (`BTP_SHARD`) works the same everywhere: `parallel: 4` with `BTP_SHARD: $CI_NODE_INDEX/$CI_NODE_TOTAL` on
GitLab, a `strategy: parallel` job with `BTP_SHARD: $(System.JobPositionInPhase)/$(System.TotalJobsInPhase)` on Azure.

## Mutation testing

Stories make component logic testable in memory, so mutation testing measures how much of it the play scripts and
gates really check. With [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/):

```
dotnet tool install -g dotnet-stryker
cd tests/MyDesignSystem.Tests
dotnet stryker --project MyDesignSystem.csproj --test-project MyDesignSystem.Tests.csproj
```

Point it at the bUnit test project only: in-memory story runs take milliseconds, browser suites would multiply the run
time by the number of mutants. Surviving mutants in a component usually mean a state no story renders (compare with
[story state coverage](quality-gates.md#story-state-coverage)) or a play script without an assertion after an action.
Run it on a schedule or on demand rather than on every pull request.

## Result files

Each run writes `TestResults/btp/results/{key}.json`. The format is versioned: every file carries `schemaVersion` (currently 1; files from earlier versions read as 1). New optional properties do not change the version; it increases only when the meaning of existing properties changes, and readers list files with a newer version as unreadable rather than misreading them.

## GitHub Actions

A typical job (this repository's own [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) does the same in the Playwright container, and runs the CLI from source):

```yaml
permissions:
  contents: read
  pull-requests: write          # for --pr-comment

steps:
  - uses: actions/checkout@v4
  - run: dotnet tool install -g BlazorTestPlatform.Cli --prerelease
  - run: dotnet build -c Release
  - run: pwsh tests/MyDesignSystem.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
  - uses: actions/cache/restore@v4
    with:
      path: .btp/history.json
      key: btp-history-${{ github.run_id }}
      restore-keys: btp-history-
  - run: dotnet test -c Release --no-build
  - if: always()
    env:
      GITHUB_TOKEN: ${{ github.token }}
    run: btp report -p tests/MyDesignSystem.Tests --junit junit.xml --github-summary --history .btp/history.json --pr-comment
  - if: always()
    uses: actions/cache/save@v4
    with:
      path: .btp/history.json
      key: btp-history-${{ github.run_id }}
  - if: always()
    uses: actions/upload-artifact@v4
    with:
      name: btp-report
      path: tests/MyDesignSystem.Tests/TestResults/btp
```

CI is detected automatically (`CI` set to `1`, `true` or `yes`, or any of `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`, `BUILDKITE` or `TEAMCITY_VERSION`). On CI, missing visual baselines fail the build instead of being written, browser runs are retried, and missing performance baselines are not recorded. Diagnostics are kept for failures everywhere.

## Making snapshots stable across machines

Font rendering differs between Windows, macOS and Linux, so the platform is part of the snapshot name (`…chromium-linux.png`). CI runs in the Playwright image that matches the pinned `Microsoft.Playwright` package (`mcr.microsoft.com/playwright/dotnet:v{version}-noble`). The workflow and the scripts read the version from `Directory.Packages.props`, so the tag never has to be kept in sync by hand. The image contains all three browsers, so CI gates on `chromium-linux`, `firefox-linux` and `webkit-linux` baselines. GitHub Actions container jobs run as root with `HOME=/github/home`, a folder owned by another user, and Firefox refuses to start there: set `HOME: /root` on the steps that run browser tests. To produce or check the same Linux baselines on any machine with Docker:

```bash
scripts/visual-tests.sh              # compare with the committed Linux baselines
scripts/visual-tests.sh --update     # write missing baselines, then review and commit them
pwsh scripts/visual-tests.ps1 -Update   # Windows
```

The script mounts the repository, uses `--artifacts-path` so container builds never touch your local `bin/` and `obj/`, and caches the .NET SDK and NuGet packages in Docker volumes (the SDK is reinstalled when `global.json` needs a newer one). `--print-image` (`-PrintImage` in PowerShell) shows the tag it uses, and `BTP_PLAYWRIGHT_IMAGE` overrides it.

### Upgrading Playwright

A Playwright upgrade ships new browser builds, and those render text and anti-aliasing slightly differently, so some baselines legitimately change. The platform makes this a routine step rather than a hunt:

1. `__snapshots__/baselines.json` records which renderer produced the baselines of each `{browser}-{platform}` (Playwright and browser versions). It's written when the first baselines of a `{browser}-{platform}` are created, when baselines are revalidated with `BTP_UPDATE_SNAPSHOTS=changed|all`, and when `btp approve` accepts changed screenshots. It's never written when the update mode is `none`, the default on CI.
2. After the version bump (by hand, or the Dependabot PR that `.github/dependabot.yml` opens for `Microsoft.Playwright`), CI fails in its first test step, `BaselineProvenanceTests`, which says the baselines come from the previous version. Visual mismatches carry the same explanation in the failure message and the HTML report, so they don't look like regressions.
3. Run the **Regenerate visual baselines** workflow (`.github/workflows/baselines.yml`) on the branch. It renders every story in the matching image, rewrites only the baselines that changed (`changed`, the default), records the new renderer and pushes one commit. Review the image diffs in the PR. Locally, `scripts/visual-tests.sh --update=changed` does the same.
4. Commits pushed with the workflow's `GITHUB_TOKEN` don't trigger CI. Re-run the PR checks, or push any commit, to get a green build on the regenerated baselines.

Windows and macOS baselines are for local development only, so they aren't committed: this repository's `.gitignore` keeps only the `*-linux` images under `__snapshots__/` (see [What to commit](configuration.md#what-to-commit)). The first local run writes them; regenerate them with `BTP_UPDATE_SNAPSHOTS=changed dotnet test` on that machine.
