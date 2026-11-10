using BlazorTestPlatform;

namespace BlazorTestPlatform.Demo.Workbench;

/// <summary>A story to open in the workbench, optionally in a render mode and with its interactions running.</summary>
public sealed record TourLink(string Label, string StoryId, HostingModel? Mode = null, bool Play = false);

/// <summary>One feature: what it does, where to see it, the code that declares it and the test that runs it.</summary>
public sealed record TourFeature(string Title, string Text, IReadOnlyList<TourLink> Links, IReadOnlyList<string> Code, string? Test = null);

/// <summary>A group of features on the tour page.</summary>
public sealed record TourSection(string Id, string Title, string Lead, IReadOnlyList<TourFeature> Features);

/// <summary>
/// The guided tour on the demo's home page. Every link points at a real story: <c>TourTests</c> fails when a story is
/// renamed and the tour isn't.
/// </summary>
public static class Tour
{
    private const string Stories = "demo/BlazorTestPlatform.Demo.UI.Stories/";
    private const string Tests = "demo/BlazorTestPlatform.Demo.UI.Tests/";

    public static IReadOnlyList<TourSection> Sections { get; } =
    [
        new("write", "Write a story once", "A story is one named state of a component, declared in C#. Everything else on this page is generated from stories.",
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
            new("Compositions",
                "Composition stories render several components together. A kitchen-sink page is one snapshot per theme and viewport that catches a change to shared design tokens.",
                [new("Kitchen sink", "overview-kitchen-sink--design-system")],
                [Stories + "KitchenSinkStories.cs"]),
        ]),

        new("interact", "Interact like a person", "Play scripts find elements the way people and screen readers do, then click, type and use the keyboard. Assertions wait, so async renders don't make them flaky.",
        [
            new("Keyboard and focus",
                "Tab order, arrow keys, Escape and focus moved by the component itself (ElementReference.FocusAsync) are checked the same way in memory and in browsers.",
                [new("Tabs: arrow keys", "navigation-tabs--keyboard-navigation", Play: true), new("Dialog: Escape returns focus", "overlays-dialog--keyboard", Play: true)],
                [Stories + "TabsStories.cs", Stories + "DialogStories.cs"]),
            new("Forms and async work",
                "Fill, press Enter, click: implicit submission, change-on-blur and disabled controls behave like a browser's, in bUnit too.",
                [new("Sign-up validation", "patterns-sign-up-form--validation-errors", Play: true), new("Add a task", "data-todo-list--add-a-task", Play: true)],
                [Stories + "SignUpFormStories.cs", Stories + "TodoListStories.cs"]),
            new("Accessibility tree snapshots",
                "ExpectAriaSnapshotAsync pins roles, names and states as a tree: what a screen reader gets. The inspector shows the live tree of any story.",
                [new("Dialog tree", "overlays-dialog--destructive", Play: true), new("Landmarks and names", "testing-driver-parity--accessibility-tree", Play: true)],
                [Stories + "DialogStories.cs", Stories + "DriverParityStories.cs"]),
            new("One script, three drivers",
                "The same play script runs in bUnit (milliseconds), in Playwright (real browsers) and inside the workbench frame (Run interactions). Parity tests hold the three to the same results.",
                [new("Keyboard parity", "testing-driver-parity--keyboard", Play: true)],
                [Stories + "DriverParityStories.cs"], "DriverParityTests, InAppPlayTests"),
        ]),

        new("data", "Control the world around a component", "Every source of variation comes from the story: seeded data, fake time, fake users, a fake backend, mocked JavaScript and emulated browser APIs. In memory, under Server and in WebAssembly.",
        [
            new("Fake time",
                "Components that inject TimeProvider run on a clock the play script moves. A two-minute countdown, or a delayed API response, takes no time and never flakes.",
                [new("Session expires", "feedback-session-timer--expires", Play: true), new("Delayed sign-up response", "patterns-sign-up-form--successful-sign-up", Play: true)],
                [Stories + "AccountStories.cs", Stories + "SignUpFormStories.cs"]),
            new("A fake backend per story",
                "MockHttp answers HttpClient and IHttpClientFactory requests: JSON, status codes, delays, failures, requests that never finish. ExpectRequest checks what was sent.",
                [new("500, then retry", "data-people-directory--server-error-with-retry", Play: true), new("Network failure", "patterns-sign-up-form--server-unreachable", Play: true), new("409 conflict", "patterns-sign-up-form--email-already-registered", Play: true)],
                [Stories + "PeopleDirectoryStories.cs", Stories + "SignUpFormStories.cs"]),
            new("Fixtures and seeded data",
                "StoryFixtures replace the app's services per story; FakeData produces names, emails and sentences from the story's seed, identical on every machine.",
                [new("Seeded todo list", "data-todo-list--seeded"), new("Seeded people", "data-person-list--seeded-people")],
                [Stories + "TodoListStories.cs", Stories + "PersonListStories.cs"]),
            new("Fake users",
                "Anonymous, signed in, with roles and claims, or still authenticating. AuthorizeView and policies evaluate them with the real authorization services.",
                [new("Administrator", "account-user-badge--administrator"), new("Signing in", "account-user-badge--signing-in")],
                [Stories + "AccountStories.cs"]),
            new("JavaScript interop mocks",
                "MockJS answers JS calls (here localStorage) in bUnit and, optionally, in browsers; ExpectJSCall checks what the component called.",
                [new("Cookie consent", "feedback-cookie-consent--first-visit", Play: true)],
                [Stories + "CookieConsentStories.cs"]),
            new("Browser emulation",
                "Geolocation, permissions, network latency and offline mode, emulated by Playwright. Drivers that can't emulate report the story as skipped, with the reason, instead of faking a pass.",
                [new("Geolocation", "testing-browser-emulation--geolocation")],
                [Stories + "BrowserEmulationStories.cs"], "CrossHostingTests"),
        ]),

