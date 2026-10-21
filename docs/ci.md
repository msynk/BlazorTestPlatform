# Diagnostics, reports and CI

## What every run records

Each story run writes one JSON file to `TestResults/btp/results/`. Files are independent, so parallel tests never contend for them. A result contains:

* the outcome, attempts and errors of earlier attempts
* the step timeline (the last steps also end every failure message)
* visual, accessibility and performance results, including the accessibility rules the story is excused from and why
* browser runs only: console messages, page errors, network requests (with status and timing) and WebSocket activity
* artifacts for failed runs (or for every run with `BTP_DIAGNOSTICS=always`): a full-page screenshot, a DOM snapshot and a **Playwright trace** (open it with `playwright.ps1 show-trace trace.zip`). A flaky run keeps the artifacts of its failed attempts too.

Tests can add run-wide files next to the results: the manual accessibility review gate writes `a11y-review-gate.json`, and a story coverage test writes `story-coverage.json` (see [quality gates](quality-gates.md#story-state-coverage)). `btp report` and `btp a11y status` pick them up.

## Flakiness

* `BTP_RETRIES` (default 2 on CI, 0 locally) retries failed browser runs in a fresh context. A run that passes after a failure is reported as **flaky**, not as passed, and keeps the errors and artifacts of its failed attempts.
* `btp report --history .btp/history.json` keeps a rolling window of 50 runs. Persist the file between CI runs, for example with `actions/cache`, and save it even when tests fail (failed runs are what the history needs). The report ranks result keys (story × browser × viewport × theme × render mode) that have at least two recorded runs by flake rate; keys that fail every run are treated as broken, not flaky.
* `btp flaky --history .btp/history.json` prints the ranking in the terminal.

## The `btp` CLI

Install it with `dotnet tool install -g BlazorTestPlatform.Cli --prerelease` while the packages are in preview.

```
btp report   [-r <results>...] [-o <dir>] [--junit <file>] [--markdown <file>] [--github-summary]
             [--history <file>] [--run-id <id>] [--fail-on-failures]
             [--pr-comment] [--pr <number>] [--repository <owner/name>] [--report-url <url>]
btp approve  [-r <results>...] [-f|--filter <glob>] [--dry-run]
btp perf approve [-r <results>...] [-f|--filter <glob>] [--baseline <file>]
btp a11y init [-r <results>...] [--force]   |   btp a11y status [-r <results>...]
btp flaky --history <file>
btp clean
```

Every command takes `-p <project>`, the test project directory with `btp.json`. It defaults to `BTP_PROJECT_DIR`, else the current directory, so run `btp` from the test project or pass `-p`. To build one report for several test projects, repeat `-r`.

## Pull-request comments

`btp report --pr-comment` posts the Markdown summary (outcomes, failures, flaky runs, the flakiest stories and story state coverage) on the pull request, and updates that same comment on every later run instead of adding a new one. It finds the comment by a hidden marker, so other comments are never touched.

On GitHub Actions it reads the repository, the pull request number and the API address from the workflow's environment. It needs a token that can write pull-request comments, passed as `GITHUB_TOKEN`. Pull requests from forks get a read-only token: the command then prints a warning and the build continues. Outside Actions, pass `--pr` and `--repository`. The comment links to the workflow run, where the HTML report is attached; pass `--report-url` to link a published report instead.

## GitHub Actions

A typical job (this repository's own [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) does the same in the Playwright container, and runs the CLI from source):

```yaml
permissions:
  contents: read
  pull-requests: write          # for --pr-comment

steps:
  - uses: actions/checkout@v4
  - run: dotnet tool install -g BlazorTestPlatform.Cli --prerelease
  - run: dotnet build -c Release
  - run: pwsh tests/MyDesignSystem.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
  - uses: actions/cache/restore@v4
    with:
      path: .btp/history.json
      key: btp-history-${{ github.run_id }}
      restore-keys: btp-history-
  - run: dotnet test -c Release --no-build
  - if: always()
    env:
      GITHUB_TOKEN: ${{ github.token }}
    run: btp report -p tests/MyDesignSystem.Tests --junit junit.xml --github-summary --history .btp/history.json --pr-comment
  - if: always()
    uses: actions/cache/save@v4
    with:
      path: .btp/history.json
      key: btp-history-${{ github.run_id }}
  - if: always()
    uses: actions/upload-artifact@v4
    with:
      name: btp-report
      path: tests/MyDesignSystem.Tests/TestResults/btp
```

CI is detected automatically (`CI` set to `1`, `true` or `yes`, or any of `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`, `BUILDKITE` or `TEAMCITY_VERSION`). On CI, missing visual baselines fail the build instead of being written, browser runs are retried, and missing performance baselines are not recorded. Diagnostics are kept for failures everywhere.

## Making snapshots stable across machines

Font rendering differs between Windows, macOS and Linux, so the platform is part of the snapshot name (`…chromium-linux.png`). CI runs in the Playwright image that matches the pinned `Microsoft.Playwright` package (`mcr.microsoft.com/playwright/dotnet:v{version}-noble`). The workflow and the scripts read the version from `Directory.Packages.props`, so the tag never has to be kept in sync by hand. The image contains all three browsers, so CI gates on `chromium-linux`, `firefox-linux` and `webkit-linux` baselines. GitHub Actions container jobs run as root with `HOME=/github/home`, a folder owned by another user, and Firefox refuses to start there: set `HOME: /root` on the steps that run browser tests. To produce or check the same Linux baselines on any machine with Docker:

```bash
scripts/visual-tests.sh              # compare with the committed Linux baselines
scripts/visual-tests.sh --update     # write missing baselines, then review and commit them
pwsh scripts/visual-tests.ps1 -Update   # Windows
```

The script mounts the repository, uses `--artifacts-path` so container builds never touch your local `bin/` and `obj/`, and caches the .NET SDK and NuGet packages in Docker volumes (the SDK is reinstalled when `global.json` needs a newer one). `--print-image` (`-PrintImage` in PowerShell) shows the tag it uses, and `BTP_PLAYWRIGHT_IMAGE` overrides it.

### Upgrading Playwright

A Playwright upgrade ships new browser builds, and those render text and anti-aliasing slightly differently, so some baselines legitimately change. The platform makes this a routine step rather than a hunt:

1. `__snapshots__/baselines.json` records which renderer produced the baselines of each `{browser}-{platform}` (Playwright and browser versions). It's written when the first baselines of a `{browser}-{platform}` are created, when baselines are revalidated with `BTP_UPDATE_SNAPSHOTS=changed|all`, and when `btp approve` accepts changed screenshots. It's never written when the update mode is `none`, the default on CI.
2. After the version bump (by hand, or the Dependabot PR that `.github/dependabot.yml` opens for `Microsoft.Playwright`), CI fails in its first test step, `BaselineProvenanceTests`, which says the baselines come from the previous version. Visual mismatches carry the same explanation in the failure message and the HTML report, so they don't look like regressions.
3. Run the **Regenerate visual baselines** workflow (`.github/workflows/baselines.yml`) on the branch. It renders every story in the matching image, rewrites only the baselines that changed (`changed`, the default), records the new renderer and pushes one commit. Review the image diffs in the PR. Locally, `scripts/visual-tests.sh --update=changed` does the same.
4. Commits pushed with the workflow's `GITHUB_TOKEN` don't trigger CI. Re-run the PR checks, or push any commit, to get a green build on the regenerated baselines.

Windows and macOS baselines are for local development only, so they aren't committed: this repository's `.gitignore` keeps only the `*-linux` images under `__snapshots__/` (see [What to commit](configuration.md#what-to-commit)). The first local run writes them; regenerate them with `BTP_UPDATE_SNAPSHOTS=changed dotnet test` on that machine.
