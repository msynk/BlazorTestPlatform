# Configuration

Settings are resolved in this order: environment variables, then `btp.json` in the test project directory, then defaults. Relative paths are resolved against the project directory, which is the first parent of the test binaries that contains a `.csproj`. `BTP_PROJECT_DIR` overrides it.

## Environment variables

| Variable | Values | Default |
|---|---|---|
| `BTP_UPDATE_SNAPSHOTS` | `none`, `missing`, `changed`, `all` | `missing` locally, `none` on CI |
| `BTP_UPDATE_PERF` | `1` to record performance baselines | off |
| `BTP_RETRIES` | integer | 2 on CI, 0 locally |
| `BTP_DIAGNOSTICS` | `off`, `on-failure`, `always` | `on-failure` |
| `BTP_TIMEOUT_MS` | auto-wait timeout | 5000 |
| `BTP_SNAPSHOT_DIR` / `BTP_RESULTS_DIR` / `BTP_PERF_BASELINE` / `BTP_REVIEWS_DIR` | paths | `__snapshots__`, `TestResults/btp`, `__baselines__/performance.json`, `a11y-reviews` |
| `BTP_HEADED` | `1` to show the browser | headless |
| `BTP_INSTALL_BROWSERS` | `1` to install missing Playwright browsers automatically | off |
| `BTP_BASE_URL` | address for `ExternalWorkbenchHost` (the sample's fixture switches to it automatically, e.g. to test a trimmed publish) | — |
| `BTP_BUNIT_VIEWPORT` | viewport bUnit evaluates `@media` queries against: a preset (`mobile`, `tablet`, `desktop`...) or `WIDTHxHEIGHT` | `desktop` |
| `BTP_PLAYWRIGHT_IMAGE` | container image for `scripts/visual-tests.*` | derived from the `Microsoft.Playwright` version |

## `btp.json`

```json
{
  "snapshotDirectory": "__snapshots__",
  "resultsDirectory": "TestResults/btp",
  "performanceBaselinePath": "__baselines__/performance.json",
  "reviewsDirectory": "a11y-reviews",
  "updateSnapshots": "missing",
  "retries": 0,
  "diagnostics": "retainOnFailure",
  "timeoutMilliseconds": 5000,
  "visual": { "threshold": 0.1, "maxDiffRatio": 0 },
  "accessibility": {
    "failOn": "serious",
    "tags": ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"],
    "disabledRules": [],
    "suppressions": [
      { "ruleId": "color-contrast", "storyPattern": "legacy-*", "justification": "Rebrand in Q3 (#481)", "expires": "2026-12-31" }
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
    "failOnDurationRegression": false
  }
}
```

## What to commit

| Path | Commit? |
|---|---|
| `__snapshots__/**/*.png` | yes (visual baselines) |
| `__snapshots__/baselines.json` | yes (which Playwright and browser build produced them) |
| `__baselines__/performance.json` | yes |
| `a11y-reviews/*.a11y-review.json` | yes |
| `btp.json` | yes |
| `TestResults/` | no (results, artifacts and the report) |
