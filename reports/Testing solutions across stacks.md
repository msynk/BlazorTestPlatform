# Testing solutions across stacks: what BlazorTestPlatform still lacked

November 2026. This survey looked past the reporting and analytics platforms of the earlier report
([Modern test platform capabilities](Modern%20test%20platform%20capabilities.md)) at the test **APIs and runtimes**
developers use every day in other stacks, to find what a Blazor component platform should offer and BlazorTestPlatform
(BTP) did not. Three surveys ran in parallel:

* **JavaScript and TypeScript**: Storybook 9/10 (test addon, interactions, a11y addon, autodocs, loaders, globals,
  portable stories), Vitest (browser mode, snapshots, fixtures, `expect.soft`, `expect.poll`), Playwright Test (aria
  snapshots, clock, HAR, `toPass`, sharding, `--repeat-each`, `--only-changed`), Cypress, Testing Library and jest-dom,
  MSW, Chromatic, Percy, Argos, Lost Pixel, Angular CDK component harnesses, Vue Test Utils, fast-check, Stryker.
* **Other stacks**: JUnit 5/6, Kotest, Jetpack Compose testing, Paparazzi and Roborazzi, Espresso, Allure, Pact,
  swift-snapshot-testing and Swift Testing, Flutter widget and golden tests, pytest and Hypothesis, Go (`testing/synctest`,
  fuzzing, golden files), Rust (cargo-nextest, insta, proptest, cargo-mutants), Phoenix LiveView testing (the closest model
  to Blazor Server) and PhoenixTest.
* **.NET and Blazor**: Microsoft.Testing.Platform 2.x and its reports, xUnit v3, NUnit 4, MSTest 4, TUnit, bUnit 2.x,
  Verify, Playwright .NET, FsCheck and CsCheck, Stryker.NET, Aspire testing, the .NET 9/10/11 Blazor runtime features
  that change testing, Microsoft's preview cross-render-mode E2E library for .NET 11, and BlazingStory.

Every candidate was checked against BTP's code before it was listed. Several web pages could not be fetched; API
details from search results were verified against the NuGet packages' XML documentation before anything was built on
them (bUnit `SetRendererInfo`, `InputFileExtensions.UploadFiles`, `BunitNavigationManager.History`; xUnit
`TestContext.AddAttachment`; NUnit `AddTestAttachment`; MSTest `AddResultFile` and the experimental `TestContext.Current`;
TUnit `Output.AttachArtifact`; Playwright `ForcedColors`, `Contrast`, `SetInputFilesAsync`).

## What the surveys agree on

1. **Assertions that keep going and assertions that wait for anything.** `expect.soft` (Playwright, Vitest), AssertJ
   `SoftAssertions`, Kotest `assertSoftly` and Swift's `#expect` all report every failure of a test, not the first;
   `toPass`, `expect.poll` and Kotest `eventually`/`continually` retry arbitrary code, not only DOM matchers.
2. **Locators filter by what elements contain and by state.** Playwright's `filter({ has })`, `getByRole` options
   (`checked`, `pressed`, `expanded`, `selected`, `level`) and regex text everywhere; jest-dom's matchers for
   descriptions, error messages, `required`, `invalid` and `mixed` states.
3. **The JavaScript side and the platform side of components are scriptable.** LiveView's `render_hook` and
   `render_upload`, Playwright's `setInputFiles`, `assert_patch`/`assert_redirect` for navigation.
4. **Accessibility trees are asserted partially.** Playwright's `toMatchAriaSnapshot` matches contained trees with
   regex names and more states (`pressed`, `selected`, `mixed`).
5. **Flakiness is caught before merge, not only measured after.** `--repeat-each`, `@RepeatedTest`, `go test -count`,
   burn-in of new or changed tests (Uber, Playwright `--only-changed`).
6. **Results speak the formats other tools read.** CTRF keeps retries and flakiness that JUnit loses; test frameworks
   carry attachments into IDEs, TRX and CI test views.
