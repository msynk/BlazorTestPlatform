# Configuration

Settings are resolved in this order: environment variables, then `btp.json` in the test project directory, then defaults. Relative paths are resolved against the project directory, which is the first parent of the test binaries that contains a `.csproj`. `BTP_PROJECT_DIR` overrides it. The `btp` CLI uses `-p`, else `BTP_PROJECT_DIR`, else the current directory.

## Environment variables

| Variable | Values | Default |
|---|---|---|
| `BTP_UPDATE_SNAPSHOTS` | `none`, `missing`, `changed`, `all` (also `0`/`false` for none, `failed` for changed, `1`/`true` for all) | `missing` locally, `none` on CI |
| `BTP_UPDATE_PERF` | `1`, `true` or `yes` to record performance baselines | off |
| `BTP_RETRIES` | integer | 2 on CI, 0 locally |
| `BTP_DIAGNOSTICS` | `off`, `on-failure`, `always` (also `0`/`false`, `retain-on-failure`/`failure`, `on`/`1`) | `on-failure` |
| `BTP_TIMEOUT_MS` | auto-wait timeout of assertions and actions in bUnit and Playwright runs | 5000 |
| `BTP_SNAPSHOT_DIR` / `BTP_RESULTS_DIR` / `BTP_PERF_BASELINE` / `BTP_REVIEWS_DIR` | paths | `__snapshots__`, `TestResults/btp`, `__baselines__/performance.json`, `a11y-reviews` |
| `BTP_HEADED` | `1`, `true` or `yes` to show the browser | headless |
| `BTP_INSTALL_BROWSERS` | `1`, `true` or `yes` to install missing Playwright browsers automatically | off |
| `BTP_BASE_URL` | address for `ExternalWorkbenchHost` (the sample's fixture switches to it automatically, e.g. to test a trimmed publish) | — |
| `BTP_BUNIT_VIEWPORT` | viewport bUnit evaluates `@media` queries against: a preset (`mobile`, `tablet`, `desktop`...) or `WIDTHxHEIGHT` | `desktop` |
| `BTP_PLAYWRIGHT_IMAGE` | container image for `scripts/visual-tests.*` | derived from the `Microsoft.Playwright` version |
| `BTP_PROJECT_DIR` | the test project directory (see above) | detected |

## `btp.json`

```json
{
  "snapshotDirectory": "__snapshots__",
  "resultsDirectory": "TestResults/btp",
  "performanceBaselinePath": "__baselines__/performance.json",
  "reviewsDirectory": "a11y-reviews",
  "diagnostics": "retainOnFailure",
  "timeoutMilliseconds": 5000,
  "visual": { "threshold": 0.1, "maxDiffRatio": 0 },
  "accessibility": {
    "failOn": "serious",
    "tags": ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"],
    "includeBestPractices": true,
    "disabledRules": [],
    "suppressions": [
      { "ruleId": "color-contrast", "storyPattern": "legacy-*", "targetContains": ".brand", "justification": "Rebrand in Q3 (#481)", "expires": "2026-12-31" }
    ]
  },
  "bunit": {
    "stylesheets": ["../MyDesignSystem/wwwroot/app.css"],
    "viewport": "desktop"
  },
  "performance": {
    "renderCountRatio": 0,
    "allocationRatio": 0.1,
    "allocationSlackBytes": 4096,
    "durationRatio": 0.5,
    "durationSlackMilliseconds": 5,
    "failOnRenderRegression": true,
    "failOnAllocationRegression": true,
    "failOnDurationRegression": false
  }
}
```

Every key is optional. Two more exist, `updateSnapshots` (`none`, `missing`, `changed`, `all`) and `retries`, but leave them out unless you mean it: a value in `btp.json` replaces the CI-dependent default, so `"updateSnapshots": "missing"` would let CI write missing baselines and `"retries": 0` would turn off retries on CI. Use the environment variables for one-off changes instead.

In `btp.json`, `diagnostics` takes the enum names `off`, `retainOnFailure` and `always`; the `on-failure` spelling is for `BTP_DIAGNOSTICS` only.

## What to commit

| Path | Commit? |
|---|---|
| `__snapshots__/**/*-{platform}*.png` for the platform CI renders on (`*-linux.png`, `*-linux.initial.png` in the Playwright image) | yes (the visual baselines CI gates on) |
| `__snapshots__/**/*.png` for other platforms (`*-win32.png`, `*-darwin.png`) | no (written by local runs, and font rendering makes them machine-specific) |
| `__snapshots__/baselines.json` | yes (which Playwright and browser build produced each `{browser}-{platform}`; keep the entries local runs add, so the file doesn't change on every fresh clone) |
| `__baselines__/performance.json` | yes |
| `a11y-reviews/*.a11y-review.json` | yes |
| `btp.json` | yes |
| `TestResults/` | no (results, artifacts and the report) |
| `.btp/` | no (local flake history; CI keeps it in a cache) |

When CI renders in the Linux Playwright image, these `.gitignore` lines keep everything else out of the repository:

```gitignore
TestResults/
.btp/
**/__snapshots__/**/*.png
!**/__snapshots__/**/*-linux.png
!**/__snapshots__/**/*-linux.*.png
```
