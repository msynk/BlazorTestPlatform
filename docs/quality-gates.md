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

**Review.** A mismatch writes `expected`, `actual` and `diff` images under `TestResults/btp/visual/`. `btp report` shows them side by side, with a slider overlay. `btp approve [--filter 'inputs-*'] [--dry-run]` promotes the actual images to baselines.

## Accessibility

There are two engines, both reporting axe rule ids:

| Engine | Where | Covers |
|---|---|---|
| Markup rules (`btp-markup`) | bUnit, in milliseconds | 37 rules: names (buttons, links, inputs, images, frames, dialogs...), ARIA validity (roles, attributes, values, required attributes, required children and parents), `aria-hidden` focusability, nested interactive elements, tabindex, duplicate ids, list and definition-list structure, heading order, multiple labels |
| axe-core (bundled) | Every browser cell, scoped to the story root (`[data-btp-root]`) | Every axe rule with the policy's tags, including color contrast, target size and scrollable regions. Page-level rules (such as `landmark-one-main`) find no page around a story, so they effectively don't apply. |

**Policy** (`btp.json` → `accessibility`, or `AccessibilityPolicy` in code):

* `failOn`: the minimum impact that fails the build. Default `serious`.
* `tags`: the axe tags to run. Default WCAG 2.0/2.1/2.2 A and AA.
* `includeBestPractices`: also run best-practice rules (default `true`). They gate like any other rule, by impact, so only serious and critical ones fail by default.
* `disabledRules`: rules turned off globally.
* `suppressions`: entries of the form `{ ruleId, storyPattern, targetContains, justification, expires }`. `targetContains` limits a suppression to violations whose elements' selectors all contain the text. A suppression stops applying after its expiry date, so accepted debt resurfaces automatically.

A story can disable a rule only with a written justification: `DisableAccessibilityRule("color-contrast", "Approved by design, #123")`. A rule added through `Accessibility(a => a.DisabledRules.Add(...))` without a justification fails when the catalog is built. Justifications appear in the workbench, and the HTML report lists each run's **waived rules**: the story's disabled rules with their justifications, rules disabled for every story, and the active suppressions that match the story. `AccessibilityPolicy.WaiversFor(storyId, options)` returns the same list. `Accessibility(a => a.Skip = true)` skips every automated check for a story.

### Manual review workflow

Automated tools catch only part of WCAG issues. The rest need a person: keyboard traps, meaningful focus order, content on hover, reflow, screen-reader announcements and similar checks.

1. `AccessibilityReviews.VerifyAsync(catalog)` in a test renders every story of each component and computes an **accessibility fingerprint**. The fingerprint covers roles, ARIA attribute names, focusability, input types and whether each element has a name. It ignores text and styling.
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
