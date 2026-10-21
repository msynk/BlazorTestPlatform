# BlazorTestPlatform

**Write a component story once. Run it as a bUnit test, a real-browser interaction test, a visual snapshot, an accessibility audit, a render-performance check and a cross-hosting regression test. Browse it in a component workbench.**

BlazorTestPlatform is a quality platform for Blazor component libraries and apps. It does not replace bUnit or Playwright. It joins them, together with axe-core and a visual-regression engine, into one workflow built around a single source of truth: the **story**.

```csharp
public sealed class ButtonStories : Stories<Button>
{
    public override string Title => "Inputs/Button";

    protected override void Define(StoryCollectionBuilder<Button> stories)
    {
        stories.Defaults(s => s.ChildContent("Save changes"));

        stories.Add("Primary");
        stories.Add("Disabled", s => s.Arg(b => b.Disabled, true));

        stories.Add("Keyboard activation", s => s.Play(async ctx =>
        {
            await ctx.TabAsync();
            await ctx.Expect(ctx.GetByRole("button", "Save changes")).ToBeFocusedAsync();
            await ctx.PressAsync(Keys.Enter);
            await ctx.ExpectAction("OnClick").ToHaveBeenCalledAsync(times: 1);
        }));
    }
}
```

That one definition runs in all of these places:

| Where | What happens | Speed |
|---|---|---|
| **bUnit** (`BunitStoryRunner`) | Renders the story, runs the play script with browser-faithful keyboard, pointer and focus emulation, applies static accessibility rules, and checks render budgets | milliseconds |
| **Real browsers** (`BrowserStoryRunner`) | Runs the same play script in Chromium, Firefox or WebKit under **Static SSR, Interactive Server, WebAssembly and Auto**, then takes a visual snapshot, runs an axe-core scan, and checks console errors and the Blazor error UI | seconds |
| **Workbench** (`/_workbench`) | Lets you browse every story across themes, viewports and render modes, edit its parameters live with generated controls (kept in the URL, so links reproduce what you see), run its play script inside the frame (**▶ Run interactions**), and see its quality gates and manual review status | interactive |
| **Reports** (`btp report`) | Produces an HTML report with a visual-diff viewer, timelines, accessibility and performance details, JUnit XML, a GitHub step summary and cross-run flake history | — |

## Why this exists

The Blazor ecosystem already has good engines. bUnit is fast at component testing, Playwright drives real browsers, and axe-core is the standard accessibility engine. Visual regression is mostly available as a SaaS add-on. Each of these tools needs its own fixtures, its own selectors and its own CI wiring, and none of them knows about Blazor's render modes.

BlazorTestPlatform puts one layer above all of them:

* **One scenario model.** Locators (`GetByRole`, `GetByLabel`, `GetByText`...), actions and auto-waiting assertions behave the same in bUnit and in browsers. If a play script passes in-memory and fails in WebAssembly, you have found a real hosting bug, not a difference in test tooling.
* **Blazor-aware.** Waiting is hydration-aware, so a step never clicks before the circuit or the WebAssembly runtime is live. The runner verifies that the requested render mode actually ran. Services are overridden per story under Server and WebAssembly, and fake time can be advanced in the browser.
* **Quality gates as data.** Visual tolerances, accessibility suppressions (justified and time-boxed), performance budgets and baselines, and manual review fingerprints are all versioned with the code.

## Feature map

