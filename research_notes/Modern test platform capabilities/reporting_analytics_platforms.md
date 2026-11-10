# Test Result Reporting, Analytics and Observability Platforms (2025-2026)

Scope: Allure Report 3 / Allure TestOps, ReportPortal, Currents.dev, Cypress Cloud, Playwright (HTML reporter, trace viewer, UI mode, merge-reports), Azure App Testing Playwright Workspaces (formerly Microsoft Playwright Testing), Datadog Test Optimization, Buildkite Test Engine, BrowserStack Test Reporting & Analytics. Research date: 2026-11-10. All facts come from official docs, changelogs or vendor blogs unless noted. Fetched doc pages were often overview-level, so some UI details are listed under Gaps.

## Q1. What pages and views does each product have, and what are their layouts and key widgets?

### Takeaway
Most products share the same information architecture: a **runs/builds list**, a **run detail** page (status counters, metadata, spec/test tree), a **test detail/history** page, **analytics/trend** reports (flaky, slowest, top failures, most common errors), an **errors/unique-errors** view, and a **branch/PR comparison**. Products differ mainly in how deep their error clustering goes and whether they also handle quarantine/workflow automation. Several products were renamed between 2024 and 2026.

### Cited Findings

**Product naming and status (2025-2026)**
- Allure Report 3 is a TypeScript rewrite of Allure with a modular plugin system. It still produces static HTML. New in v3: one config file, reruns grouped in one report, multi-environment reports, known/muted/accepted "resolutions" linked to issue trackers, a `watch` command for real-time updates, project-wide quality gates, performance metrics with trends, and the "Allure Storage Service" for cloud history — [Allure 3 docs](https://allurereport.org/docs/v3/)
- The Allure 3 README says the Classic and Allure 2-style UIs are "currently experimental." "Awesome" is the default and recommended UI — [allure3 GitHub](https://github.com/allure-framework/allure3)
- Allure 3 migration notes: most CI and dev tool integrations are "still in development," and integrations marked "(Allure 2)" remain Allure 2-only — [Allure 3 migrate](https://allurereport.org/docs/v3/migrate.md)
- Allure TestOps ships in Self-Hosted, Cloud and Premium Cloud editions. Upgrade notes exist for versions 26.1.1, 25.3.2 and 25.2.1, which suggests year-based versioning — [Allure TestOps docs](https://docs.qameta.io/allure-testops/)
- Datadog Test Optimization was formerly called CI Visibility. It is now part of "CI/CD Optimization," which combines CI Visibility and Test Optimization in one interface — [Datadog Tests](https://docs.datadoghq.com/tests/)
- BrowserStack "Test Reporting & Analytics" was formerly called Test Observability — [BrowserStack TRA](https://www.browserstack.com/docs/test-reporting-and-analytics)
- Microsoft Playwright Testing (Preview) was retired on March 8, 2026. Its replacement is Playwright Workspaces in Azure App Testing, which has "identical execution capabilities, performance, and pricing." Azure App Testing combines Azure Load Testing and Playwright Workspaces — [Azure retirement notice AZ499860](https://mc.merill.net/message/AZ499860)
- Buildkite Test Engine is described as "the testing layer of Buildkite Pipelines." Older changelog entries use the name "Test Analytics" — [Buildkite Test Engine docs](https://buildkite.com/docs/test-engine); [Buildkite changelog 189](https://badge.buildkite.com/resources/changelog/189-test-analytics-now-automatically-detects-flaky-tests/)

**Allure Report 3 (static report)**
- Allure 2 automatically filled Behaviors, Categories, Packages and Suites tabs. Allure 3 instead groups results by any labels in a configurable hierarchy. Timeline and charts moved behind the "Report" link in the top-left corner, and the charts shown are configurable — [Allure 3 migrate](https://allurereport.org/docs/v3/migrate.md)
- Plugins (report types and outputs):
- `awesome`: main UI. Options: `groupBy`, `filter`, `charts`, `ideaLinks`, `singleFile`, `reportLanguage`, `publish`.
- `classic` and `allure2`: older-style UIs.
- `dashboard`: standalone charts-only report with a `layout` chart list.
- `csv`: configurable fields, sort and filter.
- `log`: console output, including quality gate results.
- `slack`: webhook or token plus channel.
- `jira`.
- `testops`: uploads results to Allure TestOps.
- `testplan`: writes a rerun file of unsuccessful tests.
- If no plugins are set, `awesome` is used.
— [Allure 3 configure](https://allurereport.org/docs/v3/configure/)
- `variables` holds key-value metadata shown at the top of the Awesome report. `defaultLabels` sets fallback severity, owner and tags — [Allure 3 configure](https://allurereport.org/docs/v3/configure/)
- CLI commands: `run -- <cmd>`, `generate`, `open`, `watch <resultsDir>` (live refresh), `agent -- <cmd>` (writes agent-friendly markdown: `index.md`, `manifest/run.json`), `agent inspect`, `agent latest` — [allure3 GitHub](https://github.com/allure-framework/allure3)

**Allure TestOps (server)**
- Doc sections cover: Launches, Test results, combined launches, Launch environments, Test status transitions, Unstable tests, Dashboards, Defects, Compare launches, Release information, Members and ownership, and Slack notifications. Launches are where teams "review results, rerun selected tests, close execution windows, and move results into history." Defects cover "known failures, automation rules, linked issues, and triage queues" — [Allure TestOps docs](https://docs.qameta.io/allure-testops/)

**ReportPortal**
- Dashboards are built from 22 widget templates:
- Launch statistics chart
- Overall statistics
- Launches duration chart
- Launch execution and issue statistic
- Project activity panel
- Test-cases growth trend chart
- Investigated percentage of launches
- Launches table
- Unique bugs table
- Most failed test-cases table
- Failed cases trend chart
- Non-passed test-cases trend chart
- Different launches comparison chart
- Passing rate per launch
- Passing rate summary
- Flaky test cases table (TOP-50)
- Cumulative trend chart
- Most popular pattern table (TOP-20)
- Component health check
- Component health check (table)
- Most time-consuming test cases (TOP-20)
- Test case search

Widgets support Launch or Timeline mode where applicable. They refresh every 1 minute, or every 30 seconds in fullscreen — [ReportPortal Widget Creation](https://reportportal.io/docs/dashboards-and-widgets/WidgetCreation/)
- The Component health check widget is multi-level (up to 10 levels) by test attribute, and you can drill down to test cases at each level — [ReportPortal Component health check](https://reportportal.io/docs/dashboards-and-widgets/ComponentHealthCheck/)
- Launch drill-down levels have a "Unique errors" tab and Step and Log views. The "Make decision" modal can be opened from the Step level or the Log level — [ReportPortal Unique Error Analysis](https://reportportal.io/docs/analysis/UniqueErrorAnalysis/); [ReportPortal Manual Analysis](https://reportportal.io/docs/analysis/ManualAnalysis/)

**Currents.dev**
- Dashboard pages:
- Runs: Run Details, Run Progress (how tests are distributed across CI machines), Pull Requests (runs grouped by PR), Run Attachments (screenshots, videos, traces and files in one list), Run Timeouts, Cancelling Runs.
- Tests and specs: Test View, Test Details, Test Status, Flaky Tests, Test History, Spec Files View, Spec File Details.
- Explorers: Test Suite Explorer, with Test Explorer (flakiness, failure rate and duration per test), Spec Files Explorer and Error Explorer (top errors, components and network requests).
- Analytics: Test Flakiness and Test Results distributions over time, plus Automated Reports for regression detection.
— [Currents docs index](https://docs.currents.dev/llms.txt)
- Run Details header: Duration and Progress (completed vs. total spec files), Started at, Branch, Commit message and SHA, Browser (Cypress) or Project (Playwright), Author, CI Build ID, and a CI Provider link. The run title comes from the PR title, then the commit message, then "No title" — [Currents Run Details](https://docs.currents.dev/dashboard/runs/run-details.md)

**Cypress Cloud**
- Project analytics reports (most have KPIs, a chart, a table and CSV export):
- **Run status**: runs per day by status; total and average runs.
- **Run duration**: average parallelization, average duration, time saved by parallelization.
- **Test suite size**: unique tests, spec files.
- **Top failures**: median failure rate and failure count; view by test case, spec, tag or branch; drill-in panel with error history.
- **Slowest tests**: median duration and total runs.
- **Most common errors**.
- **Flaky tests**: flaky cases per day, severity KPIs, last flaky time, flaky rate.
- Filters include branch, tag, time range, run group, committer, spec file, browser, Cypress version and OS.
— [Cypress Project Analytics](https://docs.cypress.io/cloud/features/analytics/project-analytics)
- Analytics tiers: Project Level Analytics, Enterprise Reporting (organization-wide) and the Data Extract API — [Cypress Analytics overview](https://docs.cypress.io/cloud/features/analytics/overview)
- Branch Review compares a base run with a changed run, usually from a PR.
- Tabs: Failures, Flaky, Pending, Added, Modified, plus UI Coverage and Accessibility change callouts.
- Count indicators: red up arrow (introduced), green down arrow (resolved), gray total.
- Test states: New, Existing, Resolved.
- The test comparison view shows base and feature results side by side, with Test Replay, screenshots and videos per attempt (attempts in descending order) and code diffs in the test definition panel.
- Entry points: project card, Branches nav, run overview, PR comment link "View all changes introduced in this branch," and a "Branch Review available" banner.
— [Cypress Branch Review](https://docs.cypress.io/cloud/features/branch-review)

**Playwright (OSS)**
- HTML reporter features by version:
- Speedboard tab sorting tests by duration (v1.57).
- Timeline chart in Speedboard for merged reports (v1.58).
- Shows runs from the same worker and adds step filtering (v1.59).
- Duration waterfall next to test steps (v1.63).
- Negated filters such as `!@my-tag` and `!p:my-project` (v1.52).
- Previous/next test navigation (v1.49).
- `title` option (v1.53) and `noSnippets` (v1.54).
- `mergeFiles` grouping by top-level describe (v1.62).
- `onlyFailures` (v1.65).
— [Playwright release notes](https://playwright.dev/docs/release-notes)
- Built-in reporters: `list`, `line`, `dot`, `failures` (v1.65), `html`, `blob`, `json`, `junit`, `github` and `perfetto` (Trace Event Format timeline). HTML options include `open`, `outputFolder`, `attachmentsBaseURL`, `onlyFailures`, `title`, `noSnippets`, `noCopyPrompt`, `doNotInlineAssets` (for CSP) and `mergeFiles`. `show-report` can open a `.zip` — [Playwright reporters](https://playwright.dev/docs/test-reporters)

**Azure App Testing / Playwright Workspaces**
- Reporting became available to all Playwright Workspace users (blog dated Jan 19, 2026). Results and artifacts appear on the workspace's **Test Runs** page in the Azure portal. The Playwright Trace Viewer is embedded. Storage is "bring-your-own" (your Azure Storage account), and reporting itself has no extra charge — [MS Tech Community](https://techcommunity.microsoft.com/blog/appsonazureblog/introducing-reporting-in-playwright-workspaces/4487059)
- A search-result summary of the Microsoft Learn quickstart says the dashboard highlights failed and flaky tests and shows CI details (commit, author, branch) with one-click access to the pipeline run. This page was not fetched directly. Sources also disagree on the minimum Playwright version (1.47 vs. 1.57) — [MS Learn quickstart](https://learn.microsoft.com/en-my/azUre/app-testing/playwright-workspaces/quickstart-advanced-diagnostic-with-playwright-workspaces-reporting)

**Datadog Test Optimization**
- Main pages:
- **Test Health** (overview, at `/ci/test/health`).
- **Test Optimization Explorer / Test Runs** (search; a query can be exported to a CI Test monitor).
- **Flaky Tests Management** page.
- **CI/CD Optimization settings** (per-repository opt-in for Test Impact Analysis and Early Flake Detection).

Each test configuration (OS, runtime, or custom `test.configuration` tags) is tracked as a separate test — [Datadog Tests](https://docs.datadoghq.com/tests/)
- Test Health headline metrics:
- **Pipelines Failed**: pipelines failed due to flaky tests.
- **Time Wasted in CI**.
- **Time Lost Due to Flaky Tests**.
- **Dev Experience** breakdown: flaky vs. non-flaky failure time.
- **Total Time Saved**: from Test Impact Analysis plus Auto Test Retries.
- Test Health recommendations.
— [Datadog Test Health](https://docs.datadoghq.com/tests/test_health/)
- Commit Overview has **Failed Tests** and **New Flaky Tests** tables. Branch Overview has a **Flaky Tests** table. Test runs have a **Test Details side panel** — [Datadog developer workflows](https://docs.datadoghq.com/tests/developer_workflows/)

**Buildkite Test Engine**
- Views and features mentioned in the docs:
- Test suite **Summary** page with metric charts over a time window, such as the last 7 days.
- Surfaces the "slowest and least reliable tests in each suite."
- **Tests tab** on build pages.
- **Upload health** view (1-day or 7-day windows).
- **Saved views** that store filter and display settings.
- Build **Orchestration** page.
— [Buildkite Test Engine docs](https://buildkite.com/docs/test-engine); [Buildkite llms-test-engine](https://buildkite.com/docs/llms-test-engine.txt)
- A "Flaky" saved view lists actively flaky tests, and an ownership filter shows tests by team — [Buildkite changelog 295](https://buildkite.com/resources/changelog/295-improved-flaky-test-management-in-test-engine/)
- The suite summary gained a unique flaky test count. The flaky page gained a date filter from the last hour to 28 days. Exact dates are unconfirmed — [Buildkite changelog 246](https://badge.buildbox.io/resources/changelog/246-unique-flaky-test-metric); [Buildkite changelog 258](https://badge.buildbox.io/resources/changelog/258-date-filtering-on-flaky-test-page)

**BrowserStack Test Reporting & Analytics**
- Product areas:
- Build Runs view and Tests Health.
- Unique Errors.
- Timeline Debugging.
- Smart Tags.
- Automatic Failure Analysis.
- Mute problematic tests (removed from every metric).
- log4j logs.
- Custom dashboards and alerts.
- Quality Gate for builds, PR merges and deployments.
- JUnit XML upload API and Allure report support.
— [BrowserStack TRA](https://www.browserstack.com/docs/test-reporting-and-analytics); [BrowserStack Debug](https://www.browserstack.com/docs/test-reporting-and-analytics/debug)
- Build Insights has an **Insights** tab with an AI build summary and **Failure themes** and **Affected workflows** tabs — [BrowserStack TFA agent](https://www.browserstack.com/docs/test-reporting-and-analytics/agents/test-failure-analysis)

### Inferences
- A common showcase skeleton fits all of these products:
1. Runs list with status pills, branch, commit, duration and counts.
2. Run detail with a header of metadata and counters, plus tabs for Tests, Errors, Attachments, Timeline and Environment/Browser.
3. Test detail with an attempts switcher, steps, artifacts and a history strip.
4. Analytics pages for Flaky, Slowest, Top failures and Most common errors.
5. Unique errors view.
6. Branch/PR compare view with New / Existing / Resolved buckets.

BlazorTestPlatform's per-story JSON, step timelines, traces, screenshots, visual diffs, axe and render metrics map directly onto this.
- Cypress's New / Existing / Resolved model and red/green delta arrows are a compact pattern for "story results vs. main."
- ReportPortal's attribute-driven Component health check fits component libraries well. Results could be grouped by Blazor component or story attributes.
- A Speedboard (tests sorted by duration) plus a worker timeline (Playwright 1.57/1.58) is a cheap, high-value view that render metrics can feed.

### Gaps
- Allure TestOps page layouts (launch detail tabs, widget names) were not available from the landing page.
- The Azure App Testing Test Runs page layout and filters are not documented in the fetched sources.
- Buildkite's test detail page (reliability %, duration chart) is not described. I found no reliability formula.
- Allure 3 Awesome tab names (History, Retries) were not confirmed. The history-and-retries page fetch failed.

## Q2. How do they visualize history (status timelines, duration trends, pass-rate charts, stability scores)?

### Takeaway
The dominant pattern is a **per-test chronological bar strip** colored by status, plus **trend lines** for flaky rate, failure rate and duration. Stability is expressed as a **flake rate with severity buckets** (Cypress), **status-flip or pass-on-retry rules** (BrowserStack, Buildkite), or **failure-rate thresholds** (Datadog), rather than a single numeric score.

### Cited Findings
- **Currents Test History**:
- One bar per test instance in chronological order, shown as a chart or a list. Hovering shows run details, and clicking opens the instance.
- Colors: red = failed, green = passed, **purple = passed but flaky**, grey = ignored, orange = skipped due to failure.
- Filters: time range (7/14/28/45 days), test state, **Merge Branches**, **Pin Branch**.
- Test identity is spec + project + title. Renaming breaks history.
— [Currents Test History](https://docs.currents.dev/dashboard/tests/test-history)
- **Cypress flaky analytics**:
- "Flaky tests over time" plot.
- A single project-wide "Flakiness level."
- Severity KPIs: Low >0-10%, Medium >10-50%, High >50% flake rate.
- A table ordered by severity.
- A details panel with recent flaky runs, most common errors, test changelog, and a **failure rate vs. flake rate chart**.
— [Cypress Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management)
- Cypress Run duration shows the average of passing runs and time saved by parallelization. Test suite size shows unique tests and specs over time — [Cypress Project Analytics](https://docs.cypress.io/cloud/features/analytics/project-analytics)
- **Allure 3 history**:
- Stored in a JSONL file (`historyPath`), which enables "trend charts, flaky detection, and status transitions."
- `historyLimit` prunes old entries. `appendHistory: false` reads history without appending.
- `historyBaseUrl` links history entries to old reports.
- `allureService` provides remote storage.
— [Allure 3 configure](https://allurereport.org/docs/v3/configure/)
- Allure 3 category matchers include `transitions` (new, fixed, regressed, malfunctioned) and `flaky`. `groupBy` can use flaky, owner, severity, transition, status, environment or layer — [allure3 GitHub](https://github.com/allure-framework/allure3)
- Allure 3 collects performance metrics (for example page load), trends them and can gate on them — [Allure 3 docs](https://allurereport.org/docs/v3/)
- **ReportPortal** trend widgets: Cumulative trend chart, Failed cases trend, Non-passed trend, Passing rate per launch and summary, Launches duration chart, Test-cases growth trend — [ReportPortal Widget Creation](https://reportportal.io/docs/dashboards-and-widgets/WidgetCreation/)
- ReportPortal's Flaky TOP-50 counts Passed↔Failed status changes across selected launches, considering only the last retry — [ReportPortal Flaky TOP-50](https://reportportal.io/docs/dashboards-and-widgets/FlakyTestCasesTableTop50/)
- **BrowserStack Smart Tags**:
- **Flaky**: passes on retry in the same run (configurable 0-30 runs), or pass/fail flips >50% of the last 10 runs.
- **Always Failing**: same error in the last 5 runs.
- **New Failure**: new error first seen in the last 5 runs.
- **Performance Anomaly**: duration above the P95 of the last 100 executions (percentile 80-99, window 30-100).
- Tags show on test cases and on builds.
— [BrowserStack Smart Tags](https://www.browserstack.com/docs/test-reporting-and-analytics/features/smart-tags)
- **Datadog**:
- Per-test pipeline failures, CI time wasted and failure rate.
- `broken_test:true` marks a 100% failure rate over the last 7 days.
- The metric `test_optimization.test_management.flaky_tests` has tags for branch, flaky_status, test_codeowners and flaky_category.
— [Datadog Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- **Buildkite**:
- Flaky means both passed and failed on the same commit SHA.
- The label auto-clears if that hasn't happened within 100 executions or 7 days (Oct 2025).
- Monitors: transition count, passed on retry, probabilistic flakiness, new test.
— [Buildkite changelog 295](https://buildkite.com/resources/changelog/295-improved-flaky-test-management-in-test-engine/); [Buildkite llms-test-engine](https://buildkite.com/docs/llms-test-engine.txt)
- **Currents Error Explorer** timeline: daily distribution of the top N errors, with a switchable metric — [Currents Error Explorer](https://docs.currents.dev/dashboard/test-suite-performance-explorer/errors-explorer)

### Inferences
- A per-story status strip (with a distinct flaky-passed color) plus duration and render-metric sparklines covers the most common history views.
- Severity buckets are easier to scan than a raw stability score.
- "Different outcomes on the same commit" (Buildkite) is a robust flakiness signal and matches BlazorTestPlatform's cross-run flake history. "Passed on retry within a run" is the second signal.
- A P95 Performance Anomaly tag maps directly to render metrics and is a differentiating widget.

### Gaps
- I found no source for a true tests × runs heatmap. ReportPortal's history-line UI was not fetched.
- Buildkite's probabilistic flakiness formula is not documented in the fetched sources.

## Q3. How do they group and triage failures (clustering, unique errors, defect types, auto-analysis, ownership, issue linking)?

### Takeaway
Three tiers of failure grouping exist:
1. **Normalized error-message clustering**: unique errors in ReportPortal and BrowserStack, Most common errors in Cypress.
2. **Structured error parsing**: Currents' Target / Action / Category fields.
3. **ML/AI defect-type classification**: ReportPortal XGBoost, BrowserStack AI, Datadog flaky categories.

Rule-based known-issue matching (regex to issue link) is the most portable approach (Allure 3 resolutions, ReportPortal Pattern Analysis).

### Cited Findings
- **ReportPortal Auto-Analysis**:
- Indexes Error-level logs (≥ 40,000) into OpenSearch.
- Extracts message, stack trace, numbers, exceptions, URLs and paths.
- Finds candidates with TF-IDF and boosts, keeping the top 10.
- An XGBoost model (~30 features) chooses the defect type if its probability is at least 50%, and copies the comment and BTS link. Models are retrained per project.
- Base-for-analysis options: all previous launches, same-name launches, previous same-name launch, current launch only.
- "AA" label, "Ignore in AA" flag, and a "Defect assignment based on the longest retry" option.
— [ReportPortal Auto-Analysis](https://reportportal.io/docs/analysis/AutoAnalysisOfLaunches/)
- **ReportPortal Unique Errors** (since v5.7):
- A tab at any launch level.
- Clusters show error text and a MATCHED TESTS count, are sorted by count, and expand to list tests.
- An include/exclude-numbers setting controls merging.
- Clusters open "Make decision."
- The premium Quality Gate plugin adds All / New Errors / Known Errors vs. a baseline launch.
— [ReportPortal Unique Error Analysis](https://reportportal.io/docs/analysis/UniqueErrorAnalysis/)
- **ReportPortal Pattern Analysis**: String or Regex rules run before Auto-Analysis, add a PA label, are filterable by "Pattern Name," and feed the Most popular pattern widget (TOP-20) — [ReportPortal Pattern Analysis](https://reportportal.io/docs/analysis/PatternAnalysis/)
- **ReportPortal "Make decision" modal**:
- Left: "Execution to change."
- Right tabs: Manual selection, Analyzer suggestions (copies defect type, comment and BTS ticket), History of the test.
- "Apply for" extends the decision to similar To-Investigate items, with a preview.
- Bulk update is supported.
— [ReportPortal Manual Analysis](https://reportportal.io/docs/analysis/ManualAnalysis/)
- **Currents Error Classification Engine**:
- Fields: **Target** (selector, locator, endpoint, URL), **Action** (click, toBeVisible, waitForURL...), **Category** (Assertion, Async Assertion, Action, Timeout, Infra/Misc).
- Fields can be combined like a SQL GROUP BY.
- Metrics: Occurrences, Affected Tests, Affected Branches.
- Drill-down lists Tests, Executions and Branches.
— [Currents Error Explorer](https://docs.currents.dev/dashboard/test-suite-performance-explorer/errors-explorer)
- **Allure 3**:
- Categories match on statuses, labels, message, trace, flaky, transitions and environments. The first matching rule wins. Defaults are "Product errors" (failed) and "Test errors" (broken).
- Resolutions (`issue`, `muted`, `accepted`) match by `messageRegexp`, `testCaseId`, `retryHash` or `environment`, use issue URL templates, and `muted` supports `expiresAt`.
— [allure3 GitHub](https://github.com/allure-framework/allure3); [Allure 3 configure](https://allurereport.org/docs/v3/configure/)
- **Allure TestOps**:
- "Automatic failure grouping, known-issue and flaky-test detection."
- Defects with automation rules, linked issues and triage queues.
- Admin-level "Error categories."
- Issue trackers: Jira, Azure DevOps, GitHub, GitLab, Linear, YouTrack and others.
— [Allure TestOps docs](https://docs.qameta.io/allure-testops/)
- **BrowserStack**: Unique Errors groups failures by cause. Automatic Failure Analysis uses ML to categorize failure reasons and shows quick test history — [BrowserStack Debug](https://www.browserstack.com/docs/test-reporting-and-analytics/debug)
- The BrowserStack Test Failure Analysis agent separates product bugs from environment and test-script issues. "Bulk Apply to similar failures" propagates a category — [BrowserStack TFA agent](https://www.browserstack.com/docs/test-reporting-and-analytics/agents/test-failure-analysis)
- **Datadog AI flaky categories** (14): Concurrency, Randomness, Floating Point, Unordered Collection, Too Restrictive Range, Timeout, Order Dependency, Resource Leak, Asynchronous Wait, IO, Network, Time, Environment Dependency, Unknown — [Datadog Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- **Ownership**:
- Datadog uses CODEOWNERS, updated on default-branch runs, with notifications routed by owner — [Datadog Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- Buildkite uses a TESTOWNERS file with team slugs and a weekly "flakiest tests owned by your teams" email — [Buildkite llms-test-engine](https://buildkite.com/docs/llms-test-engine.txt)
- **Issue linking**:
- Datadog: "Open issue in GitHub" from Commit/Branch Overview and the Test Details panel; Jira work items with status sync; copy as Markdown — [Datadog developer workflows](https://docs.datadoghq.com/tests/developer_workflows/)
- BrowserStack: Jira create/update from Timeline Debugging — [BrowserStack Timeline Debugging](https://www.browserstack.com/docs/test-reporting-and-analytics/features/timeline-debugging)
- Buildkite: Linear issues via workflows — [Buildkite workflows blog](https://buildkite.com/resources/blog/introducing-test-engine-workflows)

### Inferences
- A minimum viable triage set needs no ML:
1. Error-fingerprint clustering into a "Unique errors" tab, with a matched-tests count and New vs. Known relative to a baseline.
2. Allure-style resolutions (regex or story ID to an issue link, mute with expiry).
3. A Currents-style structured breakdown adapted to components: Target = component/selector, Action = interaction/assertion, Category = Assertion / Timeout / Visual diff / A11y (axe) / Render perf / Infra.
- ReportPortal's defect taxonomy is the de facto industry vocabulary.

### Gaps
- The ReportPortal defect-type list (Product Bug / Automation Bug / System Issue / No Defect / To Investigate) was not on the fetched pages. Only "To investigate" is confirmed.
- The full BrowserStack category list was not found. Only "Environment Issue" was seen.
- Cypress's "Most common errors" grouping logic is undocumented.

## Q4. How do they present a single failed test (steps, attachments, screenshots, video, trace, logs, network, retries)?

### Takeaway
Best in class is a **time-travel timeline**: an actions list, a filmstrip, before/action/after DOM snapshots, and tabs for Call, Log, Errors, Console, Network, Source, Attachments and Metadata. Playwright's trace viewer defines this layout, and Azure and others embed it. Retries are shown as switchable attempts. Cross-run context (historical runs, or base vs. feature) sits next to the failure.

### Cited Findings
- **Playwright Trace Viewer**:
- Main areas: Actions list (locator and duration), timeline/filmstrip (range selection filters Actions, Console and Network), and Before / Action / After snapshots.
- Tabs:
  - Source
  - Call (timing, locator, strict mode, keys)
  - Log
  - Errors (red marker on the timeline)
  - Console
  - Network (sortable; headers and bodies)
  - Metadata
  - Attachments (visual diff of expected/actual/diff with an overlay slider)
- Open it via `show-trace`, trace.playwright.dev (client-side, `?trace=` param) or the HTML report.
- Modes: `on-first-retry`, `on-all-retries`, `retain-on-failure`, `on`, `off`.
— [Playwright Trace Viewer](https://playwright.dev/docs/trace-viewer)
- Newer Playwright additions:
- Aria/screen snapshots and "Display Aria" (v1.63), step subtitles and params (v1.63).
- WebSocket in traces (v1.61).
- `live` trace and `retain-on-failure-and-retries` mode (v1.59), `npx playwright trace` CLI (v1.59).
- HTML report: same-worker runs (v1.59), step duration waterfall (v1.63), attachment indicator on parent steps (v1.60).
- Per-retry annotations (v1.52).
— [Playwright release notes](https://playwright.dev/docs/release-notes)
- **Playwright UI Mode** adds a test sidebar, watch mode, Pick locator, Pop out DOM snapshot and Open in VS Code — [Playwright UI Mode](https://playwright.dev/docs/test-ui-mode)
- **Cypress Test Replay**:
- Run header (run #, branch, spec, platform, browser) and a Report an Issue button.
- Command Log with time travel.
- Developer Tools (network, console, elements).
- Replay Controls: attempt toggling, speed, scrubber.
- Opened from the Run Overview, the test sidebar, or per attempt in the errors panel. A CLI is available (`cy-cloud replay`).
- No WebKit or Firefox support.
— [Cypress Test Replay](https://docs.cypress.io/cloud/features/test-replay)
- **BrowserStack Timeline Debugging**:
- A right-side pane opened from Build Runs, Tests Health or Unique Errors.
- **Debug view**: historical runs switcher, video (plus Browser Session), audio, Test vs. Session view, a Steps list that jumps on the timeline, inline screenshots, a Logs tab (terminal, framework, device and network logs, with severity filters and search), a Network tab, app logs, hierarchical steps, and in-place Jira.
- **Info view**: Smart Tags, run info, test code with an SCM link, capabilities.
— [BrowserStack Timeline Debugging](https://www.browserstack.com/docs/test-reporting-and-analytics/features/timeline-debugging)
- **Azure Playwright Workspaces** embeds the Trace Viewer, with before/after state, logs, DOM, network, errors and console per step — [MS Tech Community](https://techcommunity.microsoft.com/blog/appsonazureblog/introducing-reporting-in-playwright-workspaces/4487059)
- **Datadog**:
- Error Message section with **View Code** (VS Code, IntelliJ or GitHub).
- Source Code section.
- Distributed backend traces in test details.
- **Failed Test Replay** captures local variables.
— [Datadog developer workflows](https://docs.datadoghq.com/tests/developer_workflows/); [Datadog Tests](https://docs.datadoghq.com/tests/)
- **Currents**: a Run Attachments page, plus a Trace API (summary, player, frames, filmstrips, network) — [Currents docs index](https://docs.currents.dev/llms.txt)
- **Cypress Branch Review** shows base vs. feature side by side, with per-attempt Test Replay, screenshots and video, and a code diff — [Cypress Branch Review](https://docs.cypress.io/cloud/features/branch-review)

### Inferences
- A strong BlazorTestPlatform test detail page could have:
- Header: story, component, browser, status, attempt pills.
- Left: steps with a duration waterfall.
- Center: screenshot/filmstrip with a before/after toggle.
- Right tabs: Error, Console, Network, Visual Diff (overlay slider), Accessibility (axe by severity), Render Metrics, Metadata.
- Bottom: a history strip.
- Links: "Open trace" (deep link to trace.playwright.dev or a self-hosted viewer with `?trace=`) and "Copy as prompt."
- Attempts side by side and base vs. branch for the same story are the two most-cited debugging accelerators.

### Gaps
- The Playwright HTML report's per-test layout (retry tabs, status filter chips) is not source-verified here.
- The Allure 3 Awesome test-detail layout is unconfirmed.

## Q5. What live run progress features exist (real-time dashboards while CI runs, cancel and fail-fast)?

### Takeaway
Hosted orchestrators (Currents, Cypress) provide Running/Failing states, per-machine progress, in-place-updating PR comments and statuses, manual or automatic cancel, and fail-fast. OSS tools added watch/live modes (Allure 3 `watch`, Playwright trace `live`).

### Cited Findings
- **Currents run statuses**:
- Running.
- **Failing**: not all results reported, but failures exist.
- Failed, Passed.
- Interrupted: "👤 Cancelled" (manual), "⚡️ Cancelled" (Fail Fast), Timeout.
- The header shows Progress (completed vs. total specs).
— [Currents Run Details](https://docs.currents.dev/dashboard/runs/run-details.md)
- Currents also has a Run Progress page (across CI machines), Cancelling Runs, cancel on GitHub Actions workflow cancellation, the `currents run cancel` CLI and a Failing Fast guide — [Currents docs index](https://docs.currents.dev/llms.txt)
- The Currents PR comment shows progress while the run is going. The commit status stays pending until done — [Currents GitHub](https://docs.currents.dev/resources/integrations/github.md)
- **Cypress Smart Orchestration**: Parallelization, Load Balancing, Spec Prioritization (failed specs first), Auto Cancellation (failure threshold), Run Cancellation API, and Re-run optimization (experimental, failed specs only) — [Cypress Smart Orchestration](https://docs.cypress.io/cloud/features/smart-orchestration/overview)
- **Allure 3**: `watch` refreshes the report live. Quality gate `fastFail` is available (for example `maxFailures: 5, fastFail: true`) — [allure3 GitHub](https://github.com/allure-framework/allure3); [Allure 3 configure](https://allurereport.org/docs/v3/configure/)
- **Playwright**: `live` trace option (v1.59). UI Mode watch mode — [Playwright release notes](https://playwright.dev/docs/release-notes); [Playwright UI Mode](https://playwright.dev/docs/test-ui-mode)
- **ReportPortal**: widgets auto-refresh every 1 minute (30 seconds in fullscreen) — [ReportPortal Widget Creation](https://reportportal.io/docs/dashboards-and-widgets/WidgetCreation/)
- **Allure TestOps**: allurectl "streams data from CI" — [Allure TestOps docs](https://docs.qameta.io/allure-testops/)

### Inferences
- For a demo, simulate live mode:
- A Running/Failing run with a progress bar.
- Per-worker lanes.
- A streaming results list (SignalR fits Blazor).
- A Cancel button and a fail-fast badge.
- Distinguish Failing (in progress) from Failed (final).

### Gaps
- Run Progress lane/ETA UI details were not fetched.
- Azure documents results appearing "as soon as tests finish," with no live streaming.

## Q6. How do they integrate with PRs and CI (status checks, PR comments, Slack/Teams, GitHub Actions summaries)?

### Takeaway
The standard set is:
- A commit status or required check (optionally gated on flaky count).
- One PR comment, updated in place, with failed and flaky tests and artifact links.
- Optional inline comments on failing lines.
- Slack/Teams notifications.
- GitHub Actions annotations.

PR comments increasingly include an AI "Fix" prompt.

### Cited Findings
- **Currents GitHub App**:
- Commit status (can be a required check).
- Single updating run comment: groups, failed tests with errors, flaky tests, 30-day failure/flaky frequency per test, and Fix links to trace, screenshots and video.
- **Fix with AI** prompt.
- Optional inline comments on failing lines of changed specs.
- Legacy OAuth: status only, but works with GitHub Enterprise Server.
— [Currents GitHub](https://docs.currents.dev/resources/integrations/github.md)
- Currents also supports GitLab, Slack (App or webhook), Microsoft Teams and HTTP webhooks — [Currents docs index](https://docs.currents.dev/llms.txt)
- **Cypress**:
- GitHub flake status checks.
- PR comments with a "Flakiness" section.
- Run Checks gate with `run.flakyTests.max`.
- GitLab/Bitbucket `cypress/flake` status.
- Slack/Teams "Flaky tests" notifications.
— [Cypress Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management)
- The Cypress PR comment has a Test Results table with a "View all changes introduced in this branch" link, plus accessibility info (score, failed elements, rules by critical/serious/moderate/minor) — [Cypress Branch Review](https://docs.cypress.io/cloud/features/branch-review)
- **Datadog**: GitHub PR comments (test summary, flakiness, failed-test errors; github.com only). An EFD PR Gate can block merges — [Datadog developer workflows](https://docs.datadoghq.com/tests/developer_workflows/); [Datadog EFD](https://docs.datadoghq.com/tests/flaky_test_management/early_flake_detection/)
- Datadog notifications (email or Slack, routed by code owner) fire on: new flaky test on the default branch, quarantine/disable by policy, manual change, fix success or failure, and a weekly digest (beta) — [Datadog Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- **Playwright**: the `github` reporter adds annotations (not recommended with matrix builds). Blob plus `merge-reports` combines shards — [Playwright reporters](https://playwright.dev/docs/test-reporters)
- **Allure 3**: `slack` and `jira` plugins. The quality gate sets a non-zero exit code. Muted and accepted results are ignored by gates — [Allure 3 configure](https://allurereport.org/docs/v3/configure/)
- Allure 3 gate rules: maxFailures, minTestsCount, successRate, maxDuration. Known issues are excluded. A search summary reports the gate does not work with `--rerun` — [Allure Quality Gate](https://allurereport.org/docs/quality-gate/)
- **BrowserStack**: Quality Gate for builds, PR merges and deploys. Email and Slack notifications — [BrowserStack TRA](https://www.browserstack.com/docs/test-reporting-and-analytics)
- **Buildkite**: Slack and webhook workflow actions (alarm/recover) — [Buildkite llms-test-engine](https://buildkite.com/docs/llms-test-engine.txt)
- **Azure**: the reporter prints a shareable report URL in CI logs — [MS Tech Community](https://techcommunity.microsoft.com/blog/appsonazureblog/introducing-reporting-in-playwright-workspaces/4487059)

### Inferences
- Build a mock PR comment like Currents/Cypress:
- A summary table: passed / failed / flaky / visual-diff / a11y.
- Failed stories with an error snippet, 30-day flake rate and links to trace, screenshot and diff.
- A branch comparison link.
- An AI Fix prompt.
- Also emit a GitHub Actions job summary. SARIF already gives code-scanning annotations for a11y findings.

### Gaps
- `$GITHUB_STEP_SUMMARY` support is not documented in any fetched vendor page.
- Teams integration details are confirmed by name only.

## Q7. How do they handle search, filtering, tagging, ownership (CODEOWNERS) and quarantine/mute?

### Takeaway
Quarantine is now a first-class state machine, most mature in Datadog (Active → Quarantined/Disabled → Attempt-to-fix → Fixed). It is often policy-driven (Datadog policies, Buildkite workflows, Currents Actions). Muting (the test runs but doesn't block CI) is preferred over skipping. Filters consistently cover branch, tag, time range, browser/project, author and status.

### Cited Findings
- **Datadog states**:
- Active.
- Quarantined (runs, non-blocking; `@test.test_management.is_quarantined:true`).
- Disabled (skipped).
- Fixed (automatic after 30 days without flakes).
- A "Fix in progress" indicator.

Policies: quarantine or disable by days active, by flaking on branches, or by a 7-day failure-rate threshold (checked every 15 minutes).

Attempt to Fix: put the key (for example `DD_ABC123`) in the commit. Datadog retries the test 20 times, then tracks it until merge, with a 14-day grace period.

Row actions: View Last Failed Test Run, View related test executions, Create Work Item. Bits Code "Generate fix" can open a PR.
— [Datadog Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- **Datadog Early Flake Detection**:
- New tests are retried up to 10 times. Tests over 5 minutes are excluded.
- Any failure means flaky.
- Facets: `@test.is_new`, `@test.is_retry`.
- A test inactive for 14+ days may count as new again.
— [Datadog EFD](https://docs.datadoghq.com/tests/flaky_test_management/early_flake_detection/)
- **Datadog Auto Test Retries** (up to N retries) and **Test Impact Analysis** (skips unaffected tests using coverage and git) — [Datadog Tests](https://docs.datadoghq.com/tests/)
- **Buildkite states**: enabled, muted (recommended), skipped. Manual quarantine applies on all branches. Automatic quarantine uses a workflow "change state" action. The API lists muted and skipped tests — [Buildkite Quarantine](https://buildkite.com/docs/test-engine/quarantine)
- **Buildkite Workflows** (Oct 2, 2025, public preview):
- Monitors: transition count, passed on retry, probabilistic flakiness (docs also list "new test").
- Actions: label, change state, Slack, webhook, Linear.
- Tag filters.
- Up to 3 per suite on Pro/Enterprise.

Conflict: docs-derived summaries say change-state requires Enterprise — [Buildkite workflows blog](https://buildkite.com/resources/blog/introducing-test-engine-workflows); contradicted by [Buildkite Quarantine](https://buildkite.com/docs/test-engine/quarantine)
- Buildkite labels can be set manually, by workflows, by tags or by API. Key:value execution tags support filtering and comparison — [Buildkite llms-test-engine](https://buildkite.com/docs/llms-test-engine.txt)
- Buildkite flaky search works by name, scope and location. Flaky Resolution was deprecated in favor of removing the label — [Buildkite changelog 229](https://buildkite.com/resources/changelog/229-search-flaky-tests-from-the-ui-or-api); [Buildkite changelog 287](https://buildkite.com/resources/changelog/287-flaky-resolution-deprecation)
- **Currents Actions**:
- Rules to skip or quarantine "conditionally and temporarily," add tags, or send alerts.
- Intelligent test selection.
- Affected Tests and Lifecycle Notifications pages.
— [Currents Actions](https://docs.currents.dev/guides/currents-actions.md)
- **Currents filters**: Date Range, Tag, Author, Branch, Group (browser), error-message search, and field syntax like `Target: "toast"` — [Currents Error Explorer](https://docs.currents.dev/dashboard/test-suite-performance-explorer/errors-explorer)
- **Allure 3**: muted resolutions with `expiresAt` and a comment. `hideLabels` hides labels, and `_`-prefixed labels are hidden by default — [Allure 3 configure](https://allurereport.org/docs/v3/configure/); [allure3 GitHub](https://github.com/allure-framework/allure3)
- **BrowserStack**: muted tests are removed from every metric — [BrowserStack Debug](https://www.browserstack.com/docs/test-reporting-and-analytics/debug)
- **Playwright**: negated filters `!@tag` and `!p:project` (v1.52). UI Mode filters by name, project, @tag and status — [Playwright release notes](https://playwright.dev/docs/release-notes); [Playwright UI Mode](https://playwright.dev/docs/test-ui-mode)

### Inferences
- A demo quarantine model:
- Per-story states Active / Muted / Skipped / Fixed.
- Auto-mute when the 7-day flake rate exceeds X.
- Auto-Fixed after N clean runs.
- Mute expiry dates.
- An owner column from CODEOWNERS or a component map.
- A log of state-change events.
- A query-chip filter bar (`status:failed browser:chromium tag:@visual owner:@team !tag:@slow`) matches syntax developers already know.

### Gaps
- Cypress Cloud's native quarantine/mute was not found in the fetched docs.
- ReportPortal's saved filters UI was not fetched.

## Q8. What AI features do they offer (failure summaries, root-cause analysis, flaky classification)?

### Takeaway
AI features in 2025-2026 fall into four groups:
1. **Per-failure RCA panels** with category, confidence and fix steps (BrowserStack Test Failure Analysis, Datadog Bits Code).
2. **Flaky root-cause categorization** (Datadog's 14 categories).
3. **"Copy as prompt" / "Fix with AI"** hand-offs (Playwright 1.51+, Currents).
4. **MCP servers** (Currents, Cypress Cloud, Allure TestOps, Buildkite).

ReportPortal's XGBoost auto-analysis predates LLMs but remains the most transparent defect classifier.

### Cited Findings
- **BrowserStack Test Failure Analysis agent**:
- Inputs: logs, screenshots, metadata, plus user-added text, diffs and files (up to 5 MB).
- Outputs: RCA summary, error summary, log evidence, **confidence, impact, how to fix, failure type**, and "View Steps to Fix."
- Opened via **Debug with AI / View AI RCA**.
- Actions: **Refine analysis** (Update RCA or Discard) and **Bulk Apply to similar failures**.
- Build-level summary with Failure themes and Affected workflows.
- Requires AI enabled by an Owner or Admin.
— [BrowserStack TFA agent](https://www.browserstack.com/docs/test-reporting-and-analytics/agents/test-failure-analysis)
- **Datadog**: AI flaky category per test. Bits Code generates fix PRs (requires ≥5% failure rate, ≥2h wasted, ≥2 failed pipelines, flakes on the default branch) — [Datadog Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- **Cypress**: a Cloud AI "Test intent" summary in the test sidebar and analytics drill-ins. Cloud MCP has `cypress_get_flaky_tests` — [Cypress Project Analytics](https://docs.cypress.io/cloud/features/analytics/project-analytics); [Cypress Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management)
- **Currents**: IDE extension, local or remote MCP server, Trace API for agents, Agent Evidence, and Fix with AI in PR comments — [Currents docs](https://docs.currents.dev/); [Currents docs index](https://docs.currents.dev/llms.txt); [Currents GitHub](https://docs.currents.dev/resources/integrations/github.md)
- **Playwright**: "Copy as prompt" (v1.51), Test Agents planner/generator/healer (v1.56), `--debug=cli` and CLI trace analysis (v1.59) — [Playwright release notes](https://playwright.dev/docs/release-notes)
- **Allure**: Allure 3 `agent` command (markdown plus JSON manifests, expectation flags). Allure TestOps MCP server — [allure3 GitHub](https://github.com/allure-framework/allure3); [Allure TestOps docs](https://docs.qameta.io/allure-testops/)
- **ReportPortal**: ML Suggestions in "Make decision," and XGBoost auto-analysis retrained per project — [ReportPortal Auto-Analysis](https://reportportal.io/docs/analysis/AutoAnalysisOfLaunches/); [ReportPortal Manual Analysis](https://reportportal.io/docs/analysis/ManualAnalysis/)
- **Buildkite**: an MCP server with Test Engine tools. No AI features are mentioned in the fetched Test Engine content — [Buildkite Test Engine docs](https://buildkite.com/docs/test-engine)

### Inferences
- A self-hosted demo can show AI-style value without an LLM:
1. A "Copy as prompt" button bundling story, error, failing step, screenshot reference, axe violations and history.
2. A rule-based suggested category with a confidence label.
3. An MCP/JSON endpoint like Allure 3's agent manifest.
- BrowserStack's RCA card (summary, evidence, confidence, fix steps, refine/accept) is a good template if an LLM is added later.

### Gaps
- No vendor publishes accuracy benchmarks for its AI categorization in official docs.
- Allure TestOps native AI analysis and Azure reporting AI features were not found in the fetched sources.