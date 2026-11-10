using BlazorTestPlatform;
using BlazorTestPlatform.Hub;

namespace BlazorTestPlatform.Demo.Workbench;

/// <summary>
/// A link of the tour: a story in the workbench (optionally in a render mode, with its play script running), or a page
/// of the hub.
/// </summary>
public sealed record TourLink(string Label, string? StoryId = null, HostingModel? Mode = null, bool Play = false, string? HubPage = null)
{
    /// <summary>A link to a hub page (path under <c>/_btp</c>).</summary>
    public static TourLink Hub(string label, string page = "") => new(label, HubPage: page);
}

/// <summary>One feature: what it does, where to see it, the code that declares it and the test that runs it.</summary>
public sealed record TourFeature(string Title, string Text, IReadOnlyList<TourLink> Links, IReadOnlyList<string> Code, string? Test = null);

/// <summary>A stage of the platform on the tour page.</summary>
public sealed record TourSection(string Id, string Step, string Title, string Lead, IReadOnlyList<TourFeature> Features);

/// <summary>
/// The guided tour on the demo's home page. Every story link points at a real story and every file at a real file:
/// <c>TourTests</c> fails when either goes stale.
/// </summary>
public static class Tour
{
    private const string Stories = "demo/BlazorTestPlatform.Demo.UI.Stories/";
    private const string Tests = "demo/BlazorTestPlatform.Demo.UI.Tests/";
    private const string Hub = "src/BlazorTestPlatform.Hub/";
    private const string Reporting = "src/BlazorTestPlatform.Reporting/";

