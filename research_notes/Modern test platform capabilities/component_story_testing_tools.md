# Component- and Story-Driven UI Testing Tools: End-to-End Experience (2025-2026)

Research date: 2026-11-10. Source-access note: storybook.js.org returned TLS errors to the fetch tool, so Storybook documentation was read from the canonical docs source files in the `storybookjs/storybook` GitHub repo (`next` branch, `docs/` folder). Those files render as the pages at storybook.js.org/docs/... Storybook blog and release pages, Chromatic's review shortcuts and TurboSnap, Ladle, and testing-library.com could not be fetched directly. Facts about them come from search-result summaries, and each one is marked "(search summary)". Treat those as less verified than facts from fetched pages.

## 1. What each tool shows the developer while authoring and running tests (status per story, sidebar badges, live re-run, filters, debugging, time travel, DOM snapshots)

### Takeaway
Storybook 9+ is now a test platform as well as a workshop. A **testing widget** sits at the bottom of the sidebar, with "Run tests", per-test-type checkboxes (component/interaction, accessibility, visual, coverage) and an eye-icon watch mode. Each story in the sidebar shows a pass/fail/error status, and clicking a failure count filters the tree. In 10.4 the sidebar also gained git-based "new / modified / related" filters. Playwright UI mode and Trace Viewer set the bar for debugging, with a timeline, before/after DOM snapshots, pick-locator, and console/network/attachments tabs. Cypress offers similar time travel through its Command Log. Playwright replaced its experimental CT packages with a "story gallery page" model that is conceptually close to BlazorTestPlatform.

### Cited Findings

**Storybook (9/10): testing widget and sidebar status**
- A testing widget appears at the bottom of the sidebar after setup. It holds the watch-mode, coverage and accessibility toggles and shows a coverage summary. "Test status indicators on sidebar items" show each story's result after a run. — [Storybook docs: Writing tests (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx)
- The widget's **"Run tests"** button runs all tests. The expanded widget can run specific test types, and the component test sub-types run together. A **three-dot menu** on hover over a sidebar item runs tests for one story or a group (component or folder). — [Storybook docs: Vitest addon (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- Status indicators on stories and components show pass, fail or error. Selecting a result from a story's menu navigates to that story and opens the relevant panel, for example the Interactions panel's step-through debugger. — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- The widget shows the totals run, passed and failed/errored. **Clicking the failure count filters the sidebar to failing stories**, and the sidebar can also be filtered to show only failing stories. Vitest errors appear in the widget with a link to the full error and are also logged to the console. — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- Accessibility results get their own warning and failure counts in the widget. Pressing a count filters the sidebar to matching stories. — [Storybook a11y testing docs (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- **Watch mode** is the eye icon in the testing widget. It re-runs only the relevant tests when component source or tests change. — [Writing tests](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx)
- Test types:
  - **Render (smoke) tests**: a story passes if it renders and fails if it errors.
  - **Interaction tests**: a `play` function.
  - **Visual tests**: snapshots against baselines through Chromatic.
  - **Snapshot tests**: markup compared to a baseline.
  — [Writing tests](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx)
- How stories become tests: a Vitest plugin, `storybookTest`, transforms stories into tests using "portable stories". Every story gets a smoke test, and if it has a play function, that runs too. By default tests run in Vitest **browser mode with Playwright Chromium**, which the docs recommend over JSDom/HappyDOM. It requires a Vite-based framework and Vitest 3.0 or later. — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- Tag-based test selection uses `include` (default `['test']`), `exclude` (not tested and not counted) and `skip` (not tested but counted in results). If a tag is in both `include` and `exclude`, `exclude` wins. — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- Linking failures back to stories:
  - `storybookUrl` (default `http://localhost:6006`, or a published Storybook in CI) makes CLI failure output link to the story.
  - `storybookScript` auto-starts Storybook.
  - IDE integration, such as the VS Code Vitest extension, shows story test status, and failures link to the story.
  - `initialGlobals` pins globals such as a theme per Vitest project, so the same stories can be tested under several themes.
  — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- Compared with the older **test-runner**, the Vitest addon needs no running Storybook and supports visual tests. It has no snapshot tests and requires Vite. Webpack projects must migrate to use it. — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)

