# Improvement progress

This document tracks the modernization work on BlazorTestPlatform: what was audited, what was found, what was
changed, how it was verified and what remains. Statuses: *Not investigated*, *Investigated*, *Planned*, *In progress*,
*Implemented*, *Verified*, *Deferred*, *Blocked*. An item is *Verified* only when a test that fails without the change
(or a check named in the row) passed with it; *Implemented* means the change is in and the full suites pass, but no
dedicated automated test isolates it.

## Baseline (before any change)

| Check | Command | Result |
|---|---|---|
| Build | `dotnet build BlazorTestPlatform.slnx` | 0 warnings, 0 errors |
| Unit and in-memory tests | `dotnet test --project tests/<project>` for each of the 7 projects | 339 passed |
| End-to-end (bUnit, cross-hosting matrix, visual, axe, in-app play, Firefox, WebKit) | `dotnet test --project samples/Contoso.UI.Tests` | 398 passed (Windows, local baselines) |

## Audit

Every package was read in full by six parallel reviews (Core stories/data/HTTP/matrix/coverage; Core interactions/
results/configuration/performance/accessibility; Bunit + Accessibility; Playwright + Visual; Workbench (security and
correctness); Reporting, CLI, Generators and packaging). Findings were re-checked against the code before any fix, and
several were confirmed with throwaway harnesses or by running the new regression tests against the unfixed code.

| Package | Responsibility | Size | Quality after the audit |
|---|---|---|---|
| `BlazorTestPlatform.Core` | Story model, catalog, matrix, interactions/assertions, test data, HTTP and JS mocks, settings, results, gate models, coverage | ~8.4k lines | Sound design; defects in negated assertions, settings parsing, concurrency of baseline files and several data helpers (fixed) |
| `BlazorTestPlatform.Accessibility` | ARIA role/name engine, DOM queries, CSS visibility, 37 markup rules, fingerprints | ~2.2k | Rule divergences from axe-core, name-computation edge cases and CSS cascade gaps (fixed) |
| `BlazorTestPlatform.Bunit` | In-memory driver and runner, render metrics, review gate | ~1.7k | Lost form state on re-render, swallowed async exceptions, runner cancellation/keys, focus tracking by path (fixed) |
| `BlazorTestPlatform.Playwright` | Browser driver, matrix runner, axe, diagnostics, hosts | ~2.0k | Timeouts only cooperative, crash and cancellation paths (fixed) |
| `BlazorTestPlatform.Visual` | PNG codec, perceptual diff, snapshot store, baseline manifest | ~1.3k | Codec and comparer correct; evidence deletion, hostile input, path handling (fixed) |
| `BlazorTestPlatform.Workbench` | Workbench UI, story frames, per-story DI overrides, in-app driver | ~2.9k | No production gate, link injection, singleton disposal (fixed) |
| `BlazorTestPlatform.Reporting` | HTML, JUnit, Markdown, PR comment, flake history | ~1.1k | HTML encoding was complete; JUnit/Markdown/PR-comment/history robustness (fixed) |
| `BlazorTestPlatform.Cli` | `btp` tool | ~0.3k | Destructive `clean`, trusting `approve` (fixed) |
| `BlazorTestPlatform.Generators` | Catalog source generator, analyzers BTP001-BTP005 | ~0.5k | Record and edge-case discovery (fixed) |

## Work items

### Phase 1: correctness, capability model, isolation, security

| Id | Description | Priority | Packages | Status | Tests | Docs |
|---|---|---|---|---|---|---|
| CAP-1 | Story-level capability requirements: `Requires(DriverCapabilities, reason)`, `Story.CanRunOn`, `Story.UnsupportedReason`, `StoryCatalog.Supporting`; bUnit, browser and in-app runners skip up front with the reason | P1 | Core, Bunit, Playwright, Workbench | Verified | `BunitDriverTests.Declared_requirements_*`, `Catalogs_select_the_stories_a_driver_can_run` | interactions.md (driver capabilities) |
| CAP-2 | `BunitStoryDriver.CapabilitiesFor(story)`, `PlaywrightStoryDriver.CapabilitiesFor(mode, fakeTime)` and the per-driver capability table | P1 | Bunit, Playwright | Verified | as CAP-1 | interactions.md |
| C-1 | Negated single-element assertions passed vacuously on a missing or ambiguous element | P1 | Core | Verified | `AssertionTests.Negated_state_assertions_*` | interactions.md |
| C-2 | `ToBeVisible`/`ToBeHidden` not strict; `IsVisibleAsync` contradicted its doc | P2 | Core | Verified | `AssertionTests.Visibility_assertions_are_strict_*` | interactions.md |
| C-3 | `times: 0` assertions passed on the first probe; negative counts accepted | P2 | Core | Verified | `AssertionTests.Zero_times_waits_for_idle_like_the_not_form` | interactions.md |
| C-4 | JS-call arguments formatted with `ToString()` (arrays compared equal) | P2 | Core | Verified | `AssertionTests.JS_arguments_format_collections_and_objects_by_content` | interactions.md |
| C-5 | Performance baseline store lost entries under parallel writers | P2 | Core | Verified | `HardeningTests.Parallel_baseline_writers_keep_every_entry`, `A_corrupt_baseline_file_is_never_overwritten` | — |
| C-6 | Empty path variables resolved to the project directory and `btp clean` deleted it | P0 | Core, Cli | Verified | `HardeningTests.Empty_path_variables_*`, `Clearing_refuses_directories_that_hold_sources` | configuration.md, ci.md |
| C-7 | CI detection missed Jenkins, CodeBuild, `CI=<name>` (baselines written on CI) | P2 | Core | Verified | `HardeningTests.Continuous_integration_is_detected_across_systems`, `Btp_ci_overrides_detection` | configuration.md |
| C-8 | Unreadable results dropped silently; `--fail-on-failures` could pass | P2 | Core, Cli | Verified | `HardeningTests.Unreadable_results_are_reported_not_silently_dropped` | ci.md |
| C-9 | bUnit result key ignored viewport/theme/args | P2 | Bunit | Verified | `BunitDriverTests.Runs_with_another_viewport_get_their_own_result_key` | CHANGELOG |
| C-10 | Reviews without fingerprint never stale; reviews without checks approved | P2 | Core | Verified | `HardeningTests.Reviews_without_a_fingerprint_or_checks_are_not_approved` | — |
| C-11 | `Poll` with `Timeout.InfiniteTimeSpan` failed at once | P3 | Core | Verified | `HardeningTests.Infinite_timeouts_wait_until_success` | — |
| C-12 | `ControlOrMeta` always Meta; `Shift++` unparsable | P3 | Core | Verified | `HardeningTests.Chords_can_end_with_plus_*` | — |
| C-13 | Story-scoped suppressions applied when no story id was given | P3 | Core | Verified | `HardeningTests.Story_scoped_suppressions_do_not_apply_without_a_story` | — |
| C-14 | Upper median for even sample counts; negative warm-up crashed | P3 | Bunit | Verified | `BunitDriverTests.Medians_average_the_middle_pair` | — |
| C-15 | Caller cancellation reported as "timed out" (browser retries kept going) | P1 | Bunit, Playwright | Verified (bUnit), Implemented (browser) | `BunitDriverTests.Caller_cancellation_is_not_reported_as_a_timeout`, `Story_timeouts_are_reported_as_failures` | cross-hosting.md |
| S-1 | Removing a live override with an unknown default passed null | P2 | Core | Verified | `ArgOverrideTests.Removing_an_override_whose_default_is_unknown_recreates_the_component` | — |
| S-2 | `HttpMock.ClearRequests()` during an in-flight request threw | P2 | Core | Verified | `DataHardeningTests.Clearing_requests_while_one_is_in_flight_*` | — |
| S-3 | `MockJS` module setups did not accumulate | P2 | Core | Verified | `DataHardeningTests.Module_setups_accumulate_*` | — |
| S-4 | Relative URL matching assumed a trailing slash on `BaseAddress` | P3 | Core | Verified | `DataHardeningTests.Relative_urls_follow_how_http_client_resolves_the_base_address` | — |
| S-5 | `Never()` still timed out after the real 100 s client timeout | P3 | Core | Verified | `DataHardeningTests.Mock_clients_never_time_out_on_their_own` | — |
| S-6 | Enum aliases counted as separate states | P3 | Core | Verified | `DataHardeningTests.Enum_aliases_are_one_state` | — |
| S-7 | `TimeOnly` args lost sub-millisecond precision | P3 | Core | Verified | `DataHardeningTests.Time_values_with_sub_second_precision_round_trip` | — |
| S-8 | `NextDecimal` returned `max` for negative ranges | P3 | Core | Verified | `DataHardeningTests.Next_decimal_*` (sequence unchanged for non-negative ranges) | — |
| S-9 | Repeated matrix dimensions produced duplicate cells; in-memory keys accepted | P3 | Core | Verified | `DataHardeningTests.Matrix_dimensions_are_deduplicated_*` | — |
| W-1 | Workbench live in Production with no activation or authorization | P0 | Workbench | Verified | `WorkbenchSecurityTests.*`, sample `WorkbenchAccessTests` (real host: 404 without markup), trimmed Production publish (123 cases) | getting-started.md, configuration.md |
| W-2 | Links could pass `javascript:`/`vbscript:`/`data:` URLs as args | P1 | Core, Workbench | Verified | `HardeningTests.Script_urls_are_recognized_*`, `Links_cannot_pass_script_urls_but_typed_values_can` | getting-started.md |
| W-3 | Frame scopes disposed application singletons that stories override; options monitor disposed | P1 | Workbench | Verified (known disposable types, options monitor) | `WorkbenchSecurityTests.Disposable_application_singletons_*`, `The_application_options_monitor_survives_story_scopes` | test-data.md (factory-registered singletons remain a documented limitation) |
| W-4 | Optional resolution of story-only service types threw | P1 | Workbench | Verified | `WorkbenchSecurityTests.Services_only_stories_register_stay_optional_elsewhere` | test-data.md |
| W-5 | Play results posted to `*`; shell accepted any origin | P2 | Workbench | Implemented | full e2e suite | getting-started.md |
| W-6 | In-app play fire-and-forget: unreported failures, leaked module, no capability check | P2 | Workbench | Implemented | sample `InAppPlayTests` (76 cases) | — |
| P-1 | Per-attempt browser timeout only cooperative | P2 | Playwright | Implemented | full e2e suite | cross-hosting.md |
| P-2 | `WaitForAssets` hung on offscreen lazy images | P2 | Playwright | Implemented | full e2e suite | cross-hosting.md |
| P-3 | Crashed browser stayed cached | P2 | Playwright | Implemented | full e2e suite | cross-hosting.md |
| P-4 | `RunAsync` could throw from diagnostics/trace instead of returning a failed result | P2 | Playwright | Implemented | full e2e suite | cross-hosting.md |
| P-5 | `dotnet run` host orphaned when startup failed | P2 | Playwright | Implemented | — | — |
| P-6 | Story error-boundary errors after hydration only caught via console | P3 | Playwright | Implemented | full e2e suite | cross-hosting.md |
| P-7 | Early console errors evicted by later logging | P3 | Playwright | Implemented | — | — |
| V-1 | Later match or passing retry deleted mismatch evidence | P2 | Visual, Playwright | Verified | `SnapshotStoreTests.A_later_matching_render_mode_keeps_the_evidence_*` | cross-hosting.md |
| V-2 | PNG decoder accepted hostile input | P3 | Visual | Verified | `PngCodecTests.Corrupt_chunks_*`, `Invalid_bit_depth_*`, `Huge_dimensions_*`, `Truncated_*`, `Decompression_bombs_*`, `Short_headers_*` | CHANGELOG |
| V-3 | Non-atomic baseline writes; per-instance locks | P3 | Visual | Verified | `SnapshotStoreTests.Writes_leave_no_temporary_files` | — |
| V-4 | `btp approve` trusted paths in result JSON | P2 | Visual, Cli | Verified | `SnapshotStoreTests.Approval_*`, `Snapshot_names_cannot_leave_the_baseline_directory` | ci.md |
| R-1 | JUnit crashed on control characters | P2 | Reporting | Verified | `ReportingTests.JUnit_survives_control_characters_and_ansi_colours` | — |
| R-2 | Markdown tables broke on result keys; `@mentions` and backticks passed through | P2 | Reporting | Verified | `ReportingTests.Markdown_neutralizes_*` | ci.md |
| R-3 | PR comment lookup matched the marker in anyone's comment | P2 | Reporting | Verified | `PullRequestCommentTests.A_person_pasting_the_marker_*`, `Own_comments_are_recognized_by_marker_and_author` | ci.md, configuration.md |
| R-4 | Corrupt flake history broke reports; non-atomic writes | P2 | Reporting | Verified | `ReportingTests.Corrupt_flake_history_*` | ci.md |
| R-5 | Non-HTTP errors while commenting failed `btp report` | P3 | Cli | Implemented | — | ci.md |
| R-6 | Report help links not restricted to http(s) | P3 | Reporting | Verified | `ReportingTests.Html_report_links_only_http_help_urls` | — |
| R-7 | `btp` had no roll-forward policy | P3 | Cli | Implemented | — | — |
| G-1 | Generator skipped records; invalid namespace for digit-leading assembly names; `required`/obsolete/file-local types | P3 | Generators, Core | Verified | `StoryCatalogGeneratorTests.*` | CHANGELOG |
| G-2 | Packages referencing Core did not pass its generator/analyzers to consumers | P3 | packaging | Verified | `dotnet pack src/BlazorTestPlatform.Bunit`: Core dependency now `include="All"` | CHANGELOG |
| B-1 | bUnit lost typed text and checked state of unbound controls on re-render | P1 | Bunit | Verified | `BrowserFidelityTests.Typed_text_survives_*`, `Unbound_checkboxes_keep_their_state`, `Values_the_component_renders_replace_the_typed_ones` (fail without the fix) | interactions.md |
| B-2 | Enter did not fire `keypress` (bUnit and in-app) | P1 | Bunit, Workbench | Verified (bUnit), Implemented (in-app) | `BrowserFidelityTests.Enter_fires_keypress_like_browsers` | — |
| B-3 | `aria-valid-attr-value` false positives vs axe-core | P1 | Accessibility | Verified | `MarkupRuleTests.Rules_follow_axe_core_where_it_does_not_fail` | quality-gates.md |
| B-4 | `nested-interactive` flagged links, menu items and APG trees | P1 | Accessibility | Verified | as B-3 | — |
| B-5 | Radios did not uncheck the rest of their group | P3 | Bunit | Verified | `BrowserFidelityTests.Checking_a_radio_unchecks_the_rest_of_its_group` | — |
| B-6 | Self-referencing `aria-labelledby` lost the element's own text; hidden label targets lost their children (both engines) | P2 | Accessibility, Workbench | Verified (C#), parity fixture in `DriverParityTests` | `Accessible_names_are_computed` (3 new cases), `DriverParityTests` "labelled by" fixture | — |
| B-7 | Empty lists/listboxes failed `aria-required-children`; native `<dialog>` failed `aria-dialog-name`; `summary-name` used raw text | P2 | Accessibility | Verified | as B-3 | — |
| B-8 | Exceptions thrown after an `await` left the bUnit story passed | P1 | Bunit | Verified | `BunitDriverTests.Exceptions_thrown_after_an_await_fail_the_story` (fails without the fix) | CHANGELOG |
| B-9 | Stories without play were checked on their first frame | P2 | Bunit | Implemented | full suites | — |
| B-10 | `MeasureAsync` dropped viewport/theme/stylesheets; allocation floor depended on test order; sessions defaulted to `BtpSettings.Current` policy | P2 | Bunit | Implemented | full suites | — |

