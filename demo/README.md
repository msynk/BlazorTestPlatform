# BlazorTestPlatform demo

A small design system, its stories, a host with the workbench and the test hub, and a test project: the complete, working version of the setup in [getting started](../docs/getting-started.md). Every feature of BlazorTestPlatform is used here at least once, by a story you can open, a hub page that shows its results, and a test that runs in CI.

The idea in one sentence: **a story is one state of a component, written once in C#, and it runs as a bUnit test, a real-browser test under every render mode, a visual snapshot, an accessibility audit and a performance check; the hub then turns those results into history, triage and decisions.**

The site has three parts:

| URL | What it is |
|---|---|
| `/` | A guided tour of the platform in five stages (author, run, review, analyze, ship), linking to the page or story that shows each feature |
| `/_btp` | The test hub: dashboard and trends, runs and comparisons, flaky tests and quarantine, failures grouped by cause, visual and accessibility review, performance, coverage and WCAG traceability, impact analysis and sharding, live runs, release readiness and integrations ([guide](../docs/hub.md)) |
| `/_workbench` | The component workbench: every story across themes, viewports and render modes, with live controls, in-frame play, an inspector and a recorder |

## Run it

From the repository root:

```bash
# The tour (http://localhost:5288), the test hub (/_btp) and the workbench (/_workbench)
dotnet run --project demo/BlazorTestPlatform.Demo.Workbench

# Every story in memory with bUnit: seconds, no browser needed
dotnet test --project demo/BlazorTestPlatform.Demo.UI.Tests -- --filter-class '*ComponentStoryTests'

# Everything: cross-hosting matrix, snapshots, axe-core, Firefox and WebKit, in-app play, gates
dotnet build demo/BlazorTestPlatform.Demo.UI.Tests
pwsh demo/BlazorTestPlatform.Demo.UI.Tests/bin/Debug/net10.0/playwright.ps1 install
dotnet test --project demo/BlazorTestPlatform.Demo.UI.Tests

# Add the run to the hub's history, and write the HTML report
dotnet run --project src/BlazorTestPlatform.Cli -- report -p demo/BlazorTestPlatform.Demo.UI.Tests --archive demo/BlazorTestPlatform.Demo.UI.Tests/.btp/runs
```

On first start (`dotnet run` uses the `http` launch profile, which sets `BTP_DEMO_SEED=1`) the host records ten real in-memory runs of every story into `BlazorTestPlatform.Demo.UI.Tests/.btp/runs`, a day apart, so the hub has history to show. They include the **Demo/Chaos** stories in [`BlazorTestPlatform.Demo.Workbench/Chaos`](BlazorTestPlatform.Demo.Workbench/Chaos), which fail on purpose in some runs: an intermittent toast (flaky, and quarantined in [`quarantine.json`](BlazorTestPlatform.Demo.UI.Tests/quarantine.json)), a regression that was fixed, a renamed button that broke a story, a hanging bell (a flaky timeout), an accessibility fix and a new accessibility failure, and a feed that gets slower. They only exist in the host: the workbench and the test suite never see them. Delete `.btp/runs` to reseed; run the test suite and `btp report --archive` to add the browser results to the history.

## What's here

| Project | Role |
|---|---|
| [`BlazorTestPlatform.Demo.UI`](BlazorTestPlatform.Demo.UI) | The design system under test: 14 components, from a button to an HTTP-backed sign-up form, plus `demo.css` with light and dark themes. Nothing in it knows about testing. |
| [`BlazorTestPlatform.Demo.UI.Stories`](BlazorTestPlatform.Demo.UI.Stories) | One collection of stories per component (`*Stories.cs`), Razor fragments the stories use (`Fragments.razor`), and two test fixtures for driver parity and browser emulation. Shared by the workbench and the tests. |
| [`BlazorTestPlatform.Demo.Workbench`](BlazorTestPlatform.Demo.Workbench) + [`.Client`](BlazorTestPlatform.Demo.Workbench.Client) | A Blazor Web App with Server and WebAssembly interactivity that hosts the guided tour (`/`), the test hub (`/_btp`) and the workbench (`/_workbench`), plus the chaos stories and the history seeder. Browser tests run against it. |
| [`BlazorTestPlatform.Demo.UI.Tests`](BlazorTestPlatform.Demo.UI.Tests) | xUnit v3 tests: every story in bUnit and in real browsers, plus the quality gates. Committed baselines and reviews live here too. |

## Feature map

