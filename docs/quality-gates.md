# Quality gates

## Visual regression

**Capture.** The runner screenshots the story root element (or the full page, with `Visual(v => v.FullPage = true)`). Before the capture it does the following:

* stops animations, transitions and the caret
* waits for fonts and images
* waits for `WaitForSelector` and any `Delay` you configured
* masks the selectors listed in `Masks` with solid magenta

**Naming.** A snapshot is stored as `__snapshots__/{story-id}/{viewport}.{theme}.{browser}-{platform}[.initial].png`. The name has no render mode, so Server, WebAssembly, Auto and SSR must all match the same image. For a story with a play function, the interactive modes compare the state after play, while static SSR (which can't run play) compares the `.initial` image, the state before play. The platform is part of the name because font rasterisation differs between operating systems; to share baselines across operating systems, run your browsers in a container.

**Comparison.** The comparer measures perceptual YIQ distance per pixel (`Threshold`, default 0.1) and ignores anti-aliasing. A run can tolerate a fixed number of differing pixels (`MaxDiffPixels`) or a ratio of them (`MaxDiffRatio`). A size change always fails.

**Update modes** (`BTP_UPDATE_SNAPSHOTS`):

| Mode | Behaviour |
|---|---|
| `missing` | Write missing baselines. The default locally. |
| `none` | Never write; missing baselines fail. The default on CI. |
| `changed` | Overwrite baselines that differ. |
| `all` | Rewrite every baseline. |

**Orphaned baselines.** Renaming or removing a story, or turning its snapshots off with `NoVisual()`, leaves baselines
nobody compares against (Jest's "obsolete snapshots"). `SnapshotStore.FindOrphanedBaselines(catalog.Stories)` lists
them and `PruneOrphanedBaselines(...)` deletes them; pass every story that writes into the directory. The demo gates on
it (`BaselineProvenanceTests.Every_baseline_belongs_to_a_story_that_takes_snapshots`).

**Themes for direction and contrast.** `ThemeDefinition.RightToLeft` (`rtl`) renders stories inside `<div dir="rtl">` in
every driver and sets `dir="rtl"` on the story frame, and `ThemeDefinition.HighContrast` (`high-contrast`) emulates
`forced-colors: active` and `prefers-contrast: more` in browsers and in bUnit's media queries. Add them to a matrix
(`WithThemes(ThemeDefinition.Light, ThemeDefinition.RightToLeft)`); the workbench resolves them by name even when they
are not in its toolbar. Your own themes can set `Direction`, `ForcedColors` and `MoreContrast`.

**Review.** A mismatch writes `expected`, `actual` and `diff` images under `TestResults/btp/visual/`. `btp report` shows them side by side, with a slider overlay. `btp approve [--filter 'inputs-*'] [--dry-run]` promotes the actual images to baselines.

### Ignoring regions in markup

An element with the `data-btp-ignore` attribute is masked in every visual snapshot, as Chromatic's `data-chromatic="ignore"`: put it on a timestamp, a random avatar or a live chart in the component's markup instead of listing a selector in each story (`Visual(v => v.Masks.Add(...))` still works). The box stays, so a change of its size or position still shows.

### Screenshots of play states

`ctx.ExpectScreenshotAsync("name")` takes more snapshots inside the play script, each with its own baseline next to the story's (see [interactions](interactions.md#screenshots-and-accessibility-in-the-middle-of-play)). They follow the same rules: written when missing locally, never on CI, reverted when the run fails another check, listed by `btp approve`, reviewed in the hub's visual page and the HTML report, and counted as visual changes in summaries and SARIF.

## Accessibility

There are two engines, both reporting axe rule ids:

| Engine | Where | Covers |
|---|---|---|
| Markup rules (`btp-markup`) | bUnit, in milliseconds | 37 rules: names (buttons, links, inputs, images, frames, dialogs...), ARIA validity (roles, attributes, values, required attributes, required children and parents), `aria-hidden` focusability, nested interactive elements, tabindex, duplicate ids, list and definition-list structure, heading order, multiple labels |
| axe-core (bundled) | Every browser cell, scoped to the story root (`[data-btp-root]`) | Every axe rule with the policy's tags, including color contrast, target size and scrollable regions. Page-level rules (such as `landmark-one-main`) find no page around a story, so they effectively don't apply. |

The markup rules follow axe-core where it does not fail markup: empty id references, `aria-controls` on collapsed disclosures and unselected tabs (whose panels are not rendered), `aria-owns` on collapsed comboboxes, missing `aria-labelledby`/`aria-describedby` targets (axe reports them for review), empty lists, listboxes, menus, tablists, trees and grids, and hidden elements are not violations; `nested-interactive` only checks roles whose children are presentational (buttons, checkboxes, tabs, options...), so links and APG tree items may contain focusable content. An invalid field's `aria-errormessage` must be announced (a live region such as `role="alert"`, or also listed in `aria-describedby`), as axe-core requires; a hidden message is not reported. The markup engine checks structure only; color contrast, target size and anything else that needs layout run in browsers through axe-core.

**Policy** (`btp.json` → `accessibility`, or `AccessibilityPolicy` in code):

* `failOn`: the minimum impact that fails the build. Default `serious`.
* `tags`: the axe tags to run. Default WCAG 2.0/2.1/2.2 A and AA.
* `includeBestPractices`: also run best-practice rules (default `true`). They gate like any other rule, by impact, so only serious and critical ones fail by default.
* `disabledRules`: rules turned off globally.
* `suppressions`: entries of the form `{ ruleId, storyPattern, targetContains, justification, expires }`. `targetContains` limits a suppression to violations whose elements' selectors all contain the text. A suppression stops applying after its expiry date, so accepted debt resurfaces automatically.

A story can disable a rule only with a written justification: `DisableAccessibilityRule("color-contrast", "Approved by design, #123")`. A rule added through `Accessibility(a => a.DisabledRules.Add(...))` without a justification fails when the catalog is built. Justifications appear in the workbench, and the HTML report lists each run's **waived rules**: the story's disabled rules with their justifications, rules disabled for every story, and the active suppressions that match the story. `AccessibilityPolicy.WaiversFor(storyId, options)` returns the same list. `Accessibility(a => a.Skip = true)` skips every automated check for a story. `Accessibility(a => a.ReportOnly = true)` keeps scanning but turns every violation into a warning (Storybook's a11y `todo`): use it to adopt the gate on components with existing debt, then remove it story by story.

### Manual review workflow

Automated tools catch only part of WCAG issues. The rest need a person: keyboard traps, meaningful focus order, content on hover, reflow, screen-reader announcements and similar checks.

1. `AccessibilityReviews.VerifyAsync(catalog)` in a test renders every story of each component and computes an **accessibility fingerprint**. The fingerprint covers roles, ARIA attribute names, focusability, input types and whether each element has a name. It ignores text and styling. It is computed over the *distinct* structures the stories render, so a new story that repeats a state another story already renders (one that only adds a play script) or a reordering keeps the review current (`AccessibilityFingerprint.ComputeSet`). Reviews recorded with the ordered fingerprint of earlier versions stay current until the component's structure changes.
2. Components without a review, and components whose review is stale, get a template under `TestResults/btp/a11y-review-templates/`. Run `btp a11y init` to copy the templates into `a11y-reviews/` (`--force` replaces stale reviews).
3. A reviewer fills in the WCAG 2.2 checklist, plus `reviewer`, `reviewedAt` and `assistiveTechnology`, and commits the file. Each of the 20 checks in the template carries its title, WCAG criterion and guidance on how to verify it.
4. The gate reports each component as `Approved`, `Missing`, `Incomplete`, `Failed` or **`Stale`**. A review is stale when the fingerprint changed since it was done, so a refactor that removes a label invalidates its review. The gate writes its verdicts to `TestResults/btp/a11y-review-gate.json`.
5. `btp a11y status` prints every component with its gate state from the last gate run and its check counts. The workbench shows each component's review next to its stories (with each check's guidance on hover) when `WorkbenchOptions.ReviewsDirectory` points at the reviews. It reads the files on the server, so the review doesn't appear when the shell renders under WebAssembly.

Choose what fails with `ManualReviewGateOptions`. By default only stale reviews (`FailOnStale`) and failed checks (`FailOnFailedChecks`) fail, so teams can adopt the gate gradually. Set `RequireReview = true` to fail components without a review, and `FailOnIncomplete = true` to fail reviews with unchecked items.

## Render performance

```csharp
s.Performance(p =>
{
    p.MaxRootRenders = 2;          // absolute budgets
    p.MaxTotalRenders = 6;
    p.MaxAllocatedBytes = 64_000;
    p.Iterations = 5;              // median of 5, after 1 warm-up
});
```

* Metrics come from bUnit: render counts of the root and of every live component (per type) after play, median time of render + play, and managed allocations of the initial render. Allocations exclude play scripts, whose DOM queries would dwarf the component; interaction cost shows up in render counts and time. Allocations are counted on the rendering thread, so tests running in parallel (or an in-process web host) don't inflate them.
* Budgets fail immediately when exceeded.
* Baselines (`__baselines__/performance.json`) catch drift. Render counts are compared exactly. Allocations get +10% and 4 KiB of slack. Timing gets +50% and 5 ms, and only warns by default because shared CI agents are noisy. A missing baseline is written on local runs; on CI it isn't written and doesn't fail the build, and the report shows that the story has no baseline. Set `TrackBaseline = false` in `Performance(...)` to check the absolute budgets only.
* Accept intended changes with `BTP_UPDATE_PERF=1` or `btp perf approve`.
* Timing is the noisy metric: for stable durations, run performance stories in a non-parallel test collection.

## Browser runtime metrics

Every real-browser run records what the browser reports through the Performance Timeline (`StoryResult.BrowserMetrics`):
cumulative layout shift, largest contentful paint, long tasks (count and total time), the element count of the story
root and the JS heap size. Browsers report different sets: Chromium all of them, Firefox the largest contentful paint
(no layout shifts, long tasks or heap size), WebKit fewer still; a metric a browser does not report is null, never 0.

```csharp
s.BrowserBudget(b =>
{
    b.MaxLayoutShift = 0.1;                                   // Core Web Vitals "good"
    b.MaxLargestContentfulPaint = TimeSpan.FromSeconds(2.5);
    b.MaxLongTaskDuration = TimeSpan.FromMilliseconds(200);   // main-thread blocking during render and play
    b.MaxDomNodes = 400;
});
```

Budgets are checked after the play function and fail the run when exceeded (the violations are on the result and in the
report). A limit on a metric the browser does not report is listed in the timeline as not checked, so Firefox and WebKit
runs never pass a layout-shift budget silently. Metrics cover the story frame: render, hydration and play, not the
workbench shell.

## Leaks

The bUnit runner fails a story whose components, once disposed, are still referenced, as Flutter's leak tracking does
for widgets:

* a `DotNetObjectReference` they passed to JavaScript and did not dispose (JavaScript could still call the disposed
  component, and the reference keeps it in memory);
* a handler they added to an event of a service they injected (`[Inject]` properties, constructor-injected fields) and did
  not remove: `NavigationManager.LocationChanged`, an app state container's `Changed`, a subscriber list. A singleton or a
  circuit-scoped service would keep calling the removed component.

```text
The story's components left 2 reference(s) behind when they were disposed:
  Notifier.Changed still calls LeakySubscriber.OnChanged after the component was disposed: ... Remove the handler when the component is disposed.
  DotNetObjectReference<Chart> passed to JavaScript (chart.observe) was not disposed: ...
```

It runs after the story passes, on every component the story showed: those on screen at the end (bUnit disposes them with
the renderer) and those play removed. Method groups, lambdas and closures that capture the component are recognized.
Handlers on transient services do not count (the service goes with the component), nor do references to services (an
app-level registration may live as long as the service); only delegate fields and delegate collections of a service are
read, so lazy or large collections are never enumerated. `AllowLeaks(reason)` on a story accepts a leak on
purpose and `BunitStoryOptions.DetectLeaks = false` turns the check off. Browsers do not report leaks: Blazor does not
expose which references a circuit still tracks.

## Markup snapshots

`ctx.ExpectMarkupSnapshotAsync()` compares the story's markup with a committed file, as Verify and Jest snapshots do:
`__snapshots__/{story-id}/story.markup.html` (or `{name}.markup.html` with a name, and an element with `target:`).

```html
<div class="c-alert c-alert--info" role="status">
  <div class="c-alert__body">
    <p class="c-alert__title">Heads up</p>
    <div>Your trial ends in 3 days.</div>
  </div>
  <button aria-label="Dismiss" class="c-alert__close" type="button">×</button>
</div>
```

The markup is normalized so that only real changes show: one element per line, attributes and class tokens sorted,
whitespace collapsed and adjacent text merged (but kept as written in `pre`, `textarea`, `script` and `style`, and
`&nbsp;` shown), comments and the attributes Blazor adds (`blazor:onclick`, `_bl_` element references, CSS-isolation
scopes like `b-3xxtam6d07`) removed, and the content of `data-btp-ignore` elements, whatever it is, replaced by `…`.
Whitespace between inline elements is not represented (each node has its own line); a visual snapshot catches spacing. Scrubbers then replace what changes between runs: GUIDs become `{guid}`, and a story adds its
own with `ScrubMarkup(@"\d{4}-\d{2}-\d{2}", "{date}")`. Runs with a theme or arg overrides get their own files.

A missing snapshot is written locally and fails on CI. A different one fails with the changed lines and leaves
`{name}.markup.received.html` beside the snapshot (also attached to the run); `btp snapshots accept [--dry-run]` makes
every received file the snapshot, and `BTP_UPDATE_SNAPSHOTS=changed` overwrites while running. A snapshot written by a run
that then fails is removed again. bUnit compares markup snapshots; browsers and the in-app driver record the check as
skipped, because their DOM carries live state (input values, hydration markers) differently. For structure that must
hold in every driver, use [accessibility tree snapshots](#accessibility-tree-snapshots).

## Accessibility tree snapshots

`ctx.ExpectAriaSnapshotAsync(...)` asserts the roles, names and states of a story as a tree, identically under bUnit,
Playwright and the in-app driver ([interactions](interactions.md#accessibility-tree-snapshots)). Use it for structure
that matters to screen-reader users (landmarks, heading levels, control names), next to axe for rule violations.

## Story state coverage

Stories document a component's states, so a state without a story is a state nothing renders: no snapshot, no accessibility scan, no interaction test. `StoryCoverage` measures this. For every `bool` and enum parameter (and their nullable forms, where `null` is a state), it lists the values that at least one story renders, using the story's argument or else the component's default, and the values that no story renders:

```csharp
[Fact]
public void Every_component_state_has_a_story()
{
    var report = StoryCoverage.Analyze(ComponentStoryTests.Catalog, StoryCoverage.ComponentsIn(typeof(Button).Assembly));
    report.Save(BtpSettings.Current.ResultsDirectoryPath);   // picked up by btp report
    report.ThrowIfBelow(0.9, requireStoriesForEveryComponent: true);
}
```

* Variants (`AddVariants`) are ordinary stories, so a variant matrix covers its parameters in one line.
* Values computed per render (`Arg(b => b.X, ctx => ...)`) can't be known without rendering. They're listed under `computedIn` and don't count.
* Open-ended parameters (strings, numbers, dates) are listed with the distinct values the stories use, but don't count towards the ratio.
* `ComponentsIn(assembly)` lists the public, concrete components of a library (layouts and `_Imports` excluded), so `ComponentsWithoutStories` shows the components that have no stories at all.

`btp report` adds the coverage to the HTML report and to the Markdown summary, and so to the pull-request comment. The workbench shows each component's covered and missing states under **Quality gates**.

**Code coverage next to states.** Run the tests with a code coverage collector writing Cobertura, and `btp report` puts
each component's line and branch coverage next to its states, in the HTML report and the hub, so a component whose
states all have stories but whose code is barely run (or the reverse) stands out:

```bash
# Microsoft.Testing.Extensions.CodeCoverage (or coverlet's --collect "XPlat Code Coverage")
dotnet test --project MyLib.Tests -- --coverage --coverage-output-format cobertura --coverage-output stories.cobertura.xml
btp report --code-coverage TestResults            # files or directories; default: *.cobertura.xml under the results
```

Reports from several test projects or runs (bUnit and browsers) merge line by line, and the classes the compiler
generates for a component (lambdas, async state machines) count towards it. `StoryCoverageReport.WithCodeCoverage` and
`CoberturaCoverage.Parse` do the same in code. In the demo, the 16 components' stories run 88% of their lines.