    public static IReadOnlyList<TourSection> Sections { get; } =
    [
        new("author", "1", "Author", "A story is one named state of a component, declared in C#. Everything else on this site is generated from stories: tests, snapshots, scans, budgets, coverage and history.",
        [
            new("Stories are states",
                "Each Add(...) call is a state: arguments, content, layout. The workbench renders it, the Controls panel edits its parameters live, and the URL keeps your edits so a link reproduces what you see.",
                [new("Primary button", "inputs-button--primary"), new("Secondary (try Controls)", "inputs-button--secondary")],
                [Stories + "ButtonStories.cs"], "ComponentStoryTests"),
            new("Variants and pairwise matrices",
                "AddVariants(...) generates one story per combination. Pairwise() keeps every pair of parameter values: the badge's 45 supported combinations become 17 stories, picked deterministically.",
                [new("Badge (variants in the sidebar)", "display-badge--default"), new("Button sizes × variants", "inputs-button--variant-primary-size-small")],
                [Stories + "BadgeStories.cs"], "CombinatoricsTests"),
            new("Arguments, templates and decorators",
                "Arguments can be computed from seeded fake data, RenderFragment and RenderFragment<T> parameters take Razor templates, and decorators wrap a story in containers, providers or cascading values.",
                [new("Item template", "data-person-list--custom-item-template"), new("Empty-state fragment", "data-person-list--empty-with-custom-content"), new("Cascading density", "data-person-list--compact-density")],
                [Stories + "PersonListStories.cs", Stories + "Fragments.razor"]),
            new("A workbench to browse and record",
                "Every story across themes, viewports and render modes. Run its play script inside the frame, inspect actions, requests, JS calls and the accessibility tree, and record new play functions with readable locators.",
                [new("Run the tabs script", "navigation-tabs--keyboard-navigation", Play: true), new("Record on the todo list", "data-todo-list--empty")],
                ["src/BlazorTestPlatform.Workbench/"], "WorkbenchInspectorTests"),
        ]),

        new("run", "2", "Run everywhere", "One play script, three drivers: bUnit in milliseconds, Playwright in real browsers under four render modes, and the in-app driver inside the workbench frame. Interactions behave like a person's: role and label locators, keyboard, focus, forms, waiting assertions.",
        [
            new("Keyboard, focus and forms",
                "Tab order, arrow keys, Escape, implicit submission and focus moved by the component are checked the same way in memory and in browsers.",
                [new("Dialog: Escape returns focus", "overlays-dialog--keyboard", Play: true), new("Sign-up validation", "patterns-sign-up-form--validation-errors", Play: true)],
                [Stories + "DialogStories.cs", Stories + "SignUpFormStories.cs"]),
            new("The same story, four render modes",
                "One host serves each story under static SSR, Interactive Server, WebAssembly and Auto. The runner checks hydration, the renderer that actually ran and console errors; the matrix shows every cell.",
                [
                    new("SSR", "display-runtime-badge--masked-value", HostingModel.StaticServer),
                    new("Server", "display-runtime-badge--masked-value", HostingModel.InteractiveServer, Play: true),
                    new("WebAssembly", "display-runtime-badge--masked-value", HostingModel.InteractiveWebAssembly, Play: true),
                    TourLink.Hub("Result matrix", "tests?view=matrix"),
                ],
                [Tests + "BrowserMatrixTests.cs"], "CrossHostingTests, CrossBrowserTests"),
            new("Control the world around a component",
                "Fake time, a fake backend per story (JSON, status codes, delays, failures), seeded fixtures, fake users, JS-interop mocks and browser emulation: deterministic in memory, under Server and in WebAssembly.",
                [new("Delayed response on fake time", "patterns-sign-up-form--successful-sign-up", Play: true), new("500, then retry", "data-people-directory--server-error-with-retry", Play: true), new("Fake users", "account-user-badge--administrator")],
                [Stories + "SignUpFormStories.cs", Stories + "PeopleDirectoryStories.cs"]),
            new("Run from the browser, run only what changed",
                "Start in-memory runs from the hub and watch them live. Impact analysis maps changed files to the stories they affect, with the reason for each; the shard planner splits the browser matrix by measured duration.",
                [TourLink.Hub("Run stories", "live"), TourLink.Hub("Impact & sharding", "impact?files=demo%2FBlazorTestPlatform.Demo.UI%2FButton.razor")],
                [Hub + "HubRunner.cs", "src/BlazorTestPlatform.Core/Matrix/ShardPlan.cs"], "ImpactAnalysisTests"),
        ]),

        new("review", "3", "Review", "Decisions that need a person, made in the browser and kept in git: visual changes accepted or rejected, accessibility debt separated from regressions, manual WCAG reviews that go stale when markup changes.",
        [
            new("Visual review",
                "Every story is a snapshot per viewport, theme and browser. Changes are reviewed side by side, with a slider, as an overlay, flickering or as the diff, and accepted or rejected one by one or as a group of identical changes.",
                [TourLink.Hub("Visual review", "visual"), TourLink.Hub("Baselines", "visual?tab=baselines"), new("A masked value", "display-runtime-badge--masked-value")],
                [Hub + "Components/Pages/Visual.razor", Tests + "__snapshots__/"], "VisualTests"),
            new("Accessibility with a baseline",
                "axe-core in every browser run and 37 markup rules in bUnit. New violations are told apart from known debt and fixes; waivers need a written reason; axe's undecided checks queue for a person.",
                [TourLink.Hub("Accessibility", "accessibility"), new("A waived rule", "display-badge--custom-brand-color"), new("Dialog tree snapshot", "overlays-dialog--destructive", Play: true)],
                [Stories + "BadgeStories.cs", Reporting + "RunHistory.cs"]),
            new("Coverage and traceability",
                "Which component states have a story, and which WCAG 2.2 criteria each component was verified against: a traceability matrix where a review goes stale when the component's accessibility structure changes.",
                [TourLink.Hub("Coverage & traceability", "coverage"), TourLink.Hub("Manual reviews", "coverage#reviews")],
                [Tests + "StoryCoverageTests.cs", Tests + "a11y-reviews/"], "StoryCoverageTests, AccessibilityReviewTests"),
        ]),

        new("analyze", "4", "Analyze", "Every run goes into an archive, so the platform can tell a regression from noise: trends, run comparisons, stability per result, failures grouped by cause and performance over time.",
        [
            new("Dashboard and run history",
                "Pass rate and duration trends, outcomes per run, the render-mode × driver matrix, and what waits for a decision. Compare any two runs: new failures, still failing, fixed, new flaky, slower.",
                [TourLink.Hub("Dashboard"), TourLink.Hub("Runs", "runs"), TourLink.Hub("Compare runs", "compare")],
                [Reporting + "RunArchive.cs"], "HistoryTests"),
            new("Flaky tests and quarantine",
                "Healthy, Failing, Flaky, Broken or Fixed, with a flake rate and its 95% interval. Quarantine keeps a story running and reporting while its failures stop failing the build; entries live in quarantine.json.",
                [TourLink.Hub("Flaky & quarantine", "flaky"), TourLink.Hub("A broken story", "result?key=demo-chaos--report-export%7Cbunit")],
                [Tests + "quarantine.json", "src/BlazorTestPlatform.Core/Results/Quarantine.cs"]),
            new("Failures grouped by cause",
                "One normalized signature per error, so one broken selector shows up as one problem across every cell it breaks. Categories come from the evidence on each result, with a likely cause and a next step; any failure can be copied as a prompt.",
                [TourLink.Hub("Failures", "failures")],
                [Reporting + "FailureAnalysis.cs"]),
            new("Performance over time",
                "Render counts, allocations and time with budgets and baselines in memory; layout shift, LCP and DOM size in browsers; duration trends per story from the archive.",
                [TourLink.Hub("Performance", "performance"), new("Large list budgets", "data-person-list--large-list", Play: true)],
                [Stories + "PersonListStories.cs", Tests + "__baselines__/performance.json"]),
        ]),

        new("ship", "5", "Ship", "One verdict before release, and the same data in every place a team looks: CI, pull requests, code scanning, scripts and coding agents.",
        [
            new("Release readiness",
                "Every gate in one place (failures, visual changes, accessibility, manual reviews, performance, flakiness, quarantine, coverage, history) with a single verdict.",
                [TourLink.Hub("Release readiness", "readiness")],
                [Hub + "Components/Pages/Readiness.razor"]),
            new("Integrations",
                "JUnit, SARIF, a Markdown pull-request comment that updates itself, CSV, a JSON API for scripts and agents, OpenTelemetry, and btp report --archive to record CI runs.",
                [TourLink.Hub("Integrations", "integrations"), TourLink.Hub("JSON summary", "api/summary")],
                [Hub + "HubEndpoints.cs", ".github/workflows/ci.yml"]),
        ]),
    ];

    /// <summary>Every story the tour links to.</summary>
    public static IEnumerable<string> StoryIds => Sections.SelectMany(s => s.Features).SelectMany(f => f.Links).Select(l => l.StoryId).OfType<string>().Distinct();

    /// <summary>Every hub page the tour links to.</summary>
    public static IEnumerable<string> HubPages => Sections.SelectMany(s => s.Features).SelectMany(f => f.Links).Select(l => l.HubPage).OfType<string>().Distinct();

    /// <summary>Every file or folder the tour points at, relative to the repository root.</summary>
    public static IEnumerable<string> CodePaths => Sections.SelectMany(s => s.Features).SelectMany(f => f.Code).Distinct();

    /// <summary>Where a link goes.</summary>
    public static string Href(TourLink link) => link.HubPage is { } page
        ? HubUrls.Page(page)
        : BlazorTestPlatform.Workbench.WorkbenchUrls.Shell(link.StoryId, hostingModel: link.Mode, autoPlay: link.Play);
}
