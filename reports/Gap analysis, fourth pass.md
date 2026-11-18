# Gap analysis, fourth pass: .NET 11 and the run-control layer

November 2026. Two surveys ran in parallel, after .NET 11 shipped on 2026-11-10:

* **.NET 11 and the .NET test stack.** The survey covered ASP.NET Core 11 changes that affect component testing, the
  package versions (verified on nuget.org), bUnit 2.15, Playwright .NET 1.64, Microsoft.Testing.Platform 2.5, xUnit v3
  4.0, NUnit 5, MSTest 4.5, TUnit 1.86, axe-core 4.14 and Roslyn 5.12. It also looked at Microsoft's cross-render-mode
  E2E library (`Microsoft.AspNetCore.Components.Testing`).
* **Test tooling across stacks, beyond the previous survey.** This one covered Playwright Test 1.50 to 1.65, Vitest 4.1,
  Storybook 10, Chromatic, Cypress, Testing Library, MSW 2, Swift Testing, pytest 9, cargo-nextest, insta, Flutter,
  Compose, Roborazzi and Phoenix LiveView.

The [previous survey](Testing%20solutions%20across%20stacks.md) already covered soft and retrying assertions, locator
filters, partial ARIA snapshots, uploads, navigation, burn-in, CTRF, cultures and themes. This pass starts from the 12
items it deferred and from what the new surveys found. Before a gap was listed, it was checked against the code.

## Findings that changed the plan

1. **.NET 11 can now be targeted.** The previous pass deferred `net11.0` because only the .NET 10 SDK was installed.
   * SDK 11.0.100 is now available. All ASP.NET Core 11 packages are 11.0.0 and ship `net11.0` only, so each target
     framework needs its own package versions.
   * bUnit 2.15.2 supports `net11.0`.
   * Analyzers built against Roslyn 4.14 still load in the .NET 11 SDK. Moving them to 5.x would break consumers on the
     .NET 10 SDK, so they stay on 4.14.
2. **.NET 11 adds analyzer BL0016** ("JS interop call is not guarded with a try/catch"). It flagged one real weakness:
   * The workbench's story host imported its bridge module in `OnAfterRenderAsync` without handling a circuit that had
     already gone, so the exception reached the renderer.
   * Every other hit is a JS runtime or a driver, where a failure must propagate.
3. **Every list in an older result file read as `null` on .NET 10.** This was a latent bug, found while testing the new
   `Screenshots` list:
   * The source-generated JSON reader sets every init property, so an omitted list overwrote the `= []` default with
     `null`. .NET 11's reader does not do this.
   * `btp approve` crashed on a results directory holding files from an earlier run.
4. **Microsoft's E2E library is still preview.** `Microsoft.AspNetCore.Components.Testing` is at
   `11.0.0-preview.7.26529.118`. It is MSTest/MTP and Playwright based, with YARP, and has no documentation. It is
   complementary (whole apps, not components), so it is watched, not adopted.
5. **The everyday test API was ahead; the run-control layer was behind.**
   * Playwright, Vitest, pytest and nextest all have boolean tag filters, grep, `--last-failed`, `--max-failures`,
     `--fail-on-flaky-tests`, expected failures (`test.fail`, strict `xfail`, `withKnownIssue`), `fixme` and `slow`.
     BTP had none of them.
   * Surefire's `flakyFailure` elements show flakes natively in CI systems.

## Gap list and status

Status: **Done** (implemented and covered by tests that fail without the change; browser features were also run in
Chromium, Firefox, WebKit and the in-app driver), **Deferred** (with the reason).

