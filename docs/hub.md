# The test hub

The hub is a self-hosted test platform website over a test project's output: the latest result of every story and cell, the history of archived runs, and the decisions people make about them. It is a Razor class library, `BlazorTestPlatform.Hub`, that you add to an ASP.NET Core app (typically the one that hosts the [workbench](getting-started.md#4-workbench-host)) at `/_btp`.

Everything it shows comes from files the platform already writes, so there is no database and no service to run:

| Source | Written by | Used for |
|---|---|---|
| `TestResults/btp/results/*.json` | every bUnit and browser run | the latest result of each story × driver × render mode × viewport × theme |
| `.btp/runs/*.json` (the run archive) | `btp report --archive`, runs started from the hub | trends, stability, unique errors, comparisons, accessibility baselines |
| `__snapshots__/` and `TestResults/btp/visual/` | browser runs | visual review |
| `quarantine.json` | `btp quarantine`, the hub | quarantined stories |
| `a11y-reviews/`, `a11y-review-gate.json` | people, `AccessibilityReviews.VerifyAsync` | manual reviews and the WCAG traceability matrix |
| `__baselines__/performance.json` | bUnit runs, `btp perf approve` | render-performance baselines |

## Host it

```csharp
builder.Services.AddBlazorTestHub(options =>
{
    options.ProjectDirectory = "tests/MyDesignSystem.Tests";      // btp.json, results, baselines, quarantine.json
    options.AddStoriesFromAssemblyOf<ButtonStories>();             // stories the hub can run in memory
    options.RepositoryUrl = "https://github.com/org/repo";         // commit and issue links (optional)
});

app.UseAntiforgery();
app.MapBlazorTestHub();
app.MapRazorComponents<App>()
   .AddAdditionalAssemblies(typeof(HubOptions).Assembly);         // and in the Router's AdditionalAssemblies
```

| Option | Default | Meaning |
|---|---|---|
| `ProjectDirectory` | `BTP_PROJECT_DIR`, else the current directory | The test project the hub reads |
| `ArchiveDirectory` | `{ProjectDirectory}/.btp/runs` | The run archive |
| `Window` | 30 | Runs analysed for stability, clusters and trends |
| `ArchiveCapacity` | 100 | Runs kept |
| `Enabled` | off in `Production`, unless `BTP_HUB_ENABLED` says otherwise | Whether the hub responds at all (404 otherwise) |
| `AuthorizationPolicy` | none | A policy users must satisfy; others get 404 |
| `AllowChanges` | true | Accept baselines, quarantine stories and start runs; false makes the hub read-only |
| `WorkbenchUrl` | `/_workbench` | "Open in workbench" links; stories tagged `hub-only` get none |

The hub reads test output, writes baselines and `quarantine.json` and runs stories, so treat it like the workbench: keep it off in production, or protect it with a policy. Actions are form posts with antiforgery tokens, and files are only served from the results and snapshot directories (PNG, ZIP, JSON and text; DOM snapshots are served as plain text).

## Record runs

The hub's history views need an archive. Add a run after each test run:

```bash
btp report -p tests/MyDesignSystem.Tests --archive tests/MyDesignSystem.Tests/.btp/runs
```

The archive records the run's commit, branch, author, pull request, CI provider and link (GitHub Actions, Azure Pipelines and GitLab variables, else `git`), and a compact copy of every result: outcome, attempts, duration, the first lines of the error, its signature and category, visual status and diff fingerprint, accessibility violations, render and browser metrics. Persist the directory between CI runs (a cache, an artifact or a branch) to build history there; runs started from the hub are added to the same directory.

## Pages