Story ids open in the workbench at `/_workbench?story=<id>`; add `&play=1` to run the play script in the frame.

### Write a story once

| Feature | Story | Code | Test |
|---|---|---|---|
| Stories, args, live controls | `inputs-button--secondary` | [ButtonStories.cs](BlazorTestPlatform.Demo.UI.Stories/ButtonStories.cs) | `ComponentStoryTests`, `ArgControlsTests` |
| Collection defaults, single-parameter variants | `feedback-alert--dismissible` | [AlertStories.cs](BlazorTestPlatform.Demo.UI.Stories/AlertStories.cs) | `ComponentStoryTests` |
| Variant matrix (`AddVariants`) | `inputs-button--variant-primary-size-small` | [ButtonStories.cs](BlazorTestPlatform.Demo.UI.Stories/ButtonStories.cs) | `VisualTests` |
| Pairwise variants with constraints (`Pairwise()`, `Where`) | `display-badge--default` and its variants | [BadgeStories.cs](BlazorTestPlatform.Demo.UI.Stories/BadgeStories.cs) | `CombinatoricsTests` |
| Two-way binding (`Bind`), container decorator | `inputs-text-field--typing-updates-the-bound-value` | [TextFieldStories.cs](BlazorTestPlatform.Demo.UI.Stories/TextFieldStories.cs) | `ComponentStoryTests` |
| Computed args from seeded `FakeData` | `data-person-list--seeded-people` | [PersonListStories.cs](BlazorTestPlatform.Demo.UI.Stories/PersonListStories.cs) | `ComponentStoryTests` |
| `RenderFragment<T>` templates and `RenderFragment` args written in Razor | `data-person-list--custom-item-template`, `data-person-list--empty-with-custom-content` | [Fragments.razor](BlazorTestPlatform.Demo.UI.Stories/Fragments.razor) | `ComponentStoryTests` |
| Cascading-value decorator | `data-person-list--compact-density` | [PersonListStories.cs](BlazorTestPlatform.Demo.UI.Stories/PersonListStories.cs) | `ComponentStoryTests` |
| Unmatched attributes (`Attribute`) | `display-badge--custom-brand-color` | [BadgeStories.cs](BlazorTestPlatform.Demo.UI.Stories/BadgeStories.cs) | `ComponentStoryTests` |
| Composition stories (`Stories<Composition>`) | `overview-kitchen-sink--design-system` | [KitchenSinkStories.cs](BlazorTestPlatform.Demo.UI.Stories/KitchenSinkStories.cs) | `VisualTests` |
| Assembly-wide story defaults (`IStoryConfiguration`) | every story gets the `demo` tag | [WorkbenchSetup.cs](BlazorTestPlatform.Demo.UI.Stories/WorkbenchSetup.cs) | — |

### Interact like a person

| Feature | Story | Code | Test |
|---|---|---|---|
| Clicks and auto-logged actions | `inputs-button--click-raises-onclick` | [ButtonStories.cs](BlazorTestPlatform.Demo.UI.Stories/ButtonStories.cs) | every driver |
| Keyboard: Tab order, Enter and Space, steps on the timeline | `inputs-button--keyboard-activation` | [ButtonStories.cs](BlazorTestPlatform.Demo.UI.Stories/ButtonStories.cs) | every driver |
| WAI-ARIA widget: roving tabindex, arrows, Home and End | `navigation-tabs--keyboard-navigation` | [TabsStories.cs](BlazorTestPlatform.Demo.UI.Stories/TabsStories.cs) | every driver |
| Focus moved by the component, Escape, `ExpectTabOrderAsync` | `overlays-dialog--keyboard`, `overlays-dialog--confirm` | [DialogStories.cs](BlazorTestPlatform.Demo.UI.Stories/DialogStories.cs) | every driver |
| Accessibility tree snapshots | `overlays-dialog--destructive`, `testing-driver-parity--accessibility-tree` | [DialogStories.cs](BlazorTestPlatform.Demo.UI.Stories/DialogStories.cs) | every driver |
| Forms: fill, implicit submission with Enter, validation | `patterns-sign-up-form--validation-errors`, `data-todo-list--add-a-task` | [SignUpFormStories.cs](BlazorTestPlatform.Demo.UI.Stories/SignUpFormStories.cs) | every driver, `CombinatoricsTests` |
| One script, three drivers held to the same results | `testing-driver-parity--*` | [DriverParityStories.cs](BlazorTestPlatform.Demo.UI.Stories/DriverParityStories.cs) | `DriverParityTests`, `InAppPlayTests` |
| Driver capabilities: skipped with a reason, never faked | `testing-browser-emulation--offline` | [BrowserEmulationStories.cs](BlazorTestPlatform.Demo.UI.Stories/BrowserEmulationStories.cs) | `ComponentStoryTests` (skipped), `CrossHostingTests` |
| Optional checks per hosting model (`ctx.HostingModel`) | `display-runtime-badge--masked-value` | [RuntimeBadgeStories.cs](BlazorTestPlatform.Demo.UI.Stories/RuntimeBadgeStories.cs) | `CrossHostingTests` |
| Hand-written tests on top of a story | — | [CombinatoricsTests.cs](BlazorTestPlatform.Demo.UI.Tests/CombinatoricsTests.cs), [ComponentStoryTests.cs](BlazorTestPlatform.Demo.UI.Tests/ComponentStoryTests.cs) | `CombinatoricsTests`, `ResponsiveInMemoryTests` |