### Phase 3 and 5: new capabilities

| Id | Description | Priority | Packages | Status | Tests | Docs |
|---|---|---|---|---|---|---|
| Q-1 | Pairwise (all-pairs) and constrained combinatorial variants; public `Combinatorics` | P2 | Core | Verified | `CombinatoricsTests` (12) | stories.md |
| Q-2 | Deterministic sharding (`Shard`, `BTP_SHARD`, `InShard`) for distributed CI; used by the sample's browser suites | P2 | Core, samples | Verified | `ShardTests` (11) | ci.md, configuration.md |
| Q-3 | Versioned result files (`schemaVersion`); newer versions reported, not misread | P1 | Core | Verified | `HardeningTests.Results_carry_a_schema_version_*` | ci.md |

### Second pass: deferred items and the remaining mission scope

| Id | Description | Priority | Packages | Status | Tests | Docs |
|---|---|---|---|---|---|---|
| B-11 | Focus, hover and `@ref` targets tracked by element identity (anchors relocated after each render), not structural path | P2 | Bunit | Verified | `BrowserFidelityTests` focus-insertion case (fails without the change) | interactions.md |
| B-12 | `<summary>` toggles `<details>` (+`toggle`), arrow keys in radio groups, textarea/contenteditable Enter, `buttons` bitmask, `preventDefault` on checkbox/radio clicks, `dragover`/`dragleave` drop semantics, Tab from the click point, `readonly` fill waiting; same rules in the in-app driver, `change` only on a real change | P2 | Bunit, Workbench | Verified (bUnit), Implemented (in-app, full e2e) | 9 `BrowserFidelityTests` (each failed without its change) | interactions.md |
| A-1 | CSS cascade: `@layer` (statements, blocks, nested, anonymous, reversed for `!important`), `@container`, per-selector specificity of lists, `inherit`/`initial`/`unset`/`revert`, author `display` over `hidden`, bare boolean media features; missing stylesheets fail | P2 | Accessibility, Bunit | Verified | `CssVisibilityTests` (layers, layer order theory, container, lists, keywords, hidden override, boolean media), `JSAndCssTests` missing stylesheet | interactions.md |
| A-2 | Names and roles in both engines: controls inside `aria-labelledby` targets, wrapping labels, `title=""`, `input type=file`, full ARIA 1.2 global attributes, `aria-level` exemption for `h1`-`h6` only | P3 | Accessibility, Workbench | Verified | `AccessibilityEngineTests` cases, `DriverParityTests` "names and roles" fixture | CHANGELOG |
| A-3 | Names from content separate block and table-cell text (`Plan Team`), as browsers do (both engines) | P2 | Accessibility, Workbench | Verified | `AriaSnapshotTests.Text_in_separate_cells_*`, sample "Accessibility tree" story in every driver | CHANGELOG |
| A-4 | Accessibility tree snapshots: `ctx.ExpectAriaSnapshotAsync`, `AriaSnapshot.Of`, JS port in `btp-driver.js`, `IStoryDriver.GetAriaSnapshotAsync` | P2 | Core, Accessibility, Bunit, Playwright, Workbench | Verified | `AriaSnapshotTests` (5), sample `Testing/Driver parity/Accessibility tree` under bUnit, 4 render modes, Firefox, WebKit, in-app | interactions.md, quality-gates.md |
| S-10 | `HttpMock` named clients keep `AddHttpClient` base address, headers and delegating handlers | P2 | Core | Verified | `DataHardeningTests` named-client composition | test-data.md |
| S-11 | Story option objects read-only once built (shared catalogs) | P2 | Core | Verified | `HardeningTests` read-only options, `BrowserBudgetTests.Story_budgets_are_frozen_once_built` | stories.md |
| S-12 | JS mocks for `GetValueAsync`/`SetValueAsync`/`InvokeConstructorAsync`; in-process references of missing mocked modules | P3 | Core | Verified | `JSInteropTests` (property/constructor, strict get, in-process get, missing module) | test-data.md |
| W-7 | Story registrations can depend on application services in the workbench (fallback resolver), disposed scopes reject use | P1 | Workbench | Verified | `ServiceOverrideTests` (app-service dependency, borrowed disposal, disposed scope) | test-data.md |
| W-8 | Keyed and unregistered open-generic overrides flagged; overrides probed under every render mode and cached; reviews cached by file time; story host cancels play and releases its module on disposal | P3 | Workbench | Verified (probe, keyed), Implemented (caching, disposal) | `ServiceOverrideTests` (WebAssembly-only probe, keyed flag) | CHANGELOG |
| W-9 | Workbench inspector (actions, requests, JS calls, console, accessibility tree) and interaction recorder generating play code with unique readable locators | P2 | Workbench | Verified | sample `WorkbenchInspectorTests` (recorder under Server and WebAssembly, tree), also against the trimmed Production publish | getting-started.md |
| V-5 | Translucent pixels compared after compositing (alpha-only changes detected) | P3 | Visual | Verified | `VisualTests` alpha case | CHANGELOG |
| V-6 | Baselines written by an attempt that fails another check are reverted | P3 | Visual, Playwright | Verified | `VisualTests` revert case | CHANGELOG |
| P-8 | Process host retries startup when the port is taken | P3 | Playwright | Implemented | — | CHANGELOG |
| P-9 | Browser emulation: geolocation, permissions, latency, offline (`DriverCapabilities.BrowserEmulation`) | P2 | Core, Playwright, Workbench | Verified | sample `Testing/Browser emulation` stories in Chromium (Server, WebAssembly, Auto), Firefox and WebKit; `BrowserEmulationTests` (requirements, validation, bUnit skips) | interactions.md |
| P-10 | Browser runtime metrics (CLS, LCP, long tasks, DOM nodes, heap) on results, `BrowserBudget` with not-reported limits listed | P2 | Core, Playwright | Verified | `BrowserBudgetTests` (4), sample `BrowserMetricsTests` (Chromium, Firefox) | quality-gates.md |
| G-3 | BTP005 ignores stories added in exclusive branches | P3 | Generators | Verified | `StoryAnalyzerTests` exclusive branches | CHANGELOG |
| G-4 | Core packs the generator (and code fixes) from wherever they were built | P3 | packaging | Verified | `dotnet pack` with `-o` and `--artifacts-path` (generator), pack check below (code fixes) | CHANGELOG |
| G-5 | Code fixes for BTP001, BTP002, BTP003, BTP005 in a separate `BlazorTestPlatform.CodeFixes` assembly packed under `analyzers/dotnet/cs` | P2 | CodeFixes, Core | Verified | `StoryCodeFixTests` (5, each applies the fix and re-runs the analyzer) | stories.md, CHANGELOG |
| R-7 | Flake history counts flips and ranks by them | P3 | Reporting, Cli | Verified | `ReportingTests` flips | ci.md |
| R-8 | SARIF 2.1.0 output (`btp report --sarif`) | P2 | Reporting, Cli | Verified | `ReportingTests` SARIF | ci.md |
| R-9 | OpenTelemetry: `btp.story`/`btp.step` activities, `btp.story.runs`/`btp.story.duration` | P2 | Core, Bunit, Playwright | Verified | `TelemetryTests` (traced runs scoped to a test root activity, failed runs) | ci.md |
| R-10 | Story source locations (generator-emitted), on stories and results | P2 | Generators, Core | Verified | `StoryCatalogGeneratorTests`, `TelemetryTests.Stories_and_results_know_*` | stories.md |
| Q-4 | Test impact analysis (`StoryImpact`, `BTP_CHANGED_SINCE`) | P2 | Core | Verified | `StoryImpactTests` (10; found that untracked files were listed only under the working directory) | ci.md, configuration.md |
| DX-1 | xUnit, NUnit, MSTest and TUnit packages (data rows, categories, skip reasons, skipped runs reported as skips) | P1 | Xunit, NUnit, MSTest, TUnit, Core | Verified | `tests/BlazorTestPlatform.{Xunit,NUnit,MSTest,TUnit}.Tests`: real data-driven runs (pass, discovery skip, driver skip) plus failure and skip mapping | test-frameworks.md |
| DX-2 | `btp init` scaffolding (test class per framework, first story, GitHub/Azure/GitLab pipelines on the matching Playwright image) | P2 | Cli | Verified (generated code), Implemented (pipelines) | `ScaffoldingTests` (generated classes compile against the four adapters; pipelines checked for image, project and expressions); manual run in a temporary directory | test-frameworks.md, ci.md |
| CI-1 | Azure Pipelines and GitLab guidance, SARIF upload, sharding there | P2 | docs | Implemented | pipeline templates not executed on Azure or GitLab from here | ci.md |
| CI-2 | Mutation testing guidance (Stryker.NET on the bUnit suite) | P3 | docs | Implemented | — | ci.md |