| Page | What it shows |
|---|---|
| **Dashboard** | Latest totals, pass-rate and duration trends, outcomes per run, unique errors, least stable results, the render-mode × driver matrix, and what waits for a decision |
| **Release readiness** | Every gate with a single verdict: blocking failures, unreadable results, visual changes, the accessibility gate and new findings, manual reviews, performance budgets, flakiness, quarantine, coverage, history |
| **Runs**, **run detail** | Every archived run with its source, outcomes and pass rate; one run's environment and filterable results |
| **Compare runs** | Base versus head: new failures, still failing, fixed, new flaky, slower, added, removed |
| **Run stories** | In-memory runs started from the browser (all, failed, unstable, a component, a tag, one story, the stories a change affects), with live progress |
| **All tests** | The latest result per cell with its stability, tags and history strip; a matrix view of stories × cells |
| **Story** and **result** | Every cell of a story; one result's failure (category, likely cause, next step, earlier attempts, stack), timeline waterfall, visual snapshot, accessibility, render and browser metrics, console, mocked HTTP, network, artifacts and final markup. A failure can be copied as a prompt or opened as a GitHub issue |
| **Failures** | Unique errors: failures grouped by a normalized signature, with category, affected stories and cells, drivers, render modes, first and last seen |
| **Flaky & quarantine** | Stability per result, flake rate with a 95% interval, flips, severity, time lost; quarantine and release stories |
| **Visual review** | Changed snapshots, grouped by diff fingerprint; side by side, slider, overlay, strobe and diff views; accept or reject one snapshot or a group; keyboard review (`j`/`k`, `a`, `r`, `g`, `1`–`5`); a baseline gallery |
| **Accessibility** | New, existing and fixed violations since the previous run; violations by rule and by component; the needs-review queue (axe "incomplete"); waivers and expiring suppressions; a trend |
| **Performance** | Render budgets and baselines, browser runtime metrics, slowest results and duration trends per story |
| **Coverage & traceability** | Story-state coverage, manual review status, and a WCAG 2.2 traceability matrix (OK, NOK, NOTRUN, N/A, STALE, UNCOVERED) with automated findings per criterion |
| **Impact & sharding** | Stories affected by changed files or a git ref, with the reason for each; duration-balanced shards compared with the hash split |
| **Integrations** | JUnit, SARIF, Markdown and CSV exports, the JSON API, the pull-request comment, CI and CLI snippets |

## Stability

Each result key (story × cell) gets a status over the analysed window:

| Status | Rule |
|---|---|
| New | fewer than two runs |
| Healthy | passed cleanly in every run |
| Failing | fails now after a clean history (a regression in progress) |
| Broken | failed every run, or the last three or more |
| Flaky | failed, passed and failed again, or passed only on retry |
| Fixed | one stretch of failures followed by passes, or five clean runs after flakiness |

The flake rate is the share of unstable runs, with a 95% Wilson interval so a rate from four runs is not read like one from forty. Severity follows Cypress Cloud's bands (up to 10%, up to 50%, above). Tags describe the latest run against the previous one: new test, new failure, still failing, always failing, fixed, flaky, slower (more than 1.5× the median and 100 ms longer), quarantined. All of it is in `RunHistory` (`BlazorTestPlatform.Reporting`) for your own tools.

## Failure categories

`FailureAnalysis` gives every failure a signature (the error with numbers, durations, GUIDs, paths, URLs and long quoted values replaced) and a category from the evidence on the result: assertion, locator, visual change, accessibility, performance, render mode, component crash, page error, timeout, infrastructure or unclassified. Each category comes with a likely cause and a next step. Nothing is sent to a model; `FailureAnalysis.ToPrompt(result)` builds a self-contained description you can paste into an assistant or an issue.

## Quarantine

A quarantined story still runs and still reports its outcome, but its failures do not fail the build: `VerifyAsync` (bUnit and browsers) returns instead of throwing, the test framework packages pass the test, JUnit reports it as skipped with the reason, and the Markdown summary lists it apart. Entries live in `quarantine.json` next to `btp.json`, so they are reviewed like code:

```json
{
  "quarantined": [
    {
      "pattern": "data-people-*",
      "reason": "Flaky API mock; tracked in #42",
      "owner": "UI team",
      "issue": "#42",
      "expires": "2026-12-31"
    }
  ]
}
```

A pattern is a story id, a result key or a glob (`*|webkit|*` quarantines every WebKit cell). After `expires`, failures fail the build again. Manage entries with `btp quarantine add|remove|list` or from the hub.

## JSON API

Every page's data is available as JSON, under the same access rules: `api/summary`, `api/runs`, `api/runs/{id}`, `api/failures`, `api/stability`, `api/results/{key}` and `api/live`, all under `/_btp/`. Exports of the latest results: `export/junit`, `export/sarif`, `export/markdown` and `export/results.csv`.