### Control the world around a component

| Feature | Story | Code | Test |
|---|---|---|---|
| Fake time (`UseFakeTime`, `AdvanceTimeAsync`) | `feedback-session-timer--expires` | [AccountStories.cs](BlazorTestPlatform.Demo.UI.Stories/AccountStories.cs) | every driver |
| Fake backend: JSON, status codes, `Times`, `Never` | `data-people-directory--server-error-with-retry`, `data-people-directory--loading` | [PeopleDirectoryStories.cs](BlazorTestPlatform.Demo.UI.Stories/PeopleDirectoryStories.cs) | every driver |
| POST bodies, delayed responses on fake time, `Fail()`, 409 | `patterns-sign-up-form--successful-sign-up`, `--server-unreachable`, `--email-already-registered` | [SignUpFormStories.cs](BlazorTestPlatform.Demo.UI.Stories/SignUpFormStories.cs) | every driver |
| Story fixtures and seeds | `data-todo-list--seeded` | [TodoListStories.cs](BlazorTestPlatform.Demo.UI.Stories/TodoListStories.cs) | every driver |
| Fake users: roles, claims, authenticating | `account-user-badge--administrator`, `account-user-badge--signing-in` | [AccountStories.cs](BlazorTestPlatform.Demo.UI.Stories/AccountStories.cs) | every driver |
| JS-interop mocks, also in browsers | `feedback-cookie-consent--first-visit` | [CookieConsentStories.cs](BlazorTestPlatform.Demo.UI.Stories/CookieConsentStories.cs) | every driver |
| Media queries evaluated in bUnit | `feedback-cookie-consent--first-visit` on a phone | [btp.json](BlazorTestPlatform.Demo.UI.Tests/btp.json) | `ResponsiveInMemoryTests` |
| Browser emulation: geolocation, offline, latency | `testing-browser-emulation--*` | [BrowserEmulationStories.cs](BlazorTestPlatform.Demo.UI.Stories/BrowserEmulationStories.cs) | `CrossHostingTests`, `BrowserMetricsTests` |

### Quality gates

| Feature | Story | Code or data | Test |
|---|---|---|---|
| Visual regression across viewports, themes and browsers | any story without `NoVisual()` | [`__snapshots__/`](BlazorTestPlatform.Demo.UI.Tests/__snapshots__) | `VisualTests`, `CrossBrowserTests` |
| Masks for content that legitimately differs | `display-runtime-badge--masked-value` | [RuntimeBadgeStories.cs](BlazorTestPlatform.Demo.UI.Stories/RuntimeBadgeStories.cs) | `CrossHostingTests` |
| Full-page captures for fixed overlays | `overlays-dialog--open` | [DialogStories.cs](BlazorTestPlatform.Demo.UI.Stories/DialogStories.cs) | `VisualTests` |
| axe-core and markup rules, policy in `btp.json` | every story | [btp.json](BlazorTestPlatform.Demo.UI.Tests/btp.json) | every driver |
| A waived rule with a written justification | `display-badge--custom-brand-color` | [BadgeStories.cs](BlazorTestPlatform.Demo.UI.Stories/BadgeStories.cs) | `VisualTests` |
| Manual WCAG reviews that go stale | Quality gates panel of `inputs-button--primary` | [`a11y-reviews/`](BlazorTestPlatform.Demo.UI.Tests/a11y-reviews) | `AccessibilityReviewTests` |
| Render budgets and baselines | `inputs-button--click-raises-onclick`, `data-person-list--sort-by-name`, `data-person-list--large-list` | [performance.json](BlazorTestPlatform.Demo.UI.Tests/__baselines__/performance.json) | `ComponentStoryTests` |
| Browser runtime budgets (layout shift, DOM size) | `data-person-list--large-list`, `testing-browser-emulation--geolocation` | [PersonListStories.cs](BlazorTestPlatform.Demo.UI.Stories/PersonListStories.cs) | `CrossHostingTests`, `BrowserMetricsTests` |
| Story state coverage | every component | [StoryCoverageTests.cs](BlazorTestPlatform.Demo.UI.Tests/StoryCoverageTests.cs) | `StoryCoverageTests` |