        new("gates", "Quality gates", "Visual baselines, accessibility rules and waivers, render budgets and state coverage are data in the repository, reviewed like code.",
        [
            new("Visual regression",
                "Every story is a snapshot per viewport, theme and browser, compared with a perceptual diff. Masks hide what legitimately differs; full-page captures include fixed overlays.",
                [new("Masked value", "display-runtime-badge--masked-value"), new("Full-page dialog", "overlays-dialog--open", Play: true)],
                [Stories + "RuntimeBadgeStories.cs", Tests + "__snapshots__/"], "VisualTests, CrossBrowserTests"),
            new("Accessibility",
                "axe-core in every browser run and 37 markup rules in bUnit. A rule can be waived only with a written justification, shown here and in the report. Manual WCAG reviews go stale when the component's structure changes.",
                [new("Waived rule", "display-badge--custom-brand-color"), new("Manual review", "inputs-button--primary")],
                [Stories + "BadgeStories.cs", Tests + "a11y-reviews/"], "AccessibilityReviewTests"),
            new("Render performance",
                "Render counts, allocations and time measured in bUnit, with budgets that fail at once and committed baselines that catch drift. Browser runs add layout shift and DOM-size budgets.",
                [new("Large list budgets", "data-person-list--large-list", Play: true), new("One render per click", "inputs-button--click-raises-onclick", Play: true)],
                [Stories + "PersonListStories.cs", Tests + "__baselines__/performance.json"], "ComponentStoryTests, BrowserMetricsTests"),
            new("Story state coverage",
                "Every bool and enum value of every component must appear in some story; a state no story renders has no snapshot, scan or test. See Quality gates under any story.",
                [new("Alert severities", "feedback-alert--severity-info")],
                [Tests + "StoryCoverageTests.cs"], "StoryCoverageTests"),
        ]),

        new("hosting", "Every render mode, every browser", "One workbench host serves each story under static SSR, Interactive Server, WebAssembly and Auto. Hydration, the renderer that actually ran and console errors are all checked.",
        [
            new("The same story, four render modes",
                "Open the runtime badge in each mode: the value changes, the snapshot doesn't (it is masked), and the play script checks the renderer that ran.",
                [
                    new("Static SSR", "display-runtime-badge--masked-value", HostingModel.StaticServer),
                    new("Server", "display-runtime-badge--masked-value", HostingModel.InteractiveServer, Play: true),
                    new("WebAssembly", "display-runtime-badge--masked-value", HostingModel.InteractiveWebAssembly, Play: true),
                    new("Auto", "display-runtime-badge--masked-value", HostingModel.InteractiveAuto, Play: true),
                ],
                [Tests + "BrowserMatrixTests.cs"], "CrossHostingTests"),
            new("Chromium, Firefox and WebKit",
                "Play scripts, snapshots and axe scans run in all three engines, against baselines rendered in the Playwright container so CI and laptops agree.",
                [new("Tabs", "navigation-tabs--default")],
                [Tests + "BrowserMatrixTests.cs", "scripts/visual-tests.sh"], "CrossBrowserTests"),
        ]),
    ];

    /// <summary>Every story the tour links to.</summary>
    public static IEnumerable<string> StoryIds => Sections.SelectMany(s => s.Features).SelectMany(f => f.Links).Select(l => l.StoryId).Distinct();

    /// <summary>Every file or folder the tour points at, relative to the repository root.</summary>
    public static IEnumerable<string> CodePaths => Sections.SelectMany(s => s.Features).SelectMany(f => f.Code).Distinct();
}
