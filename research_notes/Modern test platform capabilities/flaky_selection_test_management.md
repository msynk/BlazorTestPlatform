# Flaky-test management, test selection/acceleration, and test case management platforms (2025-2026)

Scope: (a) flaky test detection and quarantine products, plus published engineering approaches; (b) predictive and impact-based test selection, distribution, and orchestration; (c) test case management (manual and automated). The notes are meant to guide a self-hosted showcase and platform improvements for BlazorTestPlatform, which already has cross-run flake history, retry-based flaky classification, story-level TIA (changed files -> affected stories), sharding, and a manual WCAG review workflow committed as JSON. Research date: 2026-11-10.

Renames and acquisitions to keep straight:
- Launchable is now CloudBees Smart Tests. CloudBees announced the acquisition on Aug 7, 2024.
- Gradle Enterprise is now Develocity. Docs moved from docs.gradle.com to docs.develocity.ai.
- Datadog "Intelligent Test Runner" is now "Test Impact Analysis", part of "Test Optimization" (formerly CI Visibility – Tests).
- Zephyr Scale was formerly TM4J.

---

## Q1. How is flakiness detected and presented, and what does quarantine look like in practice?

### Takeaway
Detection is converging on a few combinable signals, each modeled as a separate monitor or analyzer:
1. Fail-then-pass on the same commit or within the same retry loop.
2. Different outcomes for the same inputs across builds.
3. Failure rate over a rolling window, with per-branch thresholds.
4. Failure count in a window.
5. Optionally, a Bayesian or probabilistic score.

Products present this as a status (Healthy / Flaky / Broken, or Active / Quarantined / Disabled / Fixed) together with impact (CI time wasted, pipelines or PRs failed, flake rate, severity band) and ownership (CODEOWNERS). Quarantine almost always means the test **still runs and reports, but its failure does not gate the build**. This is done by rewriting the exit code or results, and it applies to Flaky tests only, never Broken ones. Auto-exit rules apply, for example 30 days without a flake moves a test to Fixed.

### Cited Findings

**Trunk Flaky Tests: detection model (monitors)**
- Detection is built from independent **monitors**, each active or inactive per test. When a monitor activates it runs its action (classify, or apply labels), and when it resolves it undoes that action — [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection)
- Health classification monitors, all enabled by default — [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection):
  - **Pass-on-Retry**: fails, then passes on the same commit. Branch-agnostic.
  - **Failure Rate**: failure % above a threshold over a time window. Can be scoped by branch, so stable, PR, and merge-queue branches get different thresholds.
  - **Failure Count**: N failures in a rolling window. Recommended for `main` and merge queues, where any failure matters.
- Label-only lifecycle monitors that don't affect status — [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection):
  - New Test (grace period) and Skipped Test are on by default.
  - Slow Test and **Timeout Inflation** (failing duration much larger than passing duration) are off by default.
- Status precedence: **Broken > Flaky > Healthy**. A test stays in its state until every monitor that flagged it resolves. If the broken monitor resolves while a flaky one is still active, the test becomes Flaky — [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection)
- **Muting a monitor per test**:
  - Durations: 1h, 4h, 24h, 7d, or 30d.
  - A muted monitor still records detections but is excluded from status.
  - "Flag as Flaky" can't override Broken.
  - Source: [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection)
- **Dry run**: create a monitor that only applies a label (e.g. `would-be-flaky`), then switch it to "Classify test status" once the set looks right — [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection)
- **Events tab** per test case logs monitor activations, resolutions, disables, and deletes. Detection runs at most 20 minutes after upload. "Variants" split environments (e.g. iOS vs Android) into distinct test cases — [Trunk docs: Detection](https://docs.trunk.io/flaky-tests/detection)

**Trunk Flaky Tests: quarantine at runtime**
- How it works — [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining):
  - Quarantined tests **keep running and uploading**.
  - The Trunk Analytics CLI asks the backend whether each failed test is quarantined. If *all* failures in the job are quarantined, it overrides the exit code to `0`.
  - Broken tests are never auto-quarantined ("Broken tests are not quarantine candidates").
- Commands:
  - `trunk-analytics-cli test` wraps the test command (`--junit-paths`, `--test-process-exit-code` fallback when no JUnit is produced).
  - `trunk-analytics-cli upload` runs as a separate gating step (the test step uses continue-on-error).
  - `--dry-run` / `TRUNK_DRY_RUN=true` writes the bundle locally.
  - GitHub Action: `trunk-io/analytics-uploader@v2`.
  - Source: [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining)
- Settings, at repository and collection level — [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining):
  - Enable Test Quarantining, the master toggle. On its own it quarantines nothing.
  - Auto-Quarantine Flaky Tests.
  - Manual Quarantine Permissions (Anyone / Admins only).
  - Manual Test Status Override Permissions.
  - Summary Flaky Tests Reports (PR comments, on by default).
- Manual overrides — [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining):
  - **Always Quarantine** quarantines even a healthy test.
  - **Never Quarantine** blocks quarantine even when the test is flaky.
  - Setting either requires a comment. Both are available from the test detail page and from the table's ⋮ menu.
- Audit and visibility — [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining):
  - Events tab: author and timestamp for every override.
  - Test History filter "Quarantined: Only".
  - **Quarantining tab**: 30-day chart of quarantined jobs per day, plus a jobs table with branch filter.
  - Webhook `test_case.quarantining_setting_changed`.
  - API to list quarantined tests.
  - A "Create ticket" row action exists, but the fetched page doesn't document its destination.
- Sharded runs caveat: wrap each invocation or upload everything at a final gate. Otherwise a build error can be misread as a quarantined test failure — [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining)

**Datadog Flaky Test Management (Test Optimization)**
- Flaky definition and tags — [Datadog: Flaky tests](https://docs.datadoghq.com/tests/flaky_test_management/):
  - A test "passes and fails across multiple runs for the same commit."
  - Tags: `is_flaky`, `is_new_flaky` (first flake on current/default branch), `is_known_flaky`.
- States — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/):
  - **Active**: known flaky and running.
  - **Quarantined**: runs, but failures don't affect CI.
  - **Disabled**: skipped in CI.
  - **Fixed**: set automatically after a confirmed fix merges, or after **30 days without flaking**.
  - **Fix in progress**: an indicator, not a state.
- State rules — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/):
  - Disabled never auto-moves to Fixed.
  - A **100% failure rate over the last 7 days** is treated as "broken" (`broken_test:true`) and is not auto-fixed.