### The test hub

| Feature | Hub page | Code | Test |
|---|---|---|---|
| Dashboard: trends, outcomes per run, matrix, decisions waiting | `/_btp` | [Dashboard.razor](../src/BlazorTestPlatform.Hub/Components/Pages/Dashboard.razor) | `HubTests` |
| Run archive with commit, branch and CI metadata | `/_btp/runs` | [RunArchive.cs](../src/BlazorTestPlatform.Reporting/RunArchive.cs) | `HistoryTests` |
| Run comparison: new failures, still failing, fixed, new flaky, slower | `/_btp/compare` | [RunHistory.cs](../src/BlazorTestPlatform.Reporting/RunHistory.cs) | `HistoryTests` |
| Stability (healthy, failing, flaky, broken, fixed) with a 95% interval and smart tags | `/_btp/flaky` | [RunHistory.cs](../src/BlazorTestPlatform.Reporting/RunHistory.cs) | `HistoryTests` |
| Quarantine in a committed file | `/_btp/flaky#quarantine` | [quarantine.json](BlazorTestPlatform.Demo.UI.Tests/quarantine.json) | `HubTests`, `QuarantineAndShardPlanTests` |
| Unique errors with category, likely cause and next step | `/_btp/failures` | [FailureAnalysis.cs](../src/BlazorTestPlatform.Reporting/FailureAnalysis.cs) | `HistoryTests` |
| A result as a trace: timeline, failure, attempts, artifacts, copy as prompt | `/_btp/result?key=demo-chaos--report-export%7Cbunit` | [ResultDetail.razor](../src/BlazorTestPlatform.Hub/Components/Pages/ResultDetail.razor) | `HubTests` |
| Visual review: five view modes, grouped decisions, keyboard | `/_btp/visual` | [Visual.razor](../src/BlazorTestPlatform.Hub/Components/Pages/Visual.razor) | `HubTests` |
| Accessibility baseline: new, existing, fixed; needs review; waivers | `/_btp/accessibility` | [Accessibility.razor](../src/BlazorTestPlatform.Hub/Components/Pages/Accessibility.razor) | `HistoryTests` |
| Performance budgets, browser metrics, duration trends | `/_btp/performance` | [Performance.razor](../src/BlazorTestPlatform.Hub/Components/Pages/Performance.razor) | `HubTests` |
| Story-state coverage and a WCAG traceability matrix | `/_btp/coverage` | [Coverage.razor](../src/BlazorTestPlatform.Hub/Components/Pages/Coverage.razor) | `HubTests` |
| Impact analysis with reasons, duration-balanced shards | `/_btp/impact` | [ShardPlan.cs](../src/BlazorTestPlatform.Core/Matrix/ShardPlan.cs) | `ImpactAnalysisTests`, `QuarantineAndShardPlanTests` |
| Live in-memory runs from the browser | `/_btp/live` | [HubRunner.cs](../src/BlazorTestPlatform.Hub/HubRunner.cs) | `HubTests` |
| Release readiness verdict | `/_btp/readiness` | [Readiness.razor](../src/BlazorTestPlatform.Hub/Components/Pages/Readiness.razor) | `HubTests` |
| Exports, JSON API, pull-request comment | `/_btp/integrations` | [HubEndpoints.cs](../src/BlazorTestPlatform.Hub/HubEndpoints.cs) | `HubTests` |
| Access control: 404 when disabled, antiforgery on actions, files only from results and snapshots | — | [HubEndpoints.cs](../src/BlazorTestPlatform.Hub/HubEndpoints.cs) | `HubTests` |

### Every render mode, every browser, CI

