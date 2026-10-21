# Quality gates

## Visual regression

**Capture.** The runner screenshots the story root element (or the full page, with `Visual(v => v.FullPage = true)`). Before the capture it does the following:

* stops animations, transitions and the caret
* waits for fonts and images
* waits for `WaitForSelector` and any `Delay` you configured
* masks the selectors listed in `Masks` with solid magenta

**Naming.** A snapshot is stored as `__snapshots__/{story-id}/{viewport}.{theme}.{browser}-{platform}[.initial].png`. The name has no render mode, so Server, WebAssembly, Auto and SSR must all match the same image. The platform is part of the name because font rasterisation differs between operating systems; to share baselines across operating systems, run your browsers in a container.

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
| axe-core (bundled) | Every browser cell | Everything axe checks, including color contrast, target size, scrollable regions and landmark rules |

**Policy** (`btp.json` → `accessibility`, or `AccessibilityPolicy` in code):

* `failOn`: the minimum impact that fails the build. Default `serious`.
* `tags`: the axe tags to run. Default WCAG 2.0/2.1/2.2 A and AA, plus best practices, which are reported but not gating unless they are serious.
* `disabledRules`: rules turned off globally.
* `suppressions`: entries of the form `{ ruleId, storyPattern, targetContains, justification, expires }`. A suppression stops applying after its expiry date, so accepted debt resurfaces automatically.

A story can disable a rule only with a written justification: `DisableAccessibilityRule("color-contrast", "Approved by design, #123")`. Justifications appear in the workbench and in reports.

### Manual review workflow

Automated tools catch only part of WCAG issues. The rest need a person: keyboard traps, meaningful focus order, content on hover, reflow, screen-reader announcements and similar checks.

1. `AccessibilityReviews.VerifyAsync(catalog)` in a test renders every story of each component and computes an **accessibility fingerprint**. The fingerprint covers roles, ARIA attribute names, focusability, input types and whether each element has a name. It ignores text and styling.
2. Components without a review get a template under `TestResults/btp/a11y-review-templates/`. Run `btp a11y init` to copy the templates into `a11y-reviews/`.
3. A reviewer fills in the WCAG 2.2 checklist (20 checks with guidance), plus `reviewer`, `reviewedAt` and `assistiveTechnology`, and commits the file.
4. The gate reports each component as `Approved`, `Missing`, `Incomplete`, `Failed` or **`Stale`**. A review is stale when the fingerprint changed since it was done, so a refactor that removes a label invalidates its review.
5. The workbench shows each component's review next to its stories. `btp a11y status` prints a summary.

Choose what fails with `ManualReviewGateOptions`. `RequireReview = false` lets teams adopt the gate gradually.

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
* Baselines (`__baselines__/performance.json`) catch drift. Render counts are compared exactly. Allocations get +10% and 4 KiB of slack. Timing gets +50% and 5 ms, and only warns by default because shared CI agents are noisy.
* Accept intended changes with `BTP_UPDATE_PERF=1` or `btp perf approve`.
* Timing is the noisy metric: for stable durations, run performance stories in a non-parallel test collection.