- Policies, configured per repository — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/):
  - **Quarantine** triggers: time (Active for N days, evaluated daily at 12:15 UTC), branch (flakes on specified branches), or failure rate (≥ threshold over 7 days, evaluated every 15 min).
  - **Disable** triggers: time, branch, or failure rate.
  - **Fixed**: built in, after 30 days with no flakes.
- **Attempt to Fix** workflow — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/):
  - Put the test key (e.g. `DD_ABC123`) in a commit message. The test is then retried **20 times** by default, even if Disabled.
  - The last retry is tagged `attempt_to_fix_passed:true/false`.
  - After a successful attempt merges, a **14-day grace period** follows during which Active/Quarantined tests behave as Quarantined.
- Presentation — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/):
  - Table columns: state, **pipelines failed**, **CI time wasted**, failure rate.
  - Row actions: View Last Failed Test Run, View related executions, Create Work Item.
  - Stable test ID: `fingerprint_fqn`.
  - Metric `test_optimization.test_management.flaky_tests`, tagged by `flaky_status`, `test_codeowners`, `flaky_category`.
- **AI root-cause categories**: Concurrency, Randomness, Floating Point, Unordered Collection, Too Restrictive Range, Timeout, Order Dependency, Resource Leak, Asynchronous Wait, IO, Network, Time, Environment Dependency, Unknown. Requires `@error.message` and `@error.stack` — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- Ownership and notifications — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/):
  - Owners come from `CODEOWNERS`, updated on each default-branch run.
  - Notification events: new flaky test on default branch, state change, fix success/failure, weekly digest (beta).
  - Channels: email or Slack, routed by code owners.