## Verification log

| Date | Command | Result |
|---|---|---|
| 2026-11-02 | `dotnet build BlazorTestPlatform.slnx -c Release -p:TreatWarningsAsErrors=true` | 0 warnings, 0 errors |
| 2026-11-02 | `dotnet publish samples/Contoso.Workbench -c Release -warnaserror:IL2026,IL2060,IL2070,IL2072,IL2091` (CI's trimming guard) | succeeded, no trimming warnings |
| 2026-11-02 | published app, `ASPNETCORE_ENVIRONMENT=Production`, `BTP_BASE_URL=... dotnet test --project samples/Contoso.UI.Tests -c Release -- --filter-class '*CrossHostingTests' --filter-class '*InAppPlayTests' --filter-class '*ArgControlsTests'` | 123 passed |
| 2026-11-02 | unit and in-memory suites (7 projects) | 494 passed (Accessibility 135, Bunit 61, Core 196, Generators 12, Reporting 18, Visual 37, Workbench 35) |
| 2026-11-02 | `dotnet test --project samples/Contoso.UI.Tests` after the Phase 1 fixes | 398 passed |
| 2026-11-02 | `dotnet test --project samples/Contoso.UI.Tests -- --filter-class '*WorkbenchAccessTests'` | 3 passed |
| 2026-11-02 | `dotnet test --project samples/Contoso.UI.Tests` with every change (bUnit, cross-hosting matrix, visual, axe, in-app play, Firefox, WebKit, driver parity incl. the new "labelled by" fixture, workbench access) | 402 passed |
| 2026-11-03 | `dotnet build BlazorTestPlatform.slnx -c Release -p:TreatWarningsAsErrors=true` (second pass, 23 projects) | 0 warnings, 0 errors |
| 2026-11-03 | unit and in-memory suites, Release (12 projects) | 613 tests: 605 passed, 8 skipped by design (the framework packages' "skipped" and "needs a browser" rows): Accessibility 158, Bunit 88, Cli 8, Core 217, Generators 18, MSTest 6, NUnit 6, Reporting 20, TUnit 6, Visual 39, Workbench 40, Xunit 7 |
| 2026-11-03 | trimmed publish with `-warnaserror:IL2026,IL2060,IL2070,IL2072,IL2091`, then the published app in `Production` with `--filter-class '*CrossHostingTests' '*InAppPlayTests' '*ArgControlsTests' '*WorkbenchInspectorTests'` | no trimming warnings; 137 passed |
| 2026-11-03 | `dotnet test --project samples/Contoso.UI.Tests -- --filter-class '*CrossHostingTests' --filter-class '*CrossBrowserTests' --filter-class '*InAppPlayTests'` (emulation stories, accessibility tree story) | 221 passed |
| 2026-11-03 | `dotnet test --project samples/Contoso.UI.Tests` (full suite: bUnit, cross-hosting matrix, visual, axe, in-app play, Firefox, WebKit, driver parity, workbench access, emulation, metrics, inspector) | first run 427 of 429: two Firefox runs timed out at 60 s under full parallel load (open 10 s, axe 11 s; both pass alone). Contexts without emulation then went back to passing no `Permissions` (an empty list made Playwright grant on every context); second run 429 passed. No retries locally (`BTP_RETRIES` defaults to 2 on CI only) |
| 2026-11-03 | `dotnet pack` Core, Xunit, NUnit, MSTest, TUnit; a consumer project built against the local packages | Core ships `analyzers/dotnet/cs/BlazorTestPlatform.Generators.dll` and `BlazorTestPlatform.CodeFixes.dll`; the consumer build reports BTP003 from the package, with no analyzer load warnings (CS8032/AD0001) |

## Deferred

| Area | Item | Reason |
|---|---|---|
| Core | `By.TestIdAttribute` is process-global | Making it per run needs the attribute on the locator chain (a `DriverJson` and API change); a per-assembly setting is enough for every project seen so far |
| Core | `Story.Matrix` (`MatrixFilter`) exposes mutable `HashSet`s | The builder API hands those sets to story code (`m.ExcludedHostingModels.Add(...)`); freezing them needs read-only set types, a breaking change |
| Workbench | Factory-registered disposable singletons that stories override are still wrapped (disposed by frame scopes) | The container gives no way to inspect a factory's result type before it runs; documented in test-data.md |
| Workbench | Component hierarchy and state inspectors | Blazor exposes no render tree in browsers; the inspector covers what the bridge can observe (actions, requests, JS calls, console, accessibility tree) |
| Workbench | Recording hovers, drags and assertions | The recorder writes actions; assertions need intent. Hover and drag recording would need pointer-path heuristics |
| Playwright | Bandwidth throttling | Only Chromium exposes it (CDP); latency emulation works in every browser |
| Reporting | Flake rate counts failures and flaky runs | Kept for compatibility of the ranking; flips now separate a flaky key from a fixed regression |
| Workbench | Exception messages shown in frames | By design: frames are gated (`WorkbenchOptions.Enabled`, `AuthorizationPolicy`) and show developers why a story failed |
| Mission scope | Automated mutation testing in CI, documentation-site generation | Guidance only (ci.md): mutation runs take too long for pull requests, and the guides are Markdown in the repository |
