# Architecture

```
                         ┌───────────────────────────── BlazorTestPlatform.Core ─────────────────────────────┐
  Stories<T> ──build──▶  │ Story (args, decorators, play, fixtures, user, fake time, gate options)           │
  IStoryConfiguration    │ StoryCatalog · TestMatrix/MatrixCase · StoryRoot (renders any story)             │
                         │ StoryContext ─▶ ElementLocator / LocatorAssertions / ActionAssertions / Poll       │
                         │        │            (locator semantics, auto-wait, strictness, timeline)           │
                         │        ▼                                                                           │
                         │ IStoryDriver  (Query, Perform, Keyboard, Focused, Actions, Idle, AdvanceTime)     │
                         │ Results (StoryResult, StepRecord) · Accessibility/Performance models · Settings   │
                         └───────────┬───────────────────────────────────────────────┬───────────────────────┘
                                     │                                               │
               ┌──────────── BlazorTestPlatform.Bunit ───────────┐   ┌──────── BlazorTestPlatform.Playwright ───────┐
               │ BunitStoryDriver  (event-faithful emulation)    │   │ PlaywrightStoryDriver (locators → Playwright) │
               │ BunitStoryRunner  (play, markup a11y, budgets)  │   │ WorkbenchBrowser/StoryPage (hydration-aware)  │
               │ AccessibilityReviews (fingerprints)             │   │ BrowserStoryRunner (retries, visual, axe,     │
               └─────────────────────┬───────────────────────────┘   │   console/error-UI checks, artifacts)         │
                                     │                               │ Hosts: Kestrel WAF · process · external       │
               ┌─ BlazorTestPlatform.Accessibility ─┐                └──────────┬──────────────────────┬─────────────┘
               │ ARIA roles & names · DomQueries    │                           │ HTTP                 │ PNG
               │ 37 markup rules · fingerprints     │       ┌── BlazorTestPlatform.Workbench ──┐  ┌── .Visual ──────────┐
               └────────────────────────────────────┘       │ /_workbench (shell UI)           │  │ PngCodec · YIQ diff │
                                                            │ /_workbench/frame/{id}?mode=…    │  │ SnapshotStore       │
                                                            │ StoryHost (status, bridge)       │  └─────────────────────┘
                                                            │ StoryServiceScope (overrides)    │
                                                            └──────────────────────────────────┘
               BlazorTestPlatform.Reporting (HTML · JUnit · Markdown · flake history) ◀── results JSON ──▶ btp CLI
```

## Discovery without reflection

`BlazorTestPlatform.Generators` ships inside the Core package. For every assembly that declares story collections or `IStoryConfiguration`s, it emits an `IStoryCatalogProvider`, plus an assembly attribute pointing at it. `StoryCatalog.FromAssemblies` prefers the provider and falls back to a reflection scan for assemblies built without the generator. The selection rules are identical, and a test checks that the generated and reflected catalogs match.

## Design principles

1. **Stories are the single source of truth.** Every feature consumes `Story`: the workbench, both drivers, the matrix, gates and reports. Nothing is configured twice.
2. **Drivers implement primitives; the core implements semantics.** Locator chains, auto-waiting, strictness, assertion messages and timelines live in Core, which is why the two drivers behave alike. A new driver (for example, an in-browser driver for the workbench UI or a MAUI/WebView driver) implements `IStoryDriver` and inherits all of it.
3. **One renderer for every host.** `StoryRoot` renders decorators, bindings, fake authentication and the component identically in bUnit and in the frame's `StoryHost` under every render mode.
4. **Determinism by construction.** Seeds, fake time, a fixed locale and time zone, reduced motion, stabilised screenshots, hydration-aware waits and render-mode verification remove sources of nondeterminism up front; retries are a last resort, not the strategy.
5. **Gates are versioned data.** Baselines, budgets, suppressions (with expiry) and manual reviews (with fingerprints) are files in the repository, reviewed in pull requests.
6. **No lock-in.** The packages work with xUnit, NUnit and MSTest, depend on no SaaS, write portable JSON, and bundle axe-core so the platform runs offline.

## The story frame contract

`StoryHost` renders a root element:

```html
<div data-btp-root data-btp-story="inputs-button--primary"
     data-btp-status="loading|ready|error"
     data-btp-renderer="Static|Server|WebAssembly"
     data-btp-interactive="true|false"
     data-btp-layout="padded|centered|fullscreen"
     data-btp-unsupported-overrides="…">…</div>
```

`data-btp-unsupported-overrides` is present only when the story registers services that cannot apply per story in browsers (see [test data](test-data.md#how-overrides-reach-server-and-webassembly)).

In interactive modes it also registers `window.__btp` with `getActions()`, `getRequests()`, `getJSCalls()`, `setArgs(token)`, `hasFakeTime()`, `advanceTime(ms)` and `waitForIdle()`. Frame URLs accept arg overrides as `arg.Name=value`; the accepted ones are echoed in `data-btp-args` and rejected ones are listed in `[data-btp-arg-errors]`. With `?play=1`, the frame runs the play function in-app and then sets `data-btp-play-status` to `passed`, `failed` or `skipped`. It also exposes `window.__btpPlayResult` and posts a `play-result` message to the parent window. External tools, for example an existing Playwright test suite in TypeScript, can use this contract directly.

## Writing a driver

Implement `IStoryDriver`:

```csharp
Task<IReadOnlyList<ElementInfo>> QueryAsync(LocatorChain locator, CancellationToken ct);
Task PerformAsync(LocatorChain locator, ElementAction action, TimeSpan timeout, CancellationToken ct);
Task KeyboardAsync(KeyboardAction action, CancellationToken ct);
Task<ElementInfo?> GetFocusedElementAsync(CancellationToken ct);
Task<IReadOnlyList<ActionRecord>> GetActionsAsync(CancellationToken ct);
Task WaitForIdleAsync(CancellationToken ct);
Task AdvanceTimeAsync(TimeSpan delta, CancellationToken ct);
Task<string> GetMarkupAsync(CancellationToken ct);
// Optional (default implementations return nothing):
Task<IReadOnlyList<HttpRequestRecord>> GetHttpRequestsAsync(CancellationToken ct);
Task<IReadOnlyList<JSCallRecord>> GetJSCallsAsync(CancellationToken ct);
```

To support JS mocks and `ExpectJSCall`, wrap the `IJSRuntime` your host gives components with `StoryJSRuntime.Create(runtime, story.JSInterop, log, mode)`. `JSMockMode.Answer` answers mocks in the wrapper, `AnsweredByInner` lets a mocking runtime such as bUnit's answer them and only records, and `RecordOnly` records while real JavaScript answers.

Then run stories through `new StoryContext(story, driver)` and `story.Play(context)`. `BlazorTestPlatform.Accessibility.Dom.DomQueries` resolves locator chains against any AngleSharp DOM.