**Storybook: Interactions panel (debugging)**
- Play functions use `canvas` (Testing Library queries `getBy`/`queryBy`/`findBy`, with `ByRole`, `ByLabelText`, `ByText`, and `ByTestId` described as "a last resort"), `userEvent` (click, type, hover, tab, selectOptions…), `expect` (Vitest plus jest-dom matchers) and `fn` spies, all imported from `storybook/test`. `userEvent` calls must be awaited to appear in the Interactions panel. — [Storybook interaction testing docs (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/interaction-testing.mdx)
- The Interactions panel shows the step-by-step flow of the play function. It has controls to "pause, resume, rewind, and step through each interaction" and shows failures at the exact point they occurred. `step()` groups interactions under a custom label in a collapsible group. — [Interaction testing docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/interaction-testing.mdx)
- Lifecycle hooks:
  - `mount` (run code before rendering, for example mocking `Date`)
  - meta- and project-level `beforeEach`, which can return a cleanup function
  - `beforeAll`
  - `afterEach` for final assertions
  — [Interaction testing docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/interaction-testing.mdx)

**Storybook 10.4: change detection in the sidebar**
- Storybook 10.4 (May 2026) added sidebar filters that surface stories that are **new, modified, or related** (possibly affected) based on git changes, "so reviewers can focus on components most likely to need attention." — [AlternativeTo: Storybook 10.4](https://alternativeto.net/news/2026/5/storybook-10-4-brings-ai-auto-setup-change-detection-quick-sharing-and-tanstack-react/)
- The release notes list "Core: Implement Git change detection (#34420)" and deferred change-detection initialization for startup performance (#34498). Status filters persist in the URL. The Storybook team calls this a first step, with "a higher-signal diff and a better review UI" in progress. (search summary) — [Storybook 10.4 blog](https://storybook.js.org/blog/storybook-10-4/); [GitHub release v10.4.0](https://newreleases.io/project/github/storybookjs/storybook/release/v10.4.0)

**Playwright UI mode** (all items from [Playwright UI mode docs](https://playwright.dev/docs/test-ui-mode))
- A sidebar lists test files and describe blocks, each with a run triangle. Filters cover name, project, `@tag` and status (passed/failed/skipped).
- Watch mode uses an eye icon per test, or at the top for all tests.
- A colored **timeline** marks navigations and actions. Hovering shows a snapshot, and a slider narrows the window, which filters the Actions, Console and Network tabs.
- **Actions tab**: locator and duration per action, with **"Before"/"After"** DOM snapshots. A snapshot can be popped out into its own window for DevTools inspection.
- **Pick locator**: hover over the snapshot to see locators, then click to open a locator playground and copy.
- Other tabs:
  - **Source**: highlights the current line, with "Open in VSCode".
  - **Call**: timing, locator, strict mode.
  - **Log**: internal waits.
  - **Errors**: red line on the timeline.
  - **Console**: logs from the browser and the test.
  - **Network**: sortable, with headers and bodies.
  - **Attachments**: visual diff, actual and expected with a slider overlay.
  - **Metadata**: browser, viewport, duration.
- It can run as a web page with `--ui-host=0.0.0.0` (Docker/Codespaces), with a warning that this exposes traces and secrets.

**Playwright Trace Viewer** (all items from [Playwright Trace Viewer docs](https://playwright.dev/docs/trace-viewer))
- Captures **Before / Action / After** DOM snapshots. The Action snapshot highlights the target node and the exact click position.
- A screenshot **film strip** with magnified hover views. The timeline slider filters the Actions, Console and Network tabs.
- Has the same Source, Call, Log, Errors, Console, Network, Metadata and Attachments tabs as UI mode. Attachments show the diff, actual and expected images, with a slider.
- Opening traces:
  - `npx playwright show-trace trace.zip`
  - From the HTML report's trace icon
  - **trace.playwright.dev**, a static viewer that "loads the trace entirely in your browser and does not transmit any data externally". It takes a trace URL as a query parameter.
- Recording modes: `on-first-retry` (recommended for CI), `on-all-retries`, `retain-on-failure`, `on` ("performance heavy") and `off`.

**Playwright component testing (new gallery model)**
- The experimental `@playwright/experimental-ct-react`, `-ct-react17` and `-ct-vue` packages "have been removed and are no longer published". Users of them should "stay on Playwright 1.62" until they migrate. — [Playwright component testing docs](https://playwright.dev/docs/test-components)
- In the replacement, component tests are regular Playwright tests that run "against a small story gallery page served by your own dev server", using the built-in `mount` fixture of `@playwright/test`.
  - Stories live in `*.story.tsx|ts|jsx|js|vue` files, and each named export is a story.
  - Ids come from the file path plus the export name (for example `components/Button/Primary`), and any unique suffix such as `Button/Primary` also works.
  - The gallery exposes `window.mount({ story, props })` and `window.unmount()`.
  - `mount()` returns a Locator for the root. `component.update(props)` reconciles without remounting.
  - The gallery is "framework-specific and yours to own".
  — [Playwright component testing docs](https://playwright.dev/docs/test-components)
- Visual comparisons use `toHaveScreenshot` on the returned root locator, for example `expect(await mount('Button/Primary')).toHaveScreenshot('primary.png')`. Props must be serializable, and callbacks belong inside the story. — [Playwright component testing docs](https://playwright.dev/docs/test-components)

**Cypress app (component testing)**
- **Command Log**: lists commands and hooks. Hovering a command restores its DOM snapshot ("time travel"). Clicking pins the snapshot and shows event hitboxes. A snapshot menu toggles before and after. An "Instrument panel" summarizes routes, stubs and spies. Entries link to the source in the IDE and print `consoleProps` to DevTools. — [Cypress open mode docs](https://docs.cypress.io/app/core-concepts/open-mode)
- For component testing the preview renders the "Component Under Test" from `cy.mount()`. It is interactive and inspectable, and shows viewport size and scale. **Cypress Studio** records interactions into commands saved to the spec. Shortcuts are `r` rerun, `s` stop and `f` focus specs. — [Cypress open mode docs](https://docs.cypress.io/app/core-concepts/open-mode)
- Errors show the error name, a code-frame link, the code frame, a "View stack trace" toggle and a "Print to console" button. `cy.pause()` and `cy.debug()` are available. The Specs page has columns for "Last updated", "Latest runs", "Average duration" and "Flake annotation", the last three from Cypress Cloud. — [Cypress open mode docs](https://docs.cypress.io/app/core-concepts/open-mode)
- Supported stacks (page updated Nov 10, 2026):
  - React 18-19 with Vite 8 or Webpack 5
  - Next.js 15-16
  - Vue 3
  - Angular 21-22 with Webpack 5
  - Svelte 5 (Alpha)
  - Community: Qwik and Lit

  A setup wizard auto-detects the framework and bundler. The docs recommend `data-cy` selectors. — [Cypress CT get started](https://docs.cypress.io/app/component-testing/get-started)

**Histoire / Ladle**
- Histoire's guide lists stories grouped into **variants**, **variant grids** ("glance at all the possible buttons at once"), a controls pane with built-in and custom controls, responsive viewport presets, backgrounds with a checkerboard, a dark theme, and built-in **fuzzy search** over stories and docs. — [Histoire guide](https://histoire.dev/guide/)
- Ladle generates a `meta.json` that lists every story and its parameters, served at `/meta.json`. (search summary; ladle.dev was blocked for direct fetch) — [Ladle docs: meta](https://ladle.dev/docs/meta/)

### Inferences
- For BlazorTestPlatform's workbench, the established UX pattern is:
  - a persistent sidebar widget with "Run all" and per-type toggles (bUnit / Playwright per render mode / visual / a11y / perf)
  - aggregate counts that work as clickable filters
  - per-story status badges with a hover menu offering "run this story/component/folder" and "open result in panel"
  - an eye-icon watch mode
- Storybook's "result → jump to story + open the right panel" interaction lets users go from a CI or CLI failure straight to the story. The equivalent is `storybookUrl` links in failure messages. BlazorTestPlatform could emit workbench permalinks in bUnit and Playwright failure output.
- Playwright's new gallery/story model (named stories addressed by id, a dev-server-hosted gallery, `mount('Button/Primary')` returning a locator) closely matches the "stories as C# component states run in a real browser" concept. It is a useful comparison point for the showcase site.
- Git-based change detection (10.4) is a newer pattern for "review only what changed". It could be replicated with `git diff` plus a component dependency map.

### Gaps
- I could not fetch the Storybook 10.4 blog directly. The exact filter labels and UI for "modified/related" are unverified beyond secondary summaries.
- No source confirmed whether Storybook's sidebar has dedicated filter chips for "visual changes" or "a11y warnings" beyond clicking the counts in the widget.
- Cypress "Selector Playground" and live reload on save were not described on the fetched pages.
- Histoire's docs sidebar lists pages on "browser tests", an "MCP server", "inspector", "props matrix", "AI threads" and "ACP agents", but the fetched page did not describe them, and a search found nothing to confirm them. Treat them as unverified.

## 2. Visual review and approval workflows (baselines, branch-aware baselines, diff UI modes, accept/deny, comments, PR checks)

### Takeaway
Chromatic is the reference implementation, and it runs two separate flows:
- **UI Tests** (visual regression): per-story baselines tracked along git history, per-branch baselines, accept or deny per snapshot, and a "UI Tests" PR check.
- **UI Review**: a PR-style changeset review against the merge base, with threaded comments per snapshot, assigned and default reviewers, and a "UI Review" PR check.

Storybook's Visual Tests addon brings run, diff and accept into the local workbench, and accepted baselines sync to the cloud on push. Playwright offers a lighter local alternative: `toHaveScreenshot` with a diff/actual/expected slider.

### Cited Findings

**Storybook Visual Tests addon (Chromatic-backed)** (all items from [Storybook visual testing docs (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/visual-testing.mdx))
- Installed as `@chromatic-com/storybook`, it adds a **Visual Tests** panel. You sign in, pick a project, and press "Catch a UI change" for the first build, which creates the baselines.
- Run from the "Visual tests" section of the testing widget, or from the run button at the top right of the panel. With the Vitest addon, "Run tests" runs both component and visual tests.
- Stories with visual changes are highlighted 🟡 in the sidebar. The panel shows which pixels changed.
- You accept intentional changes locally with ✅, or fix the story and press rerun. Pushing syncs accepted baselines to the cloud for anyone on that branch, and baselines accepted in the addon are auto-accepted in CI.
- A "UI Tests" check appears on PRs, and making it required blocks unreviewed UI changes. Config lives in `chromatic.config.json` (`projectId`, `buildScriptName`, `debug`, `zip`).

**Chromatic baselines (branch-aware)** (all items from [Chromatic: Branching and baselines](https://www.chromatic.com/docs/branching-and-baselines/))
- A **baseline** is the last accepted snapshot of a story in a given mode. Baselines are tracked along git history, not stored in the repo, and change only when someone accepts a change.
- Each branch keeps its own baselines and inherits them from the commit it branched from. Changes accepted on `main` don't automatically update feature branches.
- How a baseline is chosen:
  - For each build, Chromatic finds the nearest ancestor commit that has a build.
  - If the ancestor's snapshot was unchanged, or changed and accepted, it becomes the baseline.
  - If it was denied or pending, Chromatic falls back to that snapshot's own baseline.
  - With multiple ancestors, the most recently accepted snapshot wins.
- Accepting a baseline covers all browsers.
- Handling of rewritten history:
  - Merges: `preferMergedBaselines` (account-level, enabled by support) prefers incoming baselines.
  - Rebases: use `--ignore-last-build-on-branch`.
  - Squash and rebase merges are detected through git provider APIs.
  - On `main`, `--auto-accept-changes` or `autoAcceptChanges: "main"` auto-accepts.
- Individual builds can't be deleted. A bad acceptance on the most recent build can be reverted in the dashboard. Older baselines are restored by branching from a past build's commit and merging with auto-accept.

**Chromatic review actions, TurboSnap**
- Reviewers step through each snapshot diff and accept it (which updates the baseline) or deny it (which marks a regression and fails the build). A denied change is always re-captured on the next build, even if TurboSnap would skip it. Later builds compare against the original baseline, not the denied snapshot. Keyboard shortcuts: `a` accept, `d` deny, `3` toggle diff. (search summary of official docs) — [Chromatic Quickstart](https://docs.chromatic.com/docs/quickstart/); [Chromatic TurboSnap docs](https://www.staging-chromatic.com/docs/turbosnap/)
- **TurboSnap** uses git history and the bundler's dependency graph to find the affected stories. Unaffected stories have their snapshots "copied" from an ancestor build. (search summary) — [Chromatic TurboSnap docs](https://www.staging-chromatic.com/docs/turbosnap/)
- Tests run across chosen combinations of viewports and browsers (Chrome, Firefox, Safari, Edge). Viewports are configurable globally or per test. Tests that "still render inconsistently are flagged as unstable and won't block your build." — [Chromatic: Visual tests overview](https://www.chromatic.com/docs/test/)

**Chromatic UI Review (PR-style review)**
- UI Review **doesn't use baselines**. It compares the head branch's latest snapshots with the merge-base build on the base branch, so it needs builds on the base branch. `--patch-build` creates a missing merge-base build. — [Chromatic: Branching and baselines](https://www.chromatic.com/docs/branching-and-baselines/)
- The **Changeset tab** shows "a side-by-side view of all visual changes" between head and base. Reviewers can "toggle the highlighted diff (in neon green)". There is also an **Activity tab**, and "Reviews" appears in the sidebar. — [Chromatic: UI Review](https://www.chromatic.com/docs/review/)
- **Comments**: a comment box sits under each change, and threaded discussions attach to the specific snapshot. They are closed with "Resolve". — [Chromatic: UI Review](https://www.chromatic.com/docs/review/)
- **Reviewers**:
  - assigned with "Assign Reviewers"
  - default reviewers set per project, all of whom must approve
  - component owners auto-assigned by the components or stories that changed
  - approval through the "Approve" button
  — [Chromatic: UI Review](https://www.chromatic.com/docs/review/)
- **PR check**: a "UI Review" status check is "🟡 Pending" when changes are found and "🟢 Passed" when they are approved and the UI Checklist is complete. It can be made required in GitHub, GitLab or Bitbucket. — [Chromatic: UI Review](https://www.chromatic.com/docs/review/)

**Other tools**
- Playwright UI mode and Trace Viewer: the Attachments tab compares screenshots as diff, actual and expected, with a slider. — [Playwright UI mode](https://playwright.dev/docs/test-ui-mode); [Trace Viewer](https://playwright.dev/docs/trace-viewer)
- Histoire supports visual regression snapshots in CI through **Percy** and **Lost Pixel** integrations. — [Histoire guide](https://histoire.dev/guide/)
- Ladle's visual snapshot recipe: read `meta.json`, generate one Playwright test per story, open each story in preview mode, wait for load and compare a screenshot. `meta.skip` excludes stories, and custom meta parameters (for example a viewport) can drive the test. (search summary) — [Ladle blog: visual snapshots](https://ladle.dev/blog/visual-snapshots); [Ladle docs: meta](https://ladle.dev/docs/meta/)

### Inferences
- A credible demo for BlazorTestPlatform's visual snapshots should show:
  - a baseline per story × mode (render mode, theme, viewport)
  - an accept/deny action per changed snapshot, with keyboard shortcuts
  - a diff viewer offering at least a highlight-overlay toggle, side-by-side, and a slider (Playwright-style)
  - a sidebar badge for "visual change pending"
- Branch-aware baselines are a large part of Chromatic's value. A self-hosted .NET equivalent could store baselines keyed by commit or branch, for example in git LFS or an artifact store, and auto-accept on main.
- Separating "regression test approval" (UI Tests) from "PR design review with comments" (UI Review) is a reusable product concept.

### Gaps
- I could not verify from a fetched page whether Chromatic's current diff viewer offers "strobe" or "spotlight/focus" modes, or batch accept. The fetched UI Review page mentions only the neon-green highlight toggle and side-by-side.
- No pricing or snapshot-billing facts were verified. One third-party guide claims a TurboSnap snapshot bills at 0.2 of a snapshot, which is unconfirmed.

## 3. How accessibility results are surfaced per story (violations panel, highlighting, rule docs, todo/ignore modes, trends)

### Takeaway
Storybook's axe-based a11y addon offers:
- a panel with **Violations / Passes / Incomplete** tabs
- element highlighting with popovers
- rule links to axe docs
- a **`parameters.a11y.test: 'off' | 'todo' | 'error'`** mode, where 'todo' shows warnings in the UI without failing CI and 'error' fails in the UI and in CI

Through the Vitest addon, a11y runs alongside component tests, and the results feed the sidebar status and the widget's warning and error counts. Chromatic adds **baseline-based a11y regression tracking**, so only new violations are flagged, plus an Accessibility Dashboard.

### Cited Findings
- The addon is built on Deque's **axe-core**. Checks run automatically when you navigate to a story. It adds a toolbar button that **simulates vision impairments** and an Accessibility panel with three tabs:
  - **Violations**
  - **Passes**
  - **Incomplete** (needs manual verification)
  — [Storybook a11y docs (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- Clicking a violation shows the rule and suggested fixes. Highlighting can be toggled to show affected elements, and clicking a highlighted element opens a popover with violation details. Rules link to the axe-core rule descriptions. — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- Default rulesets are WCAG 2.0 and 2.1 A/AA plus Best Practices. WCAG 2.2 AA or AAA can be selected with `runOnly`. The default `config` disables the `region` rule, because it rarely applies to isolated components. Elements can be excluded through `context`, for example a `no-a11y-check` class. — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- `parameters.a11y` has `context`, `config` (passed to `axe.configure`), `options` (passed to `axe.run`) and `test`. Setting `globals.a11y.manual: true` skips automatic analysis, but manual checks in the panel still work. It can be set per project (`preview`), per file (`meta`) or per story. — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- **`test` modes**:
  - `'off'`: tests don't run, and the panel is still available.
  - `'todo'`: violations show as **warnings** in the Storybook UI, with no CI errors.
  - `'error'`: violations fail the test in the UI and in CLI/CI.
  — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- Recommended adoption workflow:
  1. Set `'error'` project-wide.
  2. Mark currently failing components `'todo'` and commit that as a baseline.
  3. Fix components one at a time, starting with fundamental ones like Button, then remove the parameter.
  — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- In the UI flow, you check **Accessibility** in the testing widget and press **Run component tests**. Status indicators appear beside each story, and choosing one opens the Accessibility panel. The widget shows warning and failure counts that filter the sidebar. — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx); [Writing tests](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx)
- FAQ notes:
  - CLI and CI results cap passes and inapplicable results at one node per rule, unless `resultTypes` is set.
  - Async components (Suspense or RSC) can produce false negatives. The `developmentModeForBuild` flag helps for built Storybooks.
  - Results vary across browser versions.
  — [Storybook a11y docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/accessibility-testing.mdx)
- **Chromatic Accessibility Tests**:
  - **Runs**: in CI, Chromatic uploads the Storybook and checks every story, across modes and viewports, in standardized browsers. Chrome only. TurboSnap limits which stories run. No custom axe rules and no checks inside iframes.
  - **Baselines**: each story has a baseline of accepted violations, and "new or altered violations" are flagged for review. A violation is identified by its type, DOM node and bounding box. "Fixes are auto-accepted to the baseline."
  - **Review UI**: the accessibility tab shows new violations, with previously accepted ones hidden behind a button.
  - **Reporting limits**: "Incomplete" results are not reported, and appear only in Storybook's panel.
  - **Disabling**: `parameters.chromatic.disableSnapshot` disables both visual and a11y tests.
  - **Availability**: Storybook projects only.
  — [Chromatic: Accessibility tests](https://www.chromatic.com/docs/accessibility/)
- Chromatic's **Accessibility Dashboard** gives "a bird's-eye view of WCAG issues across all your components". — [Chromatic: Accessibility tests](https://www.chromatic.com/docs/accessibility/) Search summaries of Chromatic marketing pages say it tracks WCAG violations "over time and by component". (search summary) — [Chromatic blog: Accessibility Testing & Dashboard are live](https://www.chromatic.com/blog/accessibility-testing-is-here/); [Chromatic accessibility feature page](https://www.chromatic.com/features/accessibility-test)
- Storybook 10.3 (March 2026) is described as including an "accessibility overhaul" or "a11y improvements". (search summary; details not verified) — [Storybook 10.3 blog](https://storybook.js.org/blog/storybook-10-3/); [Storybook 10.3 release page](https://storybook.js.org/releases/10.3)
- Ladle's docs state that it includes accessibility testing through axe-core. (search summary) — [Ladle docs: a11y](https://ladle.dev/docs/a11y)

### Inferences
- BlazorTestPlatform's axe checks map directly onto the 'off' / 'todo' / 'error' tri-state. 'todo' (known debt) as a non-failing warning, distinct from a hard failure, is a valuable UX distinction for showcasing gradual adoption.
- For a per-story a11y panel, the minimum expected set is: violations, passes and incomplete tabs; impact severity; node highlighting in the preview; and links to the rule docs. A dashboard of violations by component over time is the differentiating "trend" feature, as in Chromatic.
- Regression-only a11y gating (a baseline of accepted violations) is a strong pattern for legacy component libraries and pairs naturally with the visual-baseline storage.

### Gaps
- I could not verify the specific UI changes in Storybook 10.3's a11y overhaul.
- I found no official details on whether the Chromatic dashboard shows charts over time or WCAG-level filters. The fetched docs page doesn't describe them.
- I could not fetch Testing Library's guiding principles page (testing-library.com was blocked). Its well-known principle, "The more your tests resemble the way your software is used, the more confidence they can give you," and its query priority (ByRole first) are unverified in this session, except that Storybook's docs call `ByTestId` a "last resort".

## 4. How coverage (code coverage, story coverage) is surfaced

### Takeaway
Storybook 9+ shows **code coverage** from stories in the testing widget:
- a statements percentage, coloured red, orange or green by watermarks
- a click-through interactive HTML report at `/coverage/index.html`
- calculated only on full runs and disabled in watch mode

No tool I reviewed surfaces "story coverage" (which components lack stories) as a first-class metric.

### Cited Findings
- Coverage is enabled with the "coverage checkbox in the testing widget". It needs `@vitest/coverage-v8` (the default) or `@vitest/coverage-istanbul`. — [Storybook test coverage docs (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/test-coverage.mdx)
- After a run, the widget summarizes the **percentage of statements** covered by stories. The icon is **red below the low watermark, orange between the two, and green above the high one**, configured with `coverage.watermarks.statements` (default `[50, 80]`). — [Storybook test coverage docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/test-coverage.mdx)
- The full interactive report is served at `/coverage/index.html` on the running Storybook. You can open a component to see covered and uncovered lines and branches. — [Storybook test coverage docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/test-coverage.mdx); [Writing tests](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx)
- Limitations in the Storybook UI:
  - Coverage counts only stories.
  - It can only be calculated across all stories, not per story or group.
  - It is not calculated in watch mode, and enabling watch mode disables coverage.
  - Several Vitest options, such as `reporter` and `reportsDirectory`, are ignored in the UI.
  — [Storybook test coverage docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/test-coverage.mdx)
- In CI, the docs recommend running all tests with `--coverage` for project-wide coverage, "so each PR shows coverage changes". — [Writing tests](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx); [Storybook test coverage docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/test-coverage.mdx)
- Storybook 8.5 (January 2025) was reported as adding "improved accessibility, testing, and code coverage", the precursor to 9.0's widget. (headline only) — [AlternativeTo: Storybook 8.5](https://alternativeto.net/news/2025/1/storybook-8-5-has-been-released-with-improved-accessibility-testing-and-code-coverage)
- Storybook 9 beta: "with the click of a button, you can calculate test coverage to find out exactly which lines, functions, and branches are exercised by your component tests." (search summary) — [Storybook 9 beta blog](https://storybook.js.org/blog/storybook-9-beta/)
- The Cypress component testing docs link to code coverage for component tests. — [Cypress CT get started](https://docs.cypress.io/app/component-testing/get-started)

### Inferences
- For .NET, coverlet or Microsoft code coverage over bUnit runs could drive the same pattern: a watermark-coloured percentage in the workbench widget plus a click-through per-component report. A per-component coverage view would go beyond Storybook, whose coverage is global only.
- "Story coverage" is an open differentiation opportunity for BlazorTestPlatform: components or parameters without stories, stories without play or interaction tests, and stories excluded from a11y, visual or perf checks.

### Gaps
- I found no tool in scope with a built-in "story coverage" or "components without stories" report.
- Chromatic's coverage features, if any, were not found.

## 5. Docs and catalog experiences that make a component library explorable (autodocs, component status, design tokens, usage examples)

### Takeaway
Storybook Autodocs generates a per-component docs page from tags, args and argTypes:
- Title, Subtitle and Description
- the Primary story with zoom
- an interactive **Controls** (args) table
- all other stories
- an optional table of contents

MDX and doc blocks extend it. Histoire offers variant grids, copyable dynamic source, Markdown docs, Tailwind-derived design-token stories and fuzzy search. Recent Storybook releases (10.2 and 10.3) added new Viewports and Zoom UI and **Storybook MCP**, which exposes component metadata so AI agents can use components, write stories and run tests.

### Cited Findings
- **Autodocs** is triggered by `tags: ['autodocs']`, set globally in `preview` or per component or story. The default page has these blocks:
  - `Title`, `Subtitle`, `Description`
  - `Primary` (first story, with zoom)
  - `Controls` (an interactive table of args and argTypes)
  - `Stories` (the rest)
  - `docsMode: true` shows only docs pages in the sidebar.
  — [Storybook Autodocs docs (repo source)](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-docs/autodocs.mdx)
- A table of contents comes from the `toc` docs parameter, with selector options, and is hidden below 1200px width. `subcomponents` show as tabs in the `ArgTypes` block. Custom templates are available through `docs.page` or an MDX `isTemplate`, along with a custom docs container, a light/dark docs theme, and MDX component overrides. — [Storybook Autodocs docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-docs/autodocs.mdx)
- **Histoire** catalog features:
  - **variant grids**
  - **dynamic source** (copyable template, JSX or source)
  - extendable **Markdown docs** with syntax highlighting
  - built-in and custom controls (catalogued at controls.histoire.dev)
  - responsive viewport presets
  - backgrounds and checkerboard
  - dark theme
  - fuzzy search over story names and doc text
  - **automatic design-system stories generated from Tailwind CSS configuration** (design tokens)
  — [Histoire guide](https://histoire.dev/guide/)
- Storybook 9.0 (June 2025) highlights included "tags-based organization". (search summary) — [Storybook 9.0 release page](https://storybook.js.org/releases/9.0); [InfoQ: Storybook v9 released](https://infoq.com/news/2025/07/storybook-v9-released)
- Storybook 10.2 added "New Viewports and Zoom UI", typesafe CSF factories for Vue, Angular and Web Components (preview), and MDX support for Storybook MCP (experimental). Storybook 10.3 (March 2026) headlined **Storybook MCP for React** (preview), which lets AI agents "use real components, write stories, and run tests". (search summary) — [GitHub release v10.2.0](https://newreleases.io/project/github/storybookjs/storybook/release/v10.2.0); [Storybook 10.3 blog](https://storybook.js.org/blog/storybook-10-3/)
- In Storybook 10.4, MCP for React component metadata generation is "faster and higher quality, now relying on Volar and the TypeScript Language Server". — [AlternativeTo: Storybook 10.4](https://alternativeto.net/news/2026/5/storybook-10-4-brings-ai-auto-setup-change-detection-quick-sharing-and-tanstack-react/)
- Stories can be reused outside the workbench, imported into Playwright or Cypress E2E tests and into Vitest or Jest unit tests ("portable stories"). — [Writing tests](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/index.mdx)

### Inferences
- Showcase-site must-haves:
  - a per-component docs page auto-built from C# component parameters, mirroring Controls/ArgTypes with types, defaults and XML doc comments
  - a primary story plus a gallery of all stories
  - copyable Razor source per story
  - a test-status summary on the docs page, combining bUnit, Playwright per render mode, a11y, visual and perf results
- Exposing the component and story catalog through an MCP server mirrors where Storybook and, reportedly, Histoire are heading in 2026.
- A design-token page, generated from CSS variables or theme definitions in the way Histoire uses Tailwind, would improve explorability.

### Gaps
- I found no official first-party "component status" feature (stable, beta, deprecated badges) in Storybook or Histoire. Storybook tags are the closest mechanism, but I found no documented status-badge UI.
- I could not fetch Storybook's Controls and Doc Blocks pages separately. Control types are not enumerated here.

## 6. Collaboration and sharing (published Storybooks, permalinks, comments)

### Takeaway
Sharing centres on a **published Storybook per PR** (on Chromatic or static hosting), **permalinks** that reproduce a story and interaction failure for anyone, Chromatic's **per-snapshot threaded comments and reviewer assignments**, and PR status checks. Storybook 10.4 added **one-click publish to Chromatic** from the Storybook UI for feedback. Playwright traces can be shared and opened in a static, client-only viewer, and Cypress Cloud provides recorded runs, a Debug page and Test Replay.

### Cited Findings
- **Permalinks for reproductions**: anyone with the URL sees the same failure details without setup. Publishing Storybook on pull requests gives teams a shared reference for testing and debugging. — [Storybook interaction testing docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/interaction-testing.mdx)
- Setting `storybookUrl` to a published Storybook makes CI failure output link to the hosted story. — [Vitest addon docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/integrations/vitest-addon/index.mdx)
- Storybook 10.4 (May 2026): "publish your Storybook to Chromatic with one click so teammates can give feedback." — [AlternativeTo: Storybook 10.4](https://alternativeto.net/news/2026/5/storybook-10-4-brings-ai-auto-setup-change-detection-quick-sharing-and-tanstack-react/)
- Chromatic UI Review collaboration:
  - per-change comment boxes and threaded discussions pinned to snapshots, with "Resolve"
  - "Assign Reviewers", default reviewers, and component owners auto-assigned
  - an "Approve" button
  - a "UI Review" PR check
  — [Chromatic: UI Review](https://www.chromatic.com/docs/review/)
- Visual Tests addon baselines accepted locally sync to the cloud on push "for anyone who checks out the branch". — [Storybook visual testing docs](https://raw.githubusercontent.com/storybookjs/storybook/next/docs/writing-tests/visual-testing.mdx)
- Playwright traces open at trace.playwright.dev, by drag-and-drop or as `?trace=<url>`. Processing is entirely in the browser. — [Playwright Trace Viewer](https://playwright.dev/docs/trace-viewer)
- Cypress app features that depend on Cypress Cloud:
  - **Runs** tab: recorded runs scoped by branch.
  - **Debug** page: up to the first 100 failed tests, with links to Test Replay.
  - **Test Replay**: open-mode-style debugging for CI runs.
  - Flaky-test indicators link to Flaky Test Analytics.
  — [Cypress open mode docs](https://docs.cypress.io/app/core-concepts/open-mode)

### Inferences
- For BlazorTestPlatform, the shareable artifacts are:
  - a static or hosted workbench build per PR
  - deep links to story + panel + test result, including a specific Playwright render-mode run
  - a self-contained HTML report or trace for CI failures
- Review comments on snapshots and an approval PR check are SaaS-level features. A lighter version is a PR comment bot that links to the per-PR workbench with a summary of changed snapshots, a11y deltas and perf deltas.

### Gaps
- I didn't verify whether Storybook's native UI (outside Chromatic) supports comments. I found no evidence that it does.
- Storybook composition (embedding multiple Storybooks) and embed/iframe sharing were not researched in this pass.

## 7. Notable recent (2025-2026) releases

### Takeaway
2025-2026 moved story tools from component catalog to test platform:
- **Storybook 9** (June 2025): Vitest-powered "Storybook Test" with the unified widget for interaction, a11y, visual and coverage.
- **Storybook 10** (Oct 2025): ESM-only, module automocking, CSF Factories.
- **10.2 to 10.4** (2026): Viewports/Zoom UI, the Storybook MCP for AI agents, agentic setup, and git change detection.

Playwright dropped its experimental CT packages in favour of a story-gallery `mount` fixture. Chromatic added baseline-tracked accessibility tests and a dashboard.

### Cited Findings
- **Storybook 8.5** (Jan 2025): reported as improving accessibility, testing and code coverage. (headline) — [AlternativeTo](https://alternativeto.net/news/2025/1/storybook-8-5-has-been-released-with-improved-accessibility-testing-and-code-coverage)
- **Storybook 9.0** (June 2025): component testing, interactions, accessibility, visual changes, coverage, a **48% lighter** install and tags-based organization. A test widget gives "a holistic view of Interaction, Accessibility and Visual tests" and runs the full suite in one click. "Story generation" appears only in a Reddit post and is unconfirmed. (search summary) — [Storybook 9.0 release page](https://storybook.js.org/releases/9.0); [InfoQ](https://infoq.com/news/2025/07/storybook-v9-released); [Storybook 9 beta](https://storybook.js.org/blog/storybook-9-beta/)
- Storybook previewed a refreshed Accessibility addon in 2025. (title only) — [Storybook blog: Sneak peek: Accessibility Addon refresh](https://storybook.js.org/blog/preview-the-new-accessibility-addon/)
- **Storybook 10.0** (blog dated Oct 28, 2025):
  - ESM-only, with install **29% lighter** than 9
  - Node 20.16+, 22.19+ or 24+ required
  - **module automocking**
  - typesafe **CSF Factories** (React preview)
  - Next.js 16 and Vitest 4 support

  An AlternativeTo article is dated Nov 5, 2025. (search summary) — [Storybook 10 blog](https://storybook.js.org/blog/storybook-10/); [AlternativeTo: Storybook 10](https://alternativeto.net/news/2025/11/storybook-10-debuts-esm-only-29-lighter-module-automocking-and-typesafe-csf-factories)
- **Storybook 10.2**: New Viewports and Zoom UI, CSF factories for Vue, Angular and Web Components (preview), and MDX support for Storybook MCP (experimental). (search summary) — [GitHub release v10.2.0](https://newreleases.io/project/github/storybookjs/storybook/release/v10.2.0)
- **Storybook 10.3** (March 2026): Storybook MCP for React (preview), Vite 8, Next.js 16.2, ESLint 10, and Tailwind v4 support in the Pseudo-States addon. The Vitest addon got simplified configuration with "no more setup files required". It also included an accessibility overhaul and a CLI overhaul. (search summary) — [Storybook 10.3 blog](https://storybook.js.org/blog/storybook-10-3/); [Release page 10.3](https://storybook.js.org/releases/10.3)
- **Storybook 10.4** (May 2026):
  - **AI agent setup** (`npx storybook ai setup`): an agent generates config and mocks, writes stories and interaction tests for up to ten components, checks style rendering, and iterates until setup passes.
  - git **change-detection** sidebar filters
  - one-click **publish to Chromatic**
  - the `@storybook/tanstack-react` framework
  - React Native isolation improvements
  - better MCP metadata
  — [AlternativeTo: Storybook 10.4](https://alternativeto.net/news/2026/5/storybook-10-4-brings-ai-auto-setup-change-detection-quick-sharing-and-tanstack-react/); [Storybook 10.4 blog](https://storybook.js.org/blog/storybook-10-4/)

  Release-date conflict: AlternativeTo says May 20, 2026, and a third-party blog title says "Storybook 10.4.0 on May 14". — [ishu.dev](https://ishu.dev/post/storybook-10-4-0-change-aware-review-guide-2026-05-19)
- **Playwright**: the experimental CT packages (`@playwright/experimental-ct-react`, `-ct-react17`, `-ct-vue`) "have been removed and are no longer published", and users should "stay on Playwright 1.62" until they migrate. The replacement is a built-in `mount` fixture plus a user-owned story gallery page served by the app's dev server. — [Playwright component testing docs](https://playwright.dev/docs/test-components)
- **Chromatic**: Accessibility Tests (axe per story, a baseline of accepted violations, regression-only flagging, an accessibility tab in review) and an Accessibility Dashboard. — [Chromatic: Accessibility tests](https://www.chromatic.com/docs/accessibility/); [Chromatic blog: Accessibility Testing & Dashboard are live](https://www.chromatic.com/blog/accessibility-testing-is-here/)
- **Cypress** component testing (docs current as of Nov 2026): Vite 8, Angular 21-22, Next.js 15-16 and React 19 support, Svelte 5 (Alpha), and a `/cypress-author` AI skill. — [Cypress CT get started](https://docs.cypress.io/app/component-testing/get-started)

### Inferences
- The 2026 trend is **AI agents as first-class users** of component workbenches: Storybook MCP, `storybook ai setup`, the Cypress AI authoring skill and, reportedly, Histoire's MCP and agent pages. A showcase for BlazorTestPlatform would benefit from an MCP endpoint that lists stories and components, runs story tests and returns results.
- Because the Playwright gallery model removes the version where Playwright CT was introduced, the experimental CT is no longer the reference design. "Stories rendered by the app's own dev server, addressed by id" is now Playwright's official direction, which validates BlazorTestPlatform's approach of hosting stories in a real Blazor app across render modes.
- The Playwright version that removed CT (1.63 or later) is inferred from "stay on 1.62" and was not stated explicitly.

### Gaps
- I could not confirm Storybook 10.1 features.
- I found no Storybook 11 release by Nov 2026, though I didn't search specifically for one.
- The exact Playwright release number that introduced the `mount` fixture and gallery model is not stated on the docs page.
- I could not find the Chromatic Accessibility Tests GA date.
- Histoire's version, maintenance status and 2025-2026 changelog were not found. The guide shows a "2022-present" copyright and an empty "Last updated" field.
- Ladle's 2025-2026 release activity was not researched, because ladle.dev was blocked for direct fetch.
