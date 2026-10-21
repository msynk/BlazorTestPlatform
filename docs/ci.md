# Diagnostics, reports and CI

## What every run records

Each story run writes one JSON file to `TestResults/btp/results/`. Files are independent, so parallel tests never contend for them. A result contains:

* the outcome, attempts and errors of earlier attempts
* the step timeline
* visual, accessibility and performance results
* browser runs only: console messages, page errors, network requests (with status and timing) and WebSocket activity
* artifacts for failed runs (or for every run with `BTP_DIAGNOSTICS=always`): a full-page screenshot, a DOM snapshot and a **Playwright trace** (open it with `playwright.ps1 show-trace trace.zip`)

## Flakiness

* `BTP_RETRIES` (default 2 on CI, 0 locally) retries failed browser runs in a fresh context. A run that passes after a failure is reported as **flaky**, not as passed, and keeps the errors of its failed attempts.
* `btp report --history .btp/history.json` keeps a rolling window of 50 runs. Persist the file between CI runs, for example with `actions/cache`. The report ranks stories by flake rate; stories that fail every run are treated as broken, not flaky.
* `btp flaky --history .btp/history.json` prints the ranking in the terminal.

## The `btp` CLI

```
btp report   [-p <project>] [-r <results>...] [-o <dir>] [--junit <file>] [--markdown <file>]
             [--github-summary] [--history <file>] [--run-id <id>] [--fail-on-failures]
btp approve  [-r <results>...] [--filter <glob>] [--dry-run]
btp perf approve [-r <results>...] [--filter <glob>] [--baseline <file>]
btp a11y init [--force]   |   btp a11y status
btp flaky --history <file>
btp clean
```

To build one report for several test projects, repeat `-r`.

## GitHub Actions

A typical job (this repository's own [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) does the same, running the CLI from source):

```yaml
- run: dotnet tool install -g BlazorTestPlatform.Cli
- run: dotnet build -c Release
- run: pwsh samples/Contoso.UI.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
- run: dotnet test -c Release --no-build
- if: always()
  run: btp report -p samples/Contoso.UI.Tests --junit junit.xml --github-summary --history .btp/history.json
- if: always()
  uses: actions/upload-artifact@v4
  with:
    name: btp-report
    path: samples/Contoso.UI.Tests/TestResults/btp
```

On CI (`CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`... detected automatically), missing baselines fail the build, retries are enabled, and diagnostics are kept for failures.

## Making snapshots stable across machines

Font rendering differs between Windows, macOS and Linux, so the platform is part of the snapshot name (`…chromium-linux.png`). CI runs in the Playwright image that matches the pinned `Microsoft.Playwright` package (`mcr.microsoft.com/playwright/dotnet:v{version}-noble`). The workflow and the scripts read the version from `Directory.Packages.props`, so the tag never has to be kept in sync by hand. To produce or check the same Linux baselines on any machine with Docker:

```bash
scripts/visual-tests.sh              # compare with the committed chromium-linux baselines
scripts/visual-tests.sh --update     # write missing baselines, then review and commit them
pwsh scripts/visual-tests.ps1 -Update   # Windows
```

The script mounts the repository, uses `--artifacts-path` so container builds never touch your local `bin/` and `obj/`, and caches the .NET SDK and NuGet packages in Docker volumes. `--print-image` shows the tag it uses, and `BTP_PLAYWRIGHT_IMAGE` overrides it.

### Upgrading Playwright

A Playwright upgrade ships new browser builds, and those render text and anti-aliasing slightly differently, so some baselines legitimately change. The platform makes this a routine step rather than a hunt:

1. `__snapshots__/baselines.json` records which renderer produced the baselines of each `{browser}-{platform}` (Playwright and browser versions). It's written when baselines are created or revalidated with `BTP_UPDATE_SNAPSHOTS=changed|all`, and it's never written in CI.
2. After the version bump (by hand, or the Dependabot PR that `.github/dependabot.yml` opens for `Microsoft.Playwright`), CI fails fast in `BaselineProvenanceTests`, which says the baselines come from the previous version. Visual mismatches carry the same explanation in the failure message and the HTML report, so they don't look like regressions.
3. Run the **Regenerate visual baselines** workflow (`.github/workflows/baselines.yml`) on the branch. It renders every story in the matching image, rewrites only the baselines that changed (`changed`, the default), records the new renderer and pushes one commit. Review the image diffs in the PR. Locally, `scripts/visual-tests.sh --update=changed` does the same.
4. Commits pushed with the workflow's `GITHUB_TOKEN` don't trigger CI. Re-run the PR checks, or push any commit, to get a green build on the regenerated baselines.

Windows and macOS baselines are for local development only. Regenerate them with `BTP_UPDATE_SNAPSHOTS=changed dotnet test` on that machine.