| Feature | Where | Test |
|---|---|---|
| Static SSR, Server, WebAssembly and Auto from one host | [BrowserMatrixTests.cs](BlazorTestPlatform.Demo.UI.Tests/BrowserMatrixTests.cs) | `CrossHostingTests` |
| Chromium, Firefox and WebKit | [BrowserMatrixTests.cs](BlazorTestPlatform.Demo.UI.Tests/BrowserMatrixTests.cs) | `CrossBrowserTests` |
| Play scripts inside the workbench frame (in-app driver) | [BrowserMatrixTests.cs](BlazorTestPlatform.Demo.UI.Tests/BrowserMatrixTests.cs) | `InAppPlayTests` |
| Workbench controls, recorder and inspector | [ArgControlsTests.cs](BlazorTestPlatform.Demo.UI.Tests/ArgControlsTests.cs), [WorkbenchInspectorTests.cs](BlazorTestPlatform.Demo.UI.Tests/WorkbenchInspectorTests.cs) | `ArgControlsTests`, `WorkbenchInspectorTests` |
| Workbench access control (404 for users who fail the policy) | [WorkbenchAccessTests.cs](BlazorTestPlatform.Demo.UI.Tests/WorkbenchAccessTests.cs) | `WorkbenchAccessTests` |
| Test impact analysis | [ImpactAnalysisTests.cs](BlazorTestPlatform.Demo.UI.Tests/ImpactAnalysisTests.cs) | `ImpactAnalysisTests` |
| Sharding (`BTP_SHARD=2/4`) | [BrowserMatrixTests.cs](BlazorTestPlatform.Demo.UI.Tests/BrowserMatrixTests.cs) | browser suites |
| Source-generated catalog, trimmed Release publish | [ComponentStoryTests.cs](BlazorTestPlatform.Demo.UI.Tests/ComponentStoryTests.cs), [ci.yml](../.github/workflows/ci.yml) | `GeneratedCatalogTests`, CI `trimmed-publish` |
| Baselines pinned to the Playwright version | [BaselineProvenanceTests.cs](BlazorTestPlatform.Demo.UI.Tests/BaselineProvenanceTests.cs) | `BaselineProvenanceTests` |
| Reports, pull-request comment, flake history | [ci.yml](../.github/workflows/ci.yml) | CI `Report` step |

## Committed test data

| Path | What | How it changes |
|---|---|---|
| `BlazorTestPlatform.Demo.UI.Tests/__snapshots__/` | Visual baselines. Only the Linux ones (`*-linux.png`) are committed; CI compares against them. Local runs on Windows or macOS write their own, which stay on that machine. | `scripts/visual-tests.sh --update` renders missing Linux baselines in the Playwright container (Docker), or run the "Regenerate visual baselines" workflow on your branch. `btp approve` accepts intended changes. |
| `BlazorTestPlatform.Demo.UI.Tests/__baselines__/performance.json` | Render-performance baselines. | Written for new stories on local runs; `btp perf approve` accepts intended changes. |
| `BlazorTestPlatform.Demo.UI.Tests/a11y-reviews/` | Manual WCAG 2.2 reviews, one per component, with the accessibility fingerprint they were made against. | `btp a11y init` copies templates for missing or stale reviews; a person fills them in. Components without a review are listed, not failed (`RequireReview = false`). |
| `BlazorTestPlatform.Demo.UI.Tests/btp.json` | Accessibility policy, visual threshold, stylesheets bUnit evaluates. | Edit by hand. |
| `BlazorTestPlatform.Demo.UI.Tests/quarantine.json` | Quarantined stories: they run and report, but cannot fail the build. The demo quarantines one chaos story. | `btp quarantine add/remove` or the hub. |
| `BlazorTestPlatform.Demo.UI.Tests/.btp/runs/` (not committed) | The run archive behind the hub's history. | Seeded on first start; `btp report --archive` and hub runs add to it. |

## Adding a story

1. Add a component to `BlazorTestPlatform.Demo.UI`, or pick one.
2. Add `Stories<TComponent>` to `BlazorTestPlatform.Demo.UI.Stories` (a `.cs` file: the catalog is source-generated from C#). Give every `bool` and enum value a story, or `StoryCoverageTests` fails.
3. `dotnet run --project demo/BlazorTestPlatform.Demo.Workbench` and look at it in every theme, viewport and render mode.
4. Run the tests. Local runs write your platform's missing baselines; render the Linux ones with `scripts/visual-tests.sh --update` (or the baselines workflow) and commit them.