7. **Configuration axes beyond viewport and theme.** Locale, direction (RTL), forced colors and contrast (Chromatic
   modes, Compose `DeviceConfigurationOverride`, Playwright `forcedColors`/`contrast`).
8. **Snapshots stay tidy.** Jest/Vitest report obsolete snapshots; insta lists unreferenced ones.
9. **The renderer's own state is part of the contract.** bUnit's `SetRendererInfo` (components that branch on
   `RendererInfo` throw without it), `[PersistentState]` and prerender handoff, circuit pause/resume.

## Gap list and what was done

Status: **Done** (implemented, with tests that fail without it), **Deferred** (with the reason).

| # | Gap | Seen in | Status |
|---|---|---|---|
| 1 | Vulnerable `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.13 pin (GHSA-g27p-qf6j-c96c) | `dotnet build` NU1903 | **Done**: 10.0.15 |
| 2 | bUnit did not set `RendererInfo`, so any component reading it threw `MissingRendererInfoException` | bUnit 2 docs | **Done**: `BunitStoryOptions.RendererInfo` (default interactive `Server`; `Static` simulates prerendering) |
| 3 | Soft assertions | Playwright, Vitest, AssertJ, Kotest, Swift Testing | **Done**: `ctx.ExpectSoft(...)`, `ctx.SoftAsync(...)`, failures listed when play ends |
| 4 | Retrying blocks, polling non-DOM state, conditions that must stay true | Playwright `toPass`/`expect.poll`, Kotest `continually` | **Done**: `ExpectEventuallyAsync`, `ExpectPollAsync`, `ExpectToRemainAsync` (fake-time aware) |
| 5 | Accessibility-state and structural matchers | jest-dom, Playwright | **Done**: accessible description, error message, required, invalid, partially checked, empty, id, classes, list texts, regex overloads; both engines compute descriptions and error messages |
| 6 | Locator filters by descendant, role state options, regex text | Playwright, Testing Library | **Done**: `Filter(locator)`/`FilterNot(locator)`, `isChecked`/`pressed`/`expanded`/`selected`/`disabled`/`level`/`includeHidden`/`namePattern`, `GetByText(Regex)`..., in both engines with browser parity |
| 7 | Partial accessibility-tree snapshots, regex names, `pressed`/`selected`/`mixed` states | Playwright `toMatchAriaSnapshot` | **Done**: `AriaSnapshotMatch.Contains`, `/children: equal`, `AriaSnapshotTemplate`; snapshot parity checked per fixture in browsers |
| 8 | File uploads (`InputFile`) | Playwright, LiveView `render_upload` | **Done**: `SetInputFilesAsync(StoryFile...)` in bUnit, Playwright and in-app |
| 9 | Navigation assertions; components navigating the story frame away | LiveView `assert_patch`/`assert_redirect` | **Done**: `ExpectNavigation(...)`, `GetNavigationsAsync()`; the workbench records navigations and stops those leaving the frame |
| 10 | JavaScript calling .NET (`[JSInvokable]`) | LiveView `render_hook`, MSW-style mocks | **Done**: `ctx.InvokeDotNetAsync(identifier, method, args)` in every driver |
| 11 | Burn-in (repeat each story) | Playwright `--repeat-each`, JUnit `@RepeatedTest`, `go test -count` | **Done**: `BTP_REPEAT_EACH` / `repeatEach` |
| 12 | Evidence in the test framework's result | xUnit v3, NUnit, MSTest, TUnit, MTP | **Done**: `StoryAttachments`, attached by `Passed(...)` in all four packages |
| 13 | CTRF output | CTRF, MTP 2.x | **Done**: `btp report --ctrf`, `CtrfReportWriter` |
| 14 | Orphaned visual baselines | Jest/Vitest obsolete snapshots, insta | **Done**: `SnapshotStore.FindOrphanedBaselines`/`PruneOrphanedBaselines`, demo gate |
| 15 | Report-only accessibility per story | Storybook a11y `todo` | **Done**: `Accessibility(a => a.ReportOnly = true)` |
| 16 | Culture per story | Storybook globals, Chromatic modes, Compose locales | **Done**: `Culture("de-DE")` (bUnit current culture; browser locale) |
| 17 | Right-to-left, forced colors and contrast | Compose overrides, Playwright `forcedColors`/`contrast` | **Done**: `ThemeDefinition.RightToLeft`, `HighContrast`, theme `Direction`/`ForcedColors`/`MoreContrast`, bUnit media queries |
| 18 | `aria-errormessage` announcement check missing from the in-memory rules (found by axe in a new demo story) | axe-core | **Done**: part of `aria-valid-attr-value` |
| 19 | Named screenshots inside play (`toHaveScreenshot('open')`) | Playwright, Vitest 4 | Deferred: needs per-step baseline naming, approval and hub review; the post-play and `initial` snapshots cover the common case |
| 20 | Markup/DOM snapshots with scrubbers (Verify, Jest) | Verify.Bunit, Jest | Deferred: markup differs between bUnit and browsers, so a cross-driver contract needs a normalizer; the accessibility snapshot is the cross-driver structural snapshot today |
| 21 | Inline snapshot rewriting (insta, inline-snapshot, Playwright `updateSourceMethod`) | insta, Python, Playwright | Deferred: needs a Roslyn rewriter and a review flow |
| 22 | Stateful property-based testing with shrinking (random action sequences checked against invariants) | Hypothesis, fast-check, CsCheck | Deferred: a design of its own (generators for args and actions, shrinking, replayable seeds); pairwise variants and seeded data cover combinations today |
| 23 | Persistent component state and prerender-to-interactive handoff checks; circuit pause/resume and reconnect UI | .NET 10, bUnit `AddBunitPersistentComponentState`, LiveView | Deferred: needs host support for observing the handoff in browsers; `RendererInfo` now lets bUnit stories render the prerendered branch |
| 24 | Pseudo-state forcing (`:hover`, `:focus-visible` without interaction) | storybook-addon-pseudo-states | Deferred: needs CSS rewriting in browsers and in bUnit's cascade |
| 25 | Per-step accessibility checks (after every action) | Compose `enableAccessibilityChecks` | Deferred: cost per action in browsers; explicit `ctx.ExpectAriaSnapshotAsync` and the end-of-story scan cover it partly |
| 26 | Autodocs pages (props table, live Razor snippet), shippable component harnesses, MCP server for agents | Storybook, Angular CDK, Storybook MCP | Deferred: product features beyond the testing core |
| 27 | Matrix axis for culture and time zone (not only per story) | Chromatic modes, xUnit v3 culture | Deferred: the matrix key format (`story|viewport|theme|mode|browser`) is part of result files and baselines; per-story `Culture` and themes cover it without a format change |
| 28 | HAR record/replay, WebSocket/SignalR mocking | Playwright, MSW | Deferred: `MockHttp` covers HTTP inside the app; SignalR needs a fake `HubConnection` design |
| 29 | Code coverage merged with story coverage, Stryker on MTP | coverlet.MTP, MTP code coverage, Stryker 4.13 | Deferred: documented tools exist; merging into the hub is a reporting project |
| 30 | `net11.0` target, Microsoft's cross-render-mode E2E library (.NET 11 preview) | .NET 11 (released 2026-11-10) | Deferred: the installed SDK is 10.0.403; revisit with the .NET 11 SDK |

## Where BTP was already ahead

Story state coverage (no surveyed tool reports parameter values no story renders), one play script in three drivers
with parity tests between the C# and JavaScript engines, cross-render-mode visual baselines, accessibility baselines and
justified expiring suppressions, manual review fingerprints, impact analysis with reasons, and a self-hosted hub that
joins them. The work above closes the gaps in the everyday test API rather than adding another dashboard.