| # | Gap | Seen in | Status |
|---|---|---|---|
| 1 | No `net11.0` target | .NET 11 GA | **Done**: every library and test project targets `net10.0;net11.0` with per-framework ASP.NET Core packages (10.0.15 / 11.0.0); bUnit 2.15.2; global.json pins SDK 11.0.100; CI and the container scripts install the .NET 11 SDK and the .NET 10 runtime; unit suites run on both |
| 2 | Story host interop not guarded against a closed circuit (BL0016) | .NET 11 analyzers | **Done**; the other BL0016 hits are suppressed with the reason |
| 3 | `btp init` pipelines hard-coded .NET 10 | — | **Done**: channels come from the project's target frameworks, `Directory.Build.props` and global.json |
| 4 | `dotnet run --project src/BlazorTestPlatform.Cli` became ambiguous once multi-targeted | found while testing | **Done**: the tool targets one framework and rolls forward |
| 5 | Lists of older result files read as null on .NET 10 (approve crashed) | found while testing | **Done**: every serialized list is null-safe |
| 6 | Expected failures, conditional by driver or render mode | Playwright `test.fail`, pytest strict `xfail`, Swift `withKnownIssue` | **Done**: `ExpectFailure(reason, when)`; failures pass, a clean pass fails, intermittent failures count; bUnit, browser and in-app runners |
| 7 | `fixme` and `slow` annotations | Playwright | **Done**: `Fixme(reason)` (skip plus `fixme` tag), `Slow()` (triple timeout) |
| 8 | Boolean tag filters | Vitest 4.1 `--tags-filter`, pytest `-m`, nextest filtersets | **Done**: `TagExpression`, `BTP_TAGS`, `catalog.WithTags(...)` |
| 9 | Grep and inverted grep | Playwright `--grep`, `--grep-invert` | **Done**: `BTP_GREP`, `BTP_GREP_INVERT` |
| 10 | Rerun the last failures | Playwright `--last-failed`, pytest `--lf` | **Done**: `BTP_LAST_FAILED` |
| 11 | Fail on flaky runs | Playwright `--fail-on-flaky-tests`, nextest `flaky-result` | **Done**: `BTP_FAIL_ON_FLAKY` |
| 12 | Stop after N failures | Playwright `--max-failures`, MTP `--maximum-failed-tests` | **Done**: `BTP_MAX_FAILURES` |
| 13 | Flaky attempts invisible to CI test views | nextest and Surefire JUnit | **Done**: `flakyFailure` / `rerunFailure` elements; expected failures in JUnit, CTRF (`rawStatus`) and HTML |
| 14 | Named screenshots inside play (deferred #19) | Playwright `toHaveScreenshot('name')`, Vitest `toMatchScreenshot` | **Done**: `ctx.ExpectScreenshotAsync(name, target, fullPage)` with baselines, tolerances, masks, revert on failure, `btp approve`, hub review, HTML report, SARIF and framework attachments |
| 15 | Accessibility checks in the middle of play (deferred #25) | Compose `enableAccessibilityChecks` | **Done** as an explicit check: `ctx.ExpectAccessibleAsync(label)` (markup rules in bUnit, axe-core in browsers). A check after every action stays deferred: it costs an axe run per action in browsers |
| 16 | Computed-style and viewport assertions | Playwright `toHaveCSS` (with `pseudo`), `toBeInViewport({ ratio })` | **Done**: `ToHaveCssAsync`, `ToBeInViewportAsync` and `IStoryDriver.MeasureAsync`; bUnit records them as skipped |
| 17 | Attachments from play | Playwright `testInfo.attach` | **Done**: `ctx.Attach(name, text or bytes)`, attached to framework results whatever the outcome |
| 18 | Step timeouts | Playwright `test.step` `timeout` | **Done**: `ctx.StepAsync(name, body, timeout)` |
| 19 | Ignoring regions in markup | Chromatic `data-chromatic="ignore"` | **Done**: `data-btp-ignore` is always masked |
| 20 | Container script cannot render one story's baselines | — | **Done**: `scripts/visual-tests.*` pass `BTP_GREP`, `BTP_GREP_INVERT`, `BTP_TAGS` and `BTP_SHARD` through |
| 21 | Markup snapshots with scrubbers (deferred #20) | Verify, Jest, syrupy | Deferred: browsers and bUnit serialize markup differently; the accessibility tree is the cross-driver structural snapshot |
| 22 | Inline snapshot rewriting (deferred #21) | insta, Playwright `updateSourceMethod` | **Done** for accessibility snapshots: call sites recorded by `[CallerFilePath]`/`[CallerLineNumber]`, `btp snapshots update-source` rewrites the raw literal when every run agrees and nothing (patterns, variables) would be lost |
| 23 | Stateful property-based exploration (deferred #22) | Hypothesis, fast-check, CsCheck | **Done**: `ctx.ExploreAsync` (actions with preconditions, invariants, seeded steps, replay by seed, regression script), shrunk by the bUnit runner with delta debugging over fresh renders; browser runs report unshrunk sequences |
| 24 | Persistent component state, prerender handoff, circuit pause and resume (deferred #23) | .NET 10/11, bUnit `AddBunitPersistentComponentState` | **Prerender handoff done**: `CheckPrerender()` compares the prerendered state with every state after Blazor starts. Circuit pause/resume stays deferred |
| 25 | Pseudo-state forcing (deferred #24) | storybook-addon-pseudo-states | **Done** in browsers: `ForceStateAsync(PseudoState...)` rewrites stylesheet rules in Chromium, Firefox, WebKit and the in-app driver; bUnit's cascade does not model pointer state, so it skips such stories |
| 26 | Autodocs, component harnesses, MCP server (deferred #26) | Storybook 10, Angular CDK | Deferred: product features beyond the testing core |
| 27 | Culture and time-zone matrix axis, named modes (deferred #27) | Chromatic modes | **Done**: per-story `TimeZone(...)`, and modes as derived themes (`WithCulture`, `WithTimeZone`) whose names carry them through the existing key format |
| 28 | HAR record and replay, WebSocket and SignalR mocking (deferred #28) | Playwright `routeWebSocket`, HAR | Deferred: `MockHttp` covers HTTP inside the app; SignalR needs a fake `HubConnection` design |
| 29 | Code coverage merged with story coverage (deferred #29) | coverlet, MTP | Deferred: a reporting project |
| 30 | Playwright 1.64 | Playwright | Deferred: an upgrade re-renders every baseline; use the "Regenerate visual baselines" workflow, as `docs/ci.md` describes |
| 31 | Test locks, parallel groups, shuffle | Playwright `lock`, TUnit `[NotInParallel]`, `--shuffle` | Deferred: these belong to the test framework, and xUnit, NUnit, MSTest and TUnit each have their own |
| 32 | Retry strategies (isolated retries at the end, backoff) | Playwright `retryStrategy`, nextest | Deferred: retries run inside one test-framework case; moving them to the end of the run would need framework hooks |
| 33 | Leak detection (undisposed components, `DotNetObjectReference`) | Flutter `LeakTesting`, Vitest `--detect-async-leaks` | Deferred: Blazor does not expose whether a reference was disposed; needs a tracking JS runtime |
| 34 | Tags that carry options (timeouts, retries per tag) | Vitest 4.1 | Deferred: `Slow()` and `Timeout(...)` cover the common case |
| 35 | Clock `pauseAt`, `fastForward`, `setSystemTime` | Playwright clock | Deferred: `FakeTimeProvider` fires due timers whenever time moves, so "move without firing" cannot be honored; `AdvanceTimeAsync` covers `runFor` |
| 36 | Microsoft.AspNetCore.Components.Testing | .NET 11 | Watched: still preview, app-level E2E (not components) |
| 37 | A story repeating a state marked a manual review stale | found while adding demo stories | **Done**: set-based review fingerprints, earlier fingerprints still accepted |
| 38 | Test framework updates | nuget.org | **Done**: patch updates; the NUnit package verified on NUnit 5 |

## Rules that came out of the work

* **Observations skip, actions skip the story.** A driver that cannot observe something (screenshots, computed styles,
  layout, an axe scan) records the check as skipped and the play script goes on, so one script keeps working in bUnit.
  A driver that cannot perform an action the rest of the script depends on (going offline, geolocation) skips the
  whole story, as before.
* **Leave stories out by reporting them, not by dropping them.** Run filters make the test framework packages report the
  stories they leave out as skipped, with the reason. A filter that selects nothing still runs, and it shows what it
  skipped.
* **Every result rule lives in one place.** Expected failures, the flaky policy, quarantine and the failure budget are
  applied by `RunPolicy.Apply` before a result is written. They behave the same in bUnit and in browsers, and custom
  runners can reuse them.
