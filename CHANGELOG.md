# Changelog

## Unreleased

* **Live controls in the workbench**: a Controls panel generated from the component's parameter types (text, checkbox, number, drop-down, date and time inputs, with nullable variants). Interactive frames re-render in place through `window.__btp.setArgs`, two-way bindings included, and static frames reload. Edits are kept in the URL (`?arg.Name=value`) so links reproduce them. Values from links can't contain `<`. The same overrides are available to tests through `BunitStoryOptions.Args`, `StoryPageOptions.Args` and `StoryRenderContext.SetArgOverridesAsync`, and the rules are exposed by `StoryArgs`.
* **Story analyzers** in the Core package: `BTP001` (argument isn't a component parameter), `BTP002` (binding without a matching `Changed` callback), `BTP003` (`Attribute` used for a declared parameter), `BTP004` (component can't take unmatched attributes) and `BTP005` (duplicate story id). Each one reports at compile time a mistake that used to fail when the catalog was built or the story rendered.

## 0.1.0-preview.2

* **Story-scoped HTTP mocking**: `MockHttp(...)` answers requests from `HttpClient` and from named or typed `IHttpClientFactory` clients in bUnit, Interactive Server and WebAssembly. It supports wildcards, `Times`, `Delay` (on fake time), `Never`, `Fail` and request recording. Assert with `ctx.ExpectRequest(...)`; requests appear in the HTML report.
* **Run interactions in the workbench**: a third driver (`InAppStoryDriver`) runs play scripts inside the frame under the real render mode, using native activation, focus and `preventDefault`. The shell shows the live timeline. `StoryPageOptions.AutoPlay` runs it from tests.
* **Source-generated story discovery**: `BlazorTestPlatform.Generators` ships inside the Core package. Core and Workbench are trimming-safe, with zero trim warnings and source-generated JSON throughout. The suites run against a trimmed Release publish on CI.
* **bUnit fidelity**: stylesheet-aware visibility (a cascade for `display`/`visibility`, including CSS-isolation bundles) via `bunit.stylesheets` or `BunitStoryOptions.Stylesheets`. `@media` queries are evaluated against a viewport (`BunitStoryOptions.Viewport`, `bunit.viewport`, `BTP_BUNIT_VIEWPORT`; desktop by default, as in browser runs) and the theme's color scheme (`BunitStoryOptions.Theme`): width, height and range syntax, orientation, `prefers-color-scheme`, `prefers-reduced-motion`, `hover`, `pointer`, nested `@media` and `@supports`.
* **JS-interop mocks everywhere**: `MockJS(...)` answers in bUnit and, with `ApplyInBrowsers()`, in workbench frames under Server, WebAssembly and Auto. Unmocked calls reach the real JavaScript (or fail in strict mode), and Blazor's own calls are never touched. Stories with mocks record their JS calls under every driver: `ctx.ExpectJSCall(...)` and `ctx.GetJSCallsAsync()`.
* **Per-story overrides of multi-registration services in browsers**: story registrations are composed with the application's (`IEnumerable<T>` yields both, `T` yields the story's), so options (`IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>` per story), authorization handlers and your own multi-registered services work under every render mode. Contracts consumed at start-up (hosted services, logger providers, startup filters, circuit handlers, handler-builder filters) are flagged on the story in the shell, the frame and the run timeline instead of being ignored silently.
* **Linux baselines**: `scripts/visual-tests.sh` / `.ps1` run the browser suites in the Playwright image used by CI, so CI gates on `chromium-linux` baselines. The image tag is derived from the pinned `Microsoft.Playwright` version in the scripts and the workflow.
* **Painless Playwright upgrades**: `__snapshots__/baselines.json` records the renderer behind each `{browser}-{platform}` baseline set. After an upgrade, `BaselineProvenanceTests` fail fast, visual mismatches explain that the browser changed, and the **Regenerate visual baselines** workflow commits the new baselines to the branch. Dependabot is configured for `Microsoft.Playwright`.
* `BrowserRunOptions.ConfigurePageAsync` hook for Playwright routing; `WorkbenchBrowser.CreatePageAsync`.
* Fixes:
  * Trimmed WebAssembly clients could not render stories, because `StoryHost` was trimmed away.
  * Navigation failures lost their browser diagnostics.
  * Story overrides of multi-registration services (options, filters) collapsed the app's registrations.
  * `scripts/visual-tests.sh` failed under Git Bash on Windows (container paths were rewritten).
  * Render allocation measurements also counted whatever ran in parallel (other tests, an in-process web host), which made allocation budgets flaky in mixed suites. They now count the rendering thread only. Re-record allocation baselines with `BTP_UPDATE_PERF=1`; the numbers are lower and more stable.
  * Infinite performance ratios broke result serialization.
  * Calling `AddBlazorTestWorkbench` twice is now rejected.

## 0.1.0-preview.1

First preview of BlazorTestPlatform.

* **Stories**: `Stories<T>` collections with args, two-way `Bind`, automatic action logging, decorators, variants, layouts, assembly-wide defaults, and discovery through `StoryCatalog`.
* **Unified interactions**: Testing-Library-style locators, keyboard, pointer and focus actions, auto-waiting assertions, action assertions, step timelines and driver capabilities.
* **bUnit driver**: browser-faithful emulation (focus movement, Tab order, change-on-blur, Enter/Space activation, label forwarding, programmatic `FocusAsync`), fake time, fake users, fixtures, 37 static accessibility rules, and render budgets and baselines with calibrated allocation measurement.
* **Playwright driver**: hydration-aware story frames under Static SSR, Server, WebAssembly and Auto; render-mode verification; visual snapshots; axe-core; console and Blazor-error-UI checks; retries with flaky classification; traces, screenshots and DOM artifacts; Kestrel, process and external hosts.
* **Workbench**: `/_workbench` UI with theme, viewport and render-mode switching; per-story service overrides under every render mode; a frame bridge for actions, fake time and idleness; and a view of manual review status.
* **Visual**: a dependency-free PNG codec, perceptual YIQ diff with anti-aliasing detection, and a baseline store with update modes and approval.
* **Accessibility**: an ARIA role and accessible-name engine, an accessibility policy with expiring suppressions, and a manual WCAG 2.2 review workflow with structural fingerprints.
* **Reporting and CLI**: an HTML report with a visual-diff viewer, JUnit, Markdown and GitHub summaries, flake history, and `btp report`, `approve`, `perf approve`, `a11y init/status`, `flaky` and `clean`.