| Opportunity | What BlazorTestPlatform ships |
|---|---|
| **Visual regression** | Browser-rendered snapshots across viewports × themes × component states (`AddVariants`). Includes a dependency-free PNG codec and a perceptual YIQ diff with anti-aliasing detection, masks, tolerances, `initial` vs post-play baselines, `btp approve`, and a slider diff viewer. One baseline is shared by every render mode, so SSR, Server and WASM must render identically. |
| **Component interaction testing** | Reusable play scripts with keyboard (Tab order, chords, Enter/Space activation), pointer, focus (`ExpectTabOrderAsync`, roving tabindex, programmatic `FocusAsync`), async (auto-waiting assertions, `WaitForIdleAsync`) and fake time (`AdvanceTimeAsync`). One script runs through three drivers: bUnit, Playwright and an in-app driver behind the workbench's **Run interactions** button. Stylesheet-aware visibility with viewport-aware media queries (`bunit.stylesheets`, `bunit.viewport`) and JS-interop mocks (`MockJS`, optionally applied in browsers too, with `ExpectJSCall` in every driver) keep bUnit and the browser in step. |
| **Accessibility CI** | axe-core in browsers (bundled, offline). 37 axe-compatible markup rules in bUnit, with no browser needed. A policy with an impact threshold, WCAG tags, and justified, expiring suppressions. A **manual review workflow**: a WCAG 2.2 checklist per component, committed as JSON and kept honest by an accessibility *fingerprint* that marks a review as stale when the component's structure changes. |
| **Test-data framework** | Deterministic seeds (xoshiro256**, part of the API contract), seeded `FakeData`, reusable `StoryFixture`s, fake users (`AuthenticatedAs`, `Authenticating`), fake time (`UseFakeTime`), **story-scoped HTTP mocking** (`MockHttp` for `HttpClient` and named or typed `IHttpClientFactory` clients, with `ctx.ExpectRequest`), and per-story service overrides, including options, authorization handlers and multi-registered services. All of these also work under Server and WebAssembly. |
| **Flaky-test diagnostics** | A step timeline for every run (actions, assertions, waits, timings), plus a Playwright trace, failure screenshot, DOM snapshot, console log, network log and WebSocket (circuit) activity. Retries classify a story as *flaky* rather than passed, and cross-run flake history ranks stories by flake rate. |
| **Render regression tests** | Render counts (root and total, per component type), allocations and median time across iterations. Includes budgets (`MaxRootRenders`...), committed baselines with tolerances, and `btp perf approve`. |
| **Cross-hosting test matrix** | `TestMatrix.CrossHosting()` runs each story under SSR, Server, WebAssembly and Auto from a single workbench host. The runner checks hydration, the actual renderer, and console and circuit errors. Story discovery is source-generated and the packages are trimming-safe; CI re-runs the matrix against a trimmed Release publish. |

## Packages

| Package | Purpose | Depends on |
|---|---|---|
| `BlazorTestPlatform.Core` | Story model, unified interactions and assertions, matrix, test data (incl. HTTP mocking), quality-gate models, results. Ships the story-catalog source generator and the story analyzers (BTP001-BTP005). | Components.Web, Extensions.Http |
| `BlazorTestPlatform.Bunit` | In-memory driver and runner, render metrics, review fingerprints | bUnit, Accessibility |
| `BlazorTestPlatform.Accessibility` | ARIA role/name engine, Testing-Library-style queries, markup rules, fingerprints | AngleSharp |
| `BlazorTestPlatform.Workbench` | Component workbench UI, story frames for every render mode, per-story service overrides, in-app play driver | Components.Web |
| `BlazorTestPlatform.Playwright` | Browser driver, matrix runner, axe-core, diagnostics, in-process/external/process hosts | Playwright, Mvc.Testing |
| `BlazorTestPlatform.Visual` | PNG codec, perceptual diff, baseline store | — |
| `BlazorTestPlatform.Reporting` | HTML, JUnit and Markdown reports, flake history | — |
| `BlazorTestPlatform.Cli` (`btp`) | `report`, `approve`, `perf approve`, `a11y init/status`, `flaky`, `clean` | — |

Target framework: **.NET 10**.

## Quick start

The complete working version of these steps is in [`samples/`](samples): a design system (`Contoso.UI`), its stories, a workbench host (Server + WebAssembly) and a test project. The project runs 43 stories in bUnit, 52 cross-hosting browser cases, visual/axe cases across themes and viewports, and 26 in-app play runs.

**1. Write stories** in a class library that references your components and `BlazorTestPlatform.Core` (see [docs/stories.md](docs/stories.md)).

**2. Run every story in-memory** (xUnit shown; any framework works, because story ids are plain strings):

```csharp
public sealed class ComponentStoryTests
{
    public static readonly StoryCatalog Catalog = StoryCatalog.FromAssemblyOf<ButtonStories>();
    public static TheoryData<string> Stories => new(Catalog.Ids);

    [Theory, MemberData(nameof(Stories))]
    public Task Story_passes(string id) => BunitStoryRunner.VerifyAsync(Catalog.Get(id));
}
```

**3. Host the workbench.** In a Blazor Web App with Server and WebAssembly interactivity, register it in both `Program.cs` files, after your own services:

```csharp
builder.Services.AddBlazorTestWorkbench(o => o.AddStoriesFromAssemblyOf<ButtonStories>());
// server only:
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddInteractiveWebAssemblyRenderMode()
   .AddAdditionalAssemblies(typeof(WorkbenchOptions).Assembly);
```

Then open `/_workbench`.

**4. Run the cross-hosting matrix in real browsers:**

```csharp
public sealed class Workbench : IAsyncLifetime
{
    public WorkbenchTestEnvironment Environment { get; private set; } = default!;
    public async ValueTask InitializeAsync() =>
        Environment = await WorkbenchTestEnvironment.StartAsync(new WorkbenchApplicationFactory<Program>());
    public ValueTask DisposeAsync() => Environment.DisposeAsync();
}

public sealed class CrossHostingTests(Workbench workbench)
{
    static readonly TestMatrix Matrix = TestMatrix.CrossHosting();
    public static TheoryData<string> Cases => new(Matrix.Expand(ComponentStoryTests.Catalog).Select(c => c.Key));

    [Theory, MemberData(nameof(Cases))]
    public Task Story_passes_everywhere(string key) =>
        workbench.Environment.VerifyAsync(Matrix.Resolve(ComponentStoryTests.Catalog, key));
}
```

**5. Report and approve:**

```bash
dotnet tool install -g BlazorTestPlatform.Cli
btp report --junit TestResults/junit.xml --github-summary --history .btp/history.json
btp approve            # accept intended visual changes, then commit __snapshots__/
btp perf approve       # accept intended render-performance changes
```

## Guides

* [Getting started](docs/getting-started.md): packages, project layout and a first green run
* [Writing stories](docs/stories.md): args, bindings, actions, decorators, variants, layouts and global defaults
* [Interactions and assertions](docs/interactions.md): locators, keyboard, focus, pointer, async and capabilities
* [Test data](docs/test-data.md): seeds, fake data, fixtures, fake users, fake time and service overrides
* [Cross-hosting matrix](docs/cross-hosting.md): render modes, hydration, prerendering and hosts
* [Quality gates](docs/quality-gates.md): visual regression, accessibility (automated and manual review) and render performance
* [Diagnostics, reports and CI](docs/ci.md): timelines, traces, flake history, the `btp` CLI and GitHub Actions
* [Configuration](docs/configuration.md): `btp.json` and `BTP_*` environment variables
* [Architecture](docs/architecture.md): how the pieces fit, and how to write a new driver

## How it relates to other tools

| Tool | Relationship |
|---|---|
| **bUnit** | The in-memory engine. BlazorTestPlatform adds stories, browser-faithful interactions, a11y rules, render budgets and reporting on top. Plain bUnit tests keep working next to stories. |
| **Playwright** | The browser engine. BlazorTestPlatform adds Blazor hydration awareness, the render-mode matrix, the story frame bridge (actions, fake time), axe-core, visual baselines and diagnostics. |
| **Storybook** | The same story/workbench idea, native to Blazor and .NET. Stories are C#, and play scripts run in tests as well as in the UI. |
| **Chromatic / Percy** | Self-hosted visual regression: baselines live in git and are reviewed in the HTML report. No snapshot quota, and no component code leaves your network. |
| **axe-core** | Bundled and run in every browser cell. The in-memory rules use axe rule ids, so findings line up across engines. |
| **Testing Library** | The locator philosophy (role, label, text first), implemented for both drivers. |

## Status and roadmap

This is the `0.1.0-preview.2` foundation. Planned next:

* Firefox and WebKit baselines in the CI container
* Coverage of story states per component, and a PR comment bot
* Layout-aware checks in bUnit (sizes, overflow, overlap). bUnit evaluates CSS visibility and media queries but does no layout, so stories that depend on geometry declare it and are skipped there.

## Repository layout

```
src/        the packages
tests/      unit and driver tests for every package (280 tests)
samples/    Contoso.UI design system, stories, workbench host (Server + WASM), end-to-end tests
docs/       guides
```

```bash
dotnet build BlazorTestPlatform.slnx
dotnet test --project tests/BlazorTestPlatform.Bunit.Tests       # or any test project
dotnet test --project samples/Contoso.UI.Tests                   # needs Playwright browsers
scripts/visual-tests.sh                                          # browser suites in the CI container (Docker)
```

## License

MIT