- "Bits AI Fixes" eligibility thresholds: failure rate ≥5%, wasted time ≥2h, ≥2 failed pipelines, flaked on the default branch — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- .NET tracer versions: Quarantine/Disable needs **3.13.0+**, Attempt to Fix needs **3.23.0+** — [Datadog: Flaky Test Management](https://docs.datadoghq.com/tests/flaky_management/)
- Older flaky-tests page details — [Datadog: Flaky tests](https://docs.datadoghq.com/tests/flaky_test_management/):
  - Columns: First flaked / Last flaked (with SHAs), Commits flaked, failure rate since first flake, Trend.
  - Tests that haven't failed in 30 days are removed automatically.
  - The default branch view scans the most recent 5,000 commits.

**Cypress Cloud flaky analytics**
- Detection is via **test retries** (`retries: { runMode: 2, openMode: 0 }`). "A flaky test passes and fails across retries without any code change." Requires a Team plan or above — [Cypress docs: Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management)
- **Severity bands** use flake rate, the share of runs in the window where the spec had a flaky test — [Cypress docs: Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management):
  - **Low**: >0–10%
  - **Medium**: >10–50%
  - **High**: >50%
- "Failure rate vs flake rate": a test can have a 0% final failure rate and still be flaky — [Cypress docs: Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management)
- Flaky Test Analytics page — [Cypress docs: Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management):
  - Flaky-count-over-time chart, overall flakiness level, counts by severity.
  - Filterable log ordered by severity.
  - Per-test panel: test intent summary (AI), historical flaky runs, most common errors, changelog, failure-rate-vs-flake-rate plot.
- Alerting and gating — [Cypress docs: Flaky Test Management](https://docs.cypress.io/cloud/features/flaky-test-management):
  - GitHub status checks and PR comment "Flakiness" section.
  - Gating via `run.flakyTests.max`.
  - GitLab/Bitbucket: `cypress/flake` commit status.
  - Slack/Teams: "Flaky tests" notification.
  - **Branch Review** labels flake as new, existing, or resolved versus the base branch.

**Gradle Develocity**
- Within-build: a test is marked `FLAKY` if it fails and succeeds within the same Gradle task, Maven goal, Bazel target, or sbt task. Cross-build: different outcomes for the **same task/goal inputs** across builds, marked with an icon next to the outcome (added in 2023.4). Detection doesn't require retries — [Develocity flaky test detection](https://docs.gradle.com/develocity/flaky-test-detection); [Develocity 2023.4 release](https://gradle.com/develocity/legacy-releases/2023.4)
- Remediation can be prioritized by frequency, recency, and impact over a custom set of builds, using Test Failure Analytics — [Develocity product tour](https://gradle.com/develocity/product-tour/troubleshoot/flaky-test-detection)
- Retry config: Gradle `testRetry { maxRetries, maxFailures, failOnPassedAfterRetry }` (plugin 3.12+). Maven: `rerunFailingTestsCount` — [Develocity Test Distribution docs](https://docs.develocity.ai/test-distribution/)

**Buildkite Test Engine**
- Test state flags: **enabled / muted / skipped**. Muted tests run, their failures don't fail the build, and data is still collected (recommended). Skipped tests don't run, so recovery can't be detected — [Buildkite: Test state & quarantine](https://buildkite.com/docs/test-engine/test-suites/test-state-and-quarantine)
- Automatic quarantine uses the **"Change state" workflow action** (alarm → muted/skipped, recover → enabled; Enterprise plan). Labels can be added on quarantine and removed on release. Manual quarantine is available from the test page or digest. bktec enforces state at runtime, or the REST API `tests` endpoint can be used — [Buildkite: Test state & quarantine](https://buildkite.com/docs/test-engine/test-suites/test-state-and-quarantine)

**BuildPulse** (docs from a `pages.dev` mirror, not an official domain)
- Two detection modes — [BuildPulse docs (mirror)](https://buildpulse-docs.pages.dev/flaky-tests/guides/Flaky%20Tests%20Dashboard):
  - **Code/git-based**: compares git tree SHAs, i.e. pass/fail on identical code.
  - **Statistical**: failure rate and count above the "Detection Disruption Percentage" threshold.
- Disruption metrics — [BuildPulse docs (mirror)](https://buildpulse-docs.pages.dev/flaky-tests/guides/Flaky%20Tests%20Dashboard):
  - **Disruptions** are pass/fail flips (git-based) or failures (statistical).
  - **Disruption Frequency** = disruptions / total results × 100.
  - Total build time consumed by flakiness is also tracked.

**Published engineering approaches**
- **Google (2016)**: ~1.5% of test runs produced a flaky result and ~16% of tests had some flakiness. Mitigations: re-run only failing tests, auto re-run on failure, and quarantine when flakiness gets too high. These figures come via the secondary citation in [Lam et al. 2019, "Root Causing Flaky Tests in a Large-Scale Industrial Setting"](https://cs.gmu.edu/~winglam/publications/2019/LamETAL19RootFinder.pdf); the original Google Testing Blog post was not fetched.
- **Slack "Project Cornflake" (post dated Apr 5, 2022)**
  - V1 flakiness = failed runs / last N main-branch runs (example N=50), with per-test-type thresholds (E2E > functional > unit). Tests with no history were treated as flaky. Flaky results were stripped from the XML so the job went green. This was rolled back because it masked new breakages — [Slack Engineering](https://slack.engineering/handling-flaky-tests-at-scale-auto-detection-suppression)
  - V2 classification: "failing" fails consistently across reruns, "flaky" eventually passes on rerun. Backend, API, crash, and infra failures are excluded — [Slack Engineering](https://slack.engineering/handling-flaky-tests-at-scale-auto-detection-suppression)
  - V2 suppression flow: owner lookup → find or create a Jira ticket → open an auto-approved PR that disables the test in code (`disabled_` prefix on iOS, `@Ignore` with Jira link on Android) → merge queue. Teams get weekly summaries — [Slack Engineering](https://slack.engineering/handling-flaky-tests-at-scale-auto-detection-suppression)
  - Results: main stability 19.82% → 96%, test job failure rate 56.76% → 3.85%, 553 triage hours saved (as of Feb 2021). Auto re-enable was listed as future work — [Slack Engineering](https://slack.engineering/handling-flaky-tests-at-scale-auto-detection-suppression)
- **Atlassian "Flakinator" (post dated Dec 8, 2025)**
  - Retry-based detection: implicit retries stop at the first pass/fail "flip". Retry counts are configurable per test type, and the CLI first checks whether the test is already flagged — [Atlassian Engineering](https://www.atlassian.com/blog/atlassian-engineering/taming-test-flakiness-how-we-built-a-scalable-tool-to-detect-and-manage-flaky-tests)
  - **Bayesian inference**: moving-window history serves as the prior, giving a posterior probability of flakiness. Additional signal processors cover duration variability, environment consistency, result patterns, and retry frequency. The output is a **0–1 flakiness score** — [Atlassian Engineering](https://www.atlassian.com/blog/atlassian-engineering/taming-test-flakiness-how-we-built-a-scalable-tool-to-detect-and-manage-flaky-tests)
  - Quarantined tests keep running in branch, scheduled, or dedicated quarantine pipelines, and are released after staying healthy for a configured period. Jira tickets carry due dates and are routed via code ownership — [Atlassian Engineering](https://www.atlassian.com/blog/atlassian-engineering/taming-test-flakiness-how-we-built-a-scalable-tool-to-detect-and-manage-flaky-tests)
  - Scale: 350M+ executions/day, 22,000+ builds recovered and 7,000 unique flaky tests in a quarter — [Atlassian Engineering](https://www.atlassian.com/blog/atlassian-engineering/taming-test-flakiness-how-we-built-a-scalable-tool-to-detect-and-manage-flaky-tests)
- **Meta** uses a Bayesian "Probabilistic Flakiness Score" computed with Stan. This is a secondary summary — [Trunk "How Meta tests their flaky tests"](https://trunk.io/learn/how-meta-tests-their-flaky-tests)
- **Uber**
  - 2021 Test Analyzer Service: a test with 100 consecutive successful main-branch runs is stable, the rest are flaky, and flaky tests are periodically disabled from gating CI — [Uber 2021, Handling Flaky Unit Tests in Java](https://www.uber.com/en-CA/blog/handling-flaky-tests-java)
  - 2024 "Testopedia": an FSM with states **new, stable, unstable, disabled, deleted**. Entering *unstable* files a Jira ticket and entering stable or deleted closes it — [Uber Engineering, Flaky Tests Overhaul (Jun 4, 2024)](https://www.uber.com/en-US/blog/flaky-tests-overhaul/)
  - Pluggable analyzers — [Uber Engineering, Flaky Tests Overhaul (Jun 4, 2024)](https://www.uber.com/en-US/blog/flaky-tests-overhaul/):
    - **Linear**: one failure in the last X runs → unstable; N consecutive passes → stable; per-test timeout threshold M.
    - **Landing-CI retry signal**: fail-then-pass on an identical retry, kept in a separate import stream.
    - **Percentage-based**: failure rate over the last N runs above a threshold.
  - Read API returns flakiness state, reliability, **staleness**, aggregated execution time, and per-test state-change history. Jira tickets are grouped by build target or regex and carry deadlines. Flaky integration tests run in non-blocking "FYI" mode — [Uber Engineering, Flaky Tests Overhaul (Jun 4, 2024)](https://www.uber.com/en-US/blog/flaky-tests-overhaul/)
  - Counts: about 1,000 flaky out of 600K tests (Go monorepo) and about 1K out of 350K (Java) — [Uber Engineering, Flaky Tests Overhaul (Jun 4, 2024)](https://www.uber.com/en-US/blog/flaky-tests-overhaul/)

### Inferences
- **Common detection core** for a self-hosted platform. All three signals are already derivable from BlazorTestPlatform's cross-run history:
  1. Same commit (or same input fingerprint) produced both pass and fail. This is the strongest signal (Trunk Pass-on-Retry, Datadog, BuildPulse git-tree SHA, Develocity cross-build).
  2. Pass-after-retry within a run.
  3. Windowed failure rate or flip rate, with per-branch thresholds.
- **Recommended status model**: Healthy / Flaky / Broken with Trunk's precedence rule (Broken wins; e.g. 100% failure over 7 days, per Datadog). Add lifecycle states Quarantined, Disabled, and Fixed (auto after N days or runs clean; Datadog uses 30 days, Uber uses N consecutive passes).
- **Showcase UX patterns worth copying**:
  - Severity bands (Cypress Low ≤10% / Medium ≤50% / High >50%).
  - Impact columns: CI time wasted = duration of failed attempts plus reruns; runs or PRs failed; first and last flaked commit.
  - Per-test Events/audit tab.
  - A "would-be-flaky" dry-run label.
  - Muting with expiry (1h–30d).
  - Heuristic root-cause categories from error-message regexes (Timeout, Async wait, Order dependency, Network, ...), a non-AI analogue of Datadog's categories.
- **Quarantine for a single-repo, file-backed platform**: commit a `quarantine.json` (test id, reason, owner, ticket URL, created_at, expires_at, mode mute|skip). The runner keeps executing muted tests and rewrites the exit code only when *all* failures are quarantined (Trunk and Buildkite semantics).
  - Slack's history shows that hiding results (V1) masks breakages. Muting (still run, still record) is the safer pattern.
  - The committed-file approach mirrors Slack V2's in-code disabling and BlazorTestPlatform's existing WCAG JSON workflow, so it stays reviewable in PRs.
- An "Attempt to fix" mode is cheap to implement locally: rerun a single test N=20 times and record pass/fail. It is a strong demo moment.
- A Beta-Binomial posterior (Atlassian/Meta-style Bayesian score) is easy to compute from local pass/fail counts. It gives a 0–1 flakiness score with an uncertainty band, which is better than raw rates for tests with few runs.

### Gaps
- Exact numeric defaults for Trunk's Failure Rate and Failure Count monitors (window, %, count) are on per-monitor pages that weren't fetched. Trunk's ticketing (Jira/Linear) page failed to load (socket error), so ticket contents and status sync are unverified.
- BuildPulse's official domain docs weren't reachable. Findings come from a `pages.dev` mirror, and the "disruptiveness" ranking formula wasn't found.
- The Google 2016 Testing Blog original wasn't fetched (figures are via Lam et al. 2019). Spotify's flakiness work (e.g. "Flakybot") wasn't researched in this pass.
- The Datadog Early Flake Detection and Auto Test Retries pages weren't fetched (defaults such as the number of retries for new tests are unverified).
- The Develocity flaky-test detection page timed out. Details come from search snippets of that page and the 2023.4 release notes.

---

## Q2. How do predictive selection tools present confidence and risk, and how is selection explained per test?

### Takeaway
There are two families:
- **ML/semantic predictive selection** (Develocity PTS, CloudBees Smart Tests) presents a trade-off: a **confidence curve** (test time vs probability of catching a failing session) or **profiles** (Conservative/Standard/Fast). A **simulator** replays history to report "% failures predicted" vs "savings".
- **Deterministic impact analysis** (Datadog TIA via per-test coverage, Nx affected via the project/task graph) presents time saved, plus explicit "run everything" escape hatches and `--explain` output.

Per-test explanations exist (Develocity per-test prediction explanation, Nx `--explain`) but are generally terse. "Always run" rules (new, changed, recently failed or flaky, must-run annotations, tracked files, default branch) are universal.

### Cited Findings

**Develocity Predictive Test Selection (PTS)**
- Model inputs — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/):
  - Build Scan history of code changes and test outcomes: which changes affect a module's tests, test sensitivity to change, nature of recent changes, flakiness, and the historical relationship between test and code.
  - Initially trained on partner data (millions of executions).
- **Always selected**: tests that are recent new, changed, failed, or flaky. **Less likely selected**: tests that already passed for the same sources and dependencies — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)
- **Minimum data**: 14 days of change history and ≥50 executions over 14 days of the target tests. Below that, it runs everything except tests that already passed for identical inputs — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)
- **Profiles** (2023.2+): `CONSERVATIVE` (more tests, smaller savings), `STANDARD` (default), `FAST` (higher threshold, fewer tests). Override with `-Dpts.profile` — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)
- **Modes**: `RELEVANT_TESTS` (default, fast feedback) and `REMAINING_TESTS` (the complement, for post-merge, nightly, or pre-release) — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)
- **Must-run**: `includeClasses` (wildcards) and `includeAnnotationClasses`, or the shared `@MustRun` annotation. Filtering with `--tests` forces matching tests to be selected — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)
- **Simulator** (in Build Scans and dashboards) — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/):
  - Metrics: **% Task/Goal failures predicted**, **% Test failures predicted**, **Savings potential** (serial time), **Avoidable tests** (would be NOT SELECTED), **Unavoidable tests**.
  - A toggle compares profiles.
  - Detected flaky failures are excluded from predicted-failure metrics. Undetected infra flakes count as "incorrect".
  - Claims to catch >99% of non-flaky task/goal failures.
- Savings reporting — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/):
  - Build log line: "Predictive Test Selection: 5 of 22 test classes selected with profile 'Standard' (saving 23m 45s serial time)."
  - The Build Scan summary shows selected count and saved time.
  - The PTS dashboard **Usage view** shows trends and runs where selection was unavailable, with the reason.
- **Per-test explanation** — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/):
  - Test details show "a logical prediction explanation" for each SELECTED and NOT SELECTED class.
  - NOT SELECTED means "unlikely to give a useful signal".
  - Outcomes are searchable in the Tests view.
  - Missed failures are handled with a git-bisect-style investigation, then adding the test to must-run.
- **Build Cache interplay**: results are cached only if all tests were selected and the task succeeded. Measure coverage only in non-PTS builds — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)

**CloudBees Smart Tests (formerly Launchable; acquired Aug 7, 2024)**
- Acquisition announced Aug 7, 2024 — [BusinessWire press release](https://www.businesswire.com/news/home/20240807780392/en/CloudBees-Acquires-Launchable-to-Boost-GenAI-Efforts-Across-DevSecOps-Platform)
- **Confidence** = "the probability that the subset will catch a failing session" — [CloudBees docs: Choose a subset optimization target](https://docs.cloudbees.com/docs/cloudbees-smart-tests/latest/send-data-to-smart-tests/subset/choose-a-subset-optimization-target)
- Three optimization targets for `smart-tests subset` — [CloudBees docs: Choose a subset optimization target](https://docs.cloudbees.com/docs/cloudbees-smart-tests/latest/send-data-to-smart-tests/subset/choose-a-subset-optimization-target):
  - `--confidence`: fill up to the duration that maps to the chosen confidence on the curve.
  - `--time`: fixed duration, e.g. `10m`.
  - `--target`: percentage of input duration, e.g. `20%` of 100 min = 20 min. Not yet shown in the dashboard.
- **Confidence curve on the Simulate page** — [CloudBees docs: Choose a subset optimization target](https://docs.cloudbees.com/docs/cloudbees-smart-tests/latest/send-data-to-smart-tests/subset/choose-a-subset-optimization-target):
  - x = test execution time, capped at the longest evaluation session. y = confidence %.
  - The curve runs from (0, 0%) to (max, 100%) and is steep on the left.
  - Illustrative values: 90% ≈ 10 min, 95% ≈ 25 min.
- Onboarding uses an observation mode: run alongside normal CI for a window to build the curve — [CloudBees onboarding guide](https://docs.cloudbees.com/docs/cloudbees-smart-tests/latest/resources/onboarding-guide) (search snippet)
- 2026 positioning — [CloudBees blog, How Smart Tests works (Jun 29, 2026)](https://www.cloudbees.com/blog/how-cloudbees-smart-tests-works):
  - Selection uses **GenAI semantic similarity** between changed code and tests and is "available from the very first run". The confidence curve still comes from pipeline history.
  - The subset is ordered by failure probability.
  - Modes are "Test Faster" vs "Test Earlier".
  - Per-stage thresholds, e.g. 80% for PR and full suite nightly.
  - Example: 40 of 100 min catches about 95% of historical failures.
  - Vendor-reported 35–70% CI runtime reduction.
- Same 2026 post on test insights and triage — [CloudBees blog, How Smart Tests works (Jun 29, 2026)](https://www.cloudbees.com/blog/how-cloudbees-smart-tests-works):
  - Flaky tests are failures without a related code change. They are deprioritized but kept visible, with a trend shown.
  - Health trends cover failure ratios, flaky rates, and long-running tests.
  - AI triage groups failures by shared root cause, ranks groups by tests affected, links the likely culprit commit, and notifies via Slack/Jira.
- Conflicting definitions: older Launchable material describes confidence as the likelihood that a run will pass. Current docs define it as catching a failing session — [CloudBees docs](https://docs.cloudbees.com/docs/cloudbees-smart-tests/latest/send-data-to-smart-tests/subset/choose-a-subset-optimization-target); [Launchable page](https://www.launchableinc.com/run-a-subset-of-tests-to-achieve-confidence-in-a-build)

**Datadog Test Impact Analysis (formerly Intelligent Test Runner)**
- Per-test **code coverage** is cross-referenced with changed files, using a Bloom filter with about a 0.04% false-positive rate (so a few skippable tests still run) — [Datadog TIA docs](https://docs.datadoghq.com/tests/test_impact_analysis/)
- **Force-run conditions** — [Datadog TIA docs](https://docs.datadoghq.com/tests/test_impact_analysis/):
  - **Tracked files**: non-code files (Dockerfile, Makefile, dependency files) with `*`/`**` globs trigger all tests.
  - **Unskippable** tests are marked in source.
  - The **default branch** always runs everything.
  - **Excluded branches** (e.g. `release_*`).
  - `ITR:NoSkip` in a commit message or as a PR label.
  - Commits with more than 5,000 changed files.
  - A test covering more than 16,000 files is never skipped.
- Presentation: purple "time saved" text per session or commit, purple duration bars, and an out-of-the-box dashboard of savings by repository, committer, and service. Coverage backfill for skipped tests is supported for .NET (`dd-trace-dotnet`) — [Datadog TIA docs](https://docs.datadoghq.com/tests/test_impact_analysis/)

**Nx affected (deterministic graph-based)**
- Mechanism — [Nx docs: affected](https://nx.dev/docs/features/ci-features/affected):
  - Git diff (`--base`/`--head`, `NX_BASE`/`NX_HEAD`; recommended base is the last successful `main` commit, via `nrwl/nx-set-shas`) selects tasks whose **inputs** include a changed file, plus tasks consuming their outputs.
  - `namedInputs` (e.g. `production` excluding `*.spec.ts`) control what counts.
  - A lock-file change affects everything unless `projectsAffectedByDependencyUpdates: "auto"`.
- **Explainability**: `nx affected -t <targets> --explain` prints why each task is affected (`--explain=stdout` for JSON). `--graph` visualizes without running. `nx show projects --affected` lists projects — [Nx docs: affected](https://nx.dev/docs/features/ci-features/affected)
- Nx 23 and earlier use project-based affected (`affected.legacy`). "Ultracache" selects from the files each task actually read in CI — [Nx docs: affected](https://nx.dev/docs/features/ci-features/affected)

### Inferences
- **What BlazorTestPlatform's story-level TIA lacks** relative to these tools:
  1. A **simulator/backtest**: for every historical run that had a real (non-flaky) failure, would the TIA selection have included the failing test? This yields "% real failures caught" vs "% test time saved", which is Develocity's metric pair.
  2. A local **confidence curve**: order tests by a deterministic score (affected-story match first, then recent failure, then flakiness, then duration) and plot cumulative time vs cumulative failures caught across history.
  3. **Per-test reasons** in Nx `--explain` style, e.g. "selected: `Button.razor` changed → story `Button/Primary`"; "selected: failed in last 3 runs"; "selected: must-run tag"; "not selected: no changed input; passed for identical inputs at <sha>".
  4. Escape hatches: tracked files that force full runs (`*.csproj`, `Directory.Build.props`, `global.json`, `package.json`, theme/CSS tokens), a commit token like `[run-all]`, a `[MustRun]`/trait attribute, and always running all tests on the default branch.
- **Wording to emulate**: Develocity's "N of M selected (saving X serial time)" line and the Conservative/Standard/Fast profile switch work well for a demo UI and need no ML. Profiles can map to deterministic thresholds, e.g. Conservative = affected + dependents + all recently failed/flaky; Fast = directly affected only.
- **Feasibility**: ML/GenAI selection is not realistic for a self-hosted single repo. Develocity alone requires ≥14 days and ≥50 executions per test. Ordering a subset by historical failure probability (CloudBees "test earlier") is cheap and realistic.

### Gaps
- Exactly how Develocity phrases the per-test "logical prediction explanation" (sample strings) wasn't captured.
- CloudBees Smart Tests "Test trends and insights" and "Observe subset behavior" pages weren't fetched. Exact insight screen names (e.g. "Unhealthy tests", "Flaky tests" scoring) are unverified for 2025-2026.
- Bazel affected-target tooling (e.g. `bazel-diff`, `target-determinator`) wasn't researched in this pass.
- Datadog TIA doesn't document a per-test skip reason. None was found.

---

## Q3. How do orchestration tools balance shards using historical durations?

### Takeaway
All surveyed tools estimate per-test or per-file duration from recent history and either:
- (a) **assign dynamically from a shared queue, longest first** (Cypress), or
- (b) **pre-compute balanced partitions** (Buildkite bktec, Develocity Test Distribution).

They visualize balance with partition or machine timelines (estimated vs actual) and machine-count recommendations. Auto-cancellation and failed-first ordering are layered on top.

### Cited Findings
- **Cypress Cloud load balancing** — [Cypress docs: Load balancing](https://docs.cypress.io/cloud/features/smart-orchestration/load-balancing):
  - Estimates each spec's duration from previous recorded runs, **per browser**.
  - Assigns the **longest specs first**.
  - Machines **pull the next spec from a shared queue** rather than receiving fixed lists.
  - Old history is excluded so estimates stay accurate.
  - Enabled by `cypress run --record --parallel`.
- Cypress add-ons — [Cypress docs: Load balancing](https://docs.cypress.io/cloud/features/smart-orchestration/load-balancing):
  - **Spec Prioritization** runs specs that failed in the previous run first.
  - **Auto Cancellation** cancels remaining specs after a configured failure count.
  - **Run Duration analytics**: average duration, average concurrency, time saved.
  - **Machines View**: do machines finish together?
  - **Recommendations panel**: modeled run time at different machine counts.
- **Buildkite Test Engine Client (bktec)** — [Buildkite: Test splitting](https://buildkite.com/docs/test-engine/test-splitting):
  - Splits tests across parallel jobs ("partitions") from historical timing and re-balances continuously.
  - Example: 16 min total over 4 partitions ≈ 4 min, vs a 10-min slowest partition unsplit.
  - Partition timeline "Breakdown" dropdown: **Actual** (default; recorded durations, falling back to estimated) vs **Estimated** (from the test plan).
  - Hover shows each test's estimated vs actual %. A "Show Retries" toggle is available.
  - Test splitting is on all plans, test state on Pro/Enterprise.
- bktec also enforces mute/skip states and provides retry support — [Buildkite: Test state & quarantine](https://buildkite.com/docs/test-engine/test-suites/test-state-and-quarantine)
- **Develocity Test Distribution** — [Develocity Test Distribution docs](https://docs.develocity.ai/test-distribution/):
  - Builds balanced partitions with similar expected run times from historical execution data. Partition size is capped by `forkEvery` (Gradle) or `reuseForks=false` (one class per partition, Maven).
  - Executors: local (`maxLocalExecutors`, default from `maxParallelForks`/`forkCount`) and remote (`maxRemoteExecutors`).
  - `remoteExecutionPreferred` with `waitTimeout` (default 30s).
  - A disconnected agent's partition is rescheduled.
  - `localOnly`/`remoteOnly` matchers and a test dry run are supported.
  - Build Scan **Test Distribution tab**: Configuration; Execution (executors used, avg/max concurrency); Performance (discovery time, wait time, longest test class, file transfer).

### Inferences
- The surveyed tools describe two partitioning approaches:
  - **Static**: a greedy longest-first algorithm (LPT) assigns each test to the currently lightest shard. This is deterministic and works with CI matrix jobs.
  - **Dynamic**: shards pull work from a shared queue. This is better on a single machine with N parallel workers.
- Both need a fallback estimate for tests with no history, such as the median duration or a per-story-type default. This is inferred; no surveyed doc states its fallback.
- **Showcase visuals** to build from local data:
  1. A shard Gantt chart with estimated vs actual bars (Buildkite).
  2. "Ideal vs actual makespan" plus imbalance % (max shard / mean shard).
  3. A machine-count recommendation curve from simulated LPT at k = 1..N shards (Cypress Recommendations).
  4. Failed-first ordering and an auto-cancel threshold (`--bail N`) as runner options.
- Retries and quarantined tests should count toward shard duration estimates. Otherwise shards with flaky tests run long (Buildkite shows retry segments separately).

### Gaps
- None of the fetched pages state how many historical runs feed duration estimates, the age cutoff, or the estimate used for brand-new tests (Cypress, Buildkite, and Develocity all leave it unspecified).
- Cypress Spec Prioritization and Auto Cancellation detail pages (exact settings names) weren't fetched.
- Nx Cloud Agents / "Atomizer" (per-file e2e task splitting) and Nx flaky-task auto-retry weren't confirmed. The fetched Nx page doesn't cover them.

---

## Q4. What are the core objects and screens of test case management tools, and how do they merge manual and automated results, traceability matrices, and release readiness reports?

### Takeaway
The shared object model is:
- **Test Case** (steps, expected results, fields; manual or automated).
- An organizing tree (Suite/Section/Folder).
- **Run / Execution / Cycle**: an instance of executing cases, with per-case results.
- **Plan**: groups runs, often expanded across **configurations** such as browser or OS.
- **Milestone / Version / Release**.
- **Requirement** links, usually external issues: Jira, GitHub, ADO work items.
- **Defect** links.

Automated results come in via JUnit/TRX upload CLIs or pipeline tasks that create runs and map tests to cases by name or ID. Traceability is a requirement × test matrix with computed coverage statuses. Release readiness is a milestone or plan summary covering progress, pass rate, open defects, and untested items.

### Cited Findings

**Azure DevOps Test Plans** (doc updated Aug 2026)
- Object model — [Microsoft Learn: What is Azure Test Plans](https://learn.microsoft.com/en-us/azure/devops/test/overview?view=azure-devops):
  - Work item types: **Test Plans, Test Suites, Test Cases, Shared Steps, Shared Parameters**.
  - Suites are **static**, **requirement-based**, or **query-based** (dynamic).
  - Hubs: **Test plans**, **Parameters**, **Configurations** (OS/browser/version, shared across plans), **Progress report**, **Runs**.
- Runs and results: a Test run executes one or more test cases, and each case gets its own test result. Test cases can be manual or automated. Since Azure DevOps Server 2020, pipeline test tasks don't require plans or cases — [Microsoft Learn: What is Azure Test Plans](https://learn.microsoft.com/en-us/azure/devops/test/overview?view=azure-devops)
- **Test Runner** (web): pass/fail per step, screenshots, screen recording, and image action log, with bugs auto-including diagnostics. The **Test & Feedback extension** handles exploratory testing — [Microsoft Learn: What is Azure Test Plans](https://learn.microsoft.com/en-us/azure/devops/test/overview?view=azure-devops)
- **Traceability** — [Microsoft Learn: What is Azure Test Plans](https://learn.microsoft.com/en-us/azure/devops/test/overview?view=azure-devops):
  - The `Tests/Tested by` link type connects cases to stories and features.
  - Tests can be added and run from board cards.
  - Bugs filed during testing auto-link to the requirement and build.
  - **Requirements quality widget**: every requirement in scope with test **Pass Rate** and **Failed** count (click-through to the Tests tab), and highlights requirements with no tests.
- **Progress report** answers: how much testing is complete; how many passed, failed, or blocked; is testing likely to finish on time; what is the daily execution rate; which areas need attention — [Microsoft Learn: What is Azure Test Plans](https://learn.microsoft.com/en-us/azure/devops/test/overview?view=azure-devops)
- Other reports and widgets — [Microsoft Learn: What is Azure Test Plans](https://learn.microsoft.com/en-us/azure/devops/test/overview?view=azure-devops):
  - **Deployment status widget**: environment × build matrix of pass rates.
  - **Test Results Trend (Advanced)**: daily count, pass rate, duration.
  - **Test failure** drill-down in the Analytics service.
- Automated ↔ manual: publish results with the PublishTestResults, VSTest, or .NET Core CLI tasks. An automated test method can be associated to a test case from Visual Studio or the portal ("Associate Test Case" on a pipeline run's Tests tab). The search summary states each test case has only one associated test method — [Microsoft Learn: Automated testing overview](https://learn.microsoft.com/en-us/azure/devops/test/automated-testing-overview) (search snippet; not fetched)

**Xray (Jira)**
- **Requirement coverage statuses** (computed, not user-set) — [Xray docs: coverage status calculation](https://confluence.getxray.app/display/XRAYCLOUD/Understanding+the+calculation+of+coverage+status+and+the+status+of+Tests):
  - **OK**: all linked tests PASSED.
  - **NOK**: at least one FAILED.
  - **NOTRUN**: some TODO or ABORTED, none failed.
  - **UNKNOWN**: unknown state, none failed.
  - **UNCOVERED**: no linked tests.
- Status is evaluated in a scope: **version, Test Plan, and/or Test Environment**. Tests linked to a requirement but outside the selected Test Plan can be "NOT APPLICABLE" or count toward NOTRUN depending on context — [Atlassian community thread](https://community.atlassian.com/forums/Jira-questions/Xray-reporting-Test-Coverage-using-Tasks-and-Test-Plans/qaq-p/2477762); [Xray Overall Requirement Coverage Report](https://confluence.getxray.app/display/XRAY/Overall+Requirement+Coverage+Report)

**Qase**
- **Requirements Traceability Matrix** — [Qase docs: Requirements Traceability](https://docs.qase.io/general/get-started-with-the-qase-platform/requirements-traceability-report):
  - Pulls requirements from **external trackers** (Jira Cloud and Server/DC, GitHub, GitLab, Notion, Confluence Cloud), not from Qase-native requirements.
  - It is a **point-in-time snapshot**. A summary bar shows total, linked, and unlinked requirements per project, with Linked/Unlinked tabs.
  - Each row has the requirement name, external ID, last-updated status, and counts of linked cases and defects. Expanding a row shows linked cases with their **latest run result** and defects with their status.
  - A **stale indicator** flags a requirement that changed after the linked case last ran.
  - Export as CSV or PDF.

**TestRail**
- Built-in report categories: Cases, Defects, Results, Summary — [TestRail: Reports overview](https://support.testrail.com/hc/en-us/articles/9285210470420); [TestRail Reports FAQs](https://support.testrail.com/hc/en-us/articles/9683956908436-Reports-FAQs)
  - **Cases – Coverage for References**: a coverage matrix showing which references have cases and which cases lack references.
  - **Defects – Summary**: for a milestone, plan, or selected runs.
  - **Defects – Summary for References**: defects grouped by requirement.
  - **Results – Comparison for Cases / References**: comparison matrices with passed/failed/other counts.
  - **Summary – Milestone** and **Plan (Summary)** reports.
- Automation import via **TestRail CLI (`trcli parse_junit`)**: uploads JUnit XML into a run, optionally under `--plan-id` or `--milestone-id`. Run references are managed with `--run-refs-action add|update|delete` — [TestRail CLI usage examples](https://support.testrail.com/hc/en-us/articles/12908548726804-Usage-examples); [TestRail CLI commands reference](https://support.testrail.com/hc/en-us/articles/12908518569364)

**Testmo**
- Positioning: test case management, exploratory testing (sessions and notes as "first-class"), test automation management, milestones, and reporting in one tool. Integrates with Jira, GitHub, GitLab, and CI tools. Testmo MCP is in open beta — [Testmo features](https://www.testmo.com/features/)
- `@testmo/testmo-cli` commands — [npm: @testmo/testmo-cli](https://npmjs.com/package/%40testmo%2Ftestmo-cli):
  - `automation:run:create`: create a run for parallel execution.
  - `automation:run:submit`: create, submit results, and complete in one step.
  - `automation:run:submit-thread`: add a parallel **thread** to an existing run.
- The API exposes milestones with linked run and session stats, plus manual runs and results, automation runs, automation sources, and exploratory sessions — [apis.io: Testmo](https://apis.io/providers/testmo/)

**Zephyr Scale (SmartBear)**
- Test Cases, Test Cycles (executions), and Test Plans. Cycles are linked to a plan from the plan's **Traceability tab**, and plan reports roll up linked cycles. Cases, cycles, and plans can link to any Jira issue type, and traceability reports track coverage — [SmartBear Zephyr DC: Test Plans](https://support.smartbear.com/zephyr-dc/docs/en/test-plans.html); [Zephyr Squad→Scale product comparison](https://support.smartbear.com/tm4j-cloud/docs/en/zephyr-squad-to-scale-migration-guide/product-comparison.html)
- Users report that test cycles can't be shown visually inside Zephyr Scale's traceability report — [SmartBear community](https://community.smartbear.com/t5/Zephyr-Scale-Questions/Traceability-report-in-Zephyr-Scale-not-possible-to-visually/m-p/234024/highlight/true)

### Inferences
- **Mapping to BlazorTestPlatform's domain**:
  - "Requirement" = component story, or WCAG 2.x success criterion.
  - "Test case" = automated story test, or manual WCAG review checklist item.
  - "Configuration" = browser × theme × viewport × culture (ADO configurations / TestRail plan configurations).
  - "Run" = a local test run or committed manual review JSON.
  - "Milestone" = release tag.
- **Traceability matrix**: rows = components or stories, columns = WCAG criteria (or test kinds: unit, a11y-auto, a11y-manual, visual). Each cell gets an Xray-style computed status: OK / NOK / NOTRUN / UNKNOWN / UNCOVERED.
- **Qase's "stale" indicator maps directly onto the existing TIA**: if any file mapped to a story changed after the commit of its last manual WCAG review, mark the review **stale (re-review needed)**. This is a high-value, unique showcase feature.
- **Release readiness page** combining the ADO Progress report questions with TestRail's Milestone summary:
  - % executed, pass rate.
  - Open defects (linked GitHub issues).
  - Uncovered requirements.
  - Stale manual reviews.
  - Quarantined-test count, since quarantined tests are a readiness risk.
- **Merging manual and automated results**: keep one result schema (test id, kind=manual|automated, outcome, commit SHA, timestamp, author/runner, evidence attachments) so both feed the same matrix. This mirrors ADO "Test Runs hub shows manual and automated runs" and Testmo unified milestones.

### Gaps
- Default TestRail result statuses (commonly Passed/Blocked/Untested/Retest/Failed) and its suite/section/case hierarchy couldn't be verified: the TestRail intro page returned 403.
- Testmo docs (docs.testmo.com) returned 403. How Testmo defines flaky tests and what its combined milestone report shows is unverified.
- Qase core object docs (suites, plans, runs, milestones, defects, shared steps) weren't fetched beyond the traceability matrix. "qasectl" wasn't found.
- The Zephyr Scale JUnit import mechanics (official) weren't found in this pass.
- The ADO requirements-traceability page (`/pipelines/test/requirements-traceability`) returned 404 at the guessed URL. Widget details come from the overview page.

---

## Q5. Which of these features are realistic for a self-hosted, single-repo platform backed by local test results?

### Takeaway
Most of the *UX and rules* are realistic, because they need only per-test outcome history keyed by commit SHA, durations, and file-to-story mappings. BlazorTestPlatform already has all three. The parts that aren't realistic are **ML/GenAI selection models**, **per-test coverage-based TIA at scale**, **remote executor fleets**, and **third-party ticketing integrations**. Each has a lightweight local analogue.

### Cited Findings
- ML selection needs substantial history: Develocity PTS requires ≥14 days of change history and ≥50 executions over 14 days, and otherwise falls back to running nearly everything — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/)
- Coverage-based TIA has operational limits even at Datadog scale: Bloom-filter false positives (~0.04%), a 5,000-changed-file cap, never skipping tests covering more than 16,000 files, and coverage collected only when cost-justified — [Datadog TIA docs](https://docs.datadoghq.com/tests/test_impact_analysis/)
- Deterministic graph/input-based selection with `--explain` (Nx) needs only git plus declared inputs — [Nx docs: affected](https://nx.dev/docs/features/ci-features/affected)
- Quarantine can be enforced client-side by rewriting the exit code when every failure is quarantined (Trunk), or by a runner that excludes skipped tests and ignores muted failures (Buildkite bktec) — [Trunk docs: Quarantining](https://docs.trunk.io/flaky-tests/quarantining); [Buildkite: Test state & quarantine](https://buildkite.com/docs/test-engine/test-suites/test-state-and-quarantine)
- Slack's results-suppression approach masked new breakages. The code-level disable PR approach (with ticket and owner) worked: 19.82% → 96% main stability — [Slack Engineering](https://slack.engineering/handling-flaky-tests-at-scale-auto-detection-suppression)
- Uber's pluggable-analyzer FSM (new/stable/unstable/disabled/deleted, with enter/exit actions and an audit trail of transitions) is described as a standalone service, but the logic is simple sliding-window and consecutive-pass rules — [Uber Engineering, Flaky Tests Overhaul (Jun 4, 2024)](https://www.uber.com/en-US/blog/flaky-tests-overhaul/)
- Simulator-style evaluation (predicted failures vs savings) is computed from historical runs — [Develocity PTS docs](https://docs.develocity.ai/predictive-test-selection/); [CloudBees confidence curve](https://docs.cloudbees.com/docs/cloudbees-smart-tests/latest/send-data-to-smart-tests/subset/choose-a-subset-optimization-target)
- History-based shard balancing (longest-first plus a shared queue) needs only past per-spec durations — [Cypress docs: Load balancing](https://docs.cypress.io/cloud/features/smart-orchestration/load-balancing); [Buildkite: Test splitting](https://buildkite.com/docs/test-engine/test-splitting)

### Inferences
**Realistic now, mostly reusing existing data**
1. **Flaky monitors**:
   - same-SHA pass+fail;
   - pass-on-retry;
   - windowed failure rate (per branch);
   - failure count on main;
   - optional timeout-inflation and slow-test labels;
   - Healthy/Flaky/Broken precedence, with Broken = 100% failing in the window.
2. **Flake metrics**:
   - flake rate;
   - Cypress severity bands;
   - CI minutes wasted (failed-attempt plus retry durations);
   - runs affected;
   - first and last flaked SHA;
   - trend sparkline;
   - optional Beta-Binomial 0–1 score with a confidence interval.
3. **Quarantine as code**:
   - a committed `quarantine.json` with owner, reason, issue URL, expiry, and mode mute|skip;
   - the runner still executes muted tests and gates only on non-quarantined failures;
   - auto-exit to "Fixed" after N clean runs or days;
   - an "attempt to fix" command that reruns a test 20×;
   - an Events/audit log derived from git history of the JSON file.
4. **Selection simulator and confidence curve** from local history:
   - % real failures caught vs % time saved for the story-level TIA;
   - Conservative/Standard/Fast deterministic profiles;
   - Develocity-style summary line;
   - per-test `--explain` reasons;
   - must-run attribute, tracked-files force-all list, and commit token.
5. **Shard planner**:
   - LPT on historical durations, with a fallback median for new tests;
   - estimated vs actual Gantt per shard;
   - imbalance %;
   - "recommended shard count" curve;
   - failed-first ordering and `--bail N` auto-cancel.
6. **Test management layer**:
   - unified result schema for manual WCAG reviews and automated runs;
   - requirement (story / WCAG criterion) × test matrix with OK/NOK/NOTRUN/UNKNOWN/UNCOVERED;
   - **stale-review detection via TIA**;
   - milestone/release readiness page answering the ADO Progress-report questions;
   - defect links as plain GitHub issue URLs.

**Realistic only in reduced form**
- Ownership: parse `CODEOWNERS` to attribute flaky tests and stale reviews to owners. There is no notification routing; at most a generated markdown digest.
- Ticketing: "Create issue" can prefill a GitHub new-issue URL with flake stats instead of a Jira/Linear integration.
- Root-cause categories: regex heuristics on error messages and stack traces instead of AI.

**Not realistic, or low value, for a single-repo self-hosted demo**
- Trained ML or GenAI predictive selection (data thresholds, model hosting).
- A remote executor fleet / agent pools (Develocity Test Distribution remote executors).
- Cross-org dashboards at Atlassian, Uber, or Datadog scale.
- Per-test code-coverage TIA across the whole suite. Possible with coverlet per test, but costly; story-level mapping is the pragmatic equivalent.

### Gaps
- No source directly evaluates self-hosted or OSS alternatives that implement these features end-to-end (e.g. open-source flaky dashboards for .NET/TRX). This wasn't researched in this pass.
- Data-volume thresholds at which windowed flake rates or confidence curves become statistically meaningful for a small repo aren't given by any vendor doc. Only Develocity's 14-day / 50-execution minimum was found.
