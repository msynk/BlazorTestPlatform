# Interactions and assertions

Play scripts receive a `StoryContext`. The same calls run in bUnit (in memory), in Playwright (real browsers) and inside the story frame (the workbench's **Run interactions**). Locator chains, auto-waiting assertions, failure messages and the timeline are implemented once, in the core. Queries follow the same rules in every driver: bUnit resolves them with the `BlazorTestPlatform.Accessibility` engine, and both browser drivers with one script that ports it (roles, accessible names, text, visibility of hidden subtrees). Queries only look inside the story, never at the host page around it.

## Locators

Prefer queries that match how people and assistive technology find elements:

| Call | Matches |
|---|---|
| `ctx.GetByRole("button", "Save")` | Explicit or implicit ARIA role (case-insensitive), plus accessible name (substring and case-insensitive unless `exact: true`). Hidden elements are excluded. |
| `ctx.GetByLabel("Email")` | Controls labelled by `<label>`, `aria-label` or `aria-labelledby` |
| `ctx.GetByText("Saved")` | The innermost element whose text matches |
| `ctx.GetByPlaceholder(...)`, `GetByAltText`, `GetByTitle`, `GetByTestId` | The named attribute (`By.TestIdAttribute` defaults to `data-testid`) |
| `ctx.Locate(".css")` | A CSS selector (last resort) |

Locators are lazy and can be chained: `.Locate(...)`, `.GetByRole(...)`, `.GetByText(...)`, `.GetByLabel(...)`, `.GetByPlaceholder(...)`, `.GetByAltText(...)`, `.GetByTitle(...)`, `.GetByTestId(...)`, `.Nth(i)`, `.First`, `.Last`, `.Filter("has text")` and `.FilterNot(...)`.

**Role states, patterns and filters.** As Playwright's `getByRole` options, role queries filter by state, and every text
query takes a regular expression (tested against the normalized text; browsers run it as a JavaScript `RegExp`, so keep
to syntax both share, with the `IgnoreCase`, `Multiline` and `Singleline` options only):

```csharp
ctx.GetByRole("button", "Bold", pressed: true);
ctx.GetByRole("heading", level: 2);
ctx.GetByRole("tab", selected: true);            // also isChecked, expanded, disabled, includeHidden
ctx.GetByRole("row", namePattern: new Regex(@"^Order \d+"));
ctx.GetByText(new Regex("^Saved", RegexOptions.IgnoreCase));    // GetByLabel/Placeholder/AltText/Title too
rows.Filter(new Regex("1[24]$"));                 // FilterNot(Regex) too

// Rows that contain a Delete button (Playwright's filter({ has })), and rows that contain none:
var rows = ctx.GetByRole("listitem");
await rows.Filter(ctx.GetByRole("button", "Delete")).First.GetByRole("button").ClickAsync();
await ctx.Expect(rows.FilterNot(ctx.GetByRole("button"))).ToHaveCountAsync(2);
```

`isChecked` matches checkboxes, radios, switches, checkable menu items, options and tree items (not `mixed`); `pressed`
reads `aria-pressed`; `selected` reads `aria-selected` (and, for options, HTML's selectedness: the select's value, the
last option marked `selected`, else the first enabled one); `level` reads heading levels and `aria-level`. A filter's
inner locator runs inside each match, among its descendants, as in Playwright.

Only role queries skip hidden elements. The other queries also match hidden ones, as in Playwright; actions then wait for the element to become visible, and `ToBeVisibleAsync` tells the two apart.

**Strictness.** An action or a single-element assertion fails if its locator matches more than one element once the timeout expires. The error lists the matches, in the same format in every driver. Negated assertions are just as strict: `Not.ToBeDisabledAsync()`, `Not.ToBeCheckedAsync()`, `Not.ToContainTextAsync(...)` and the other single-element assertions fail when the element is missing or ambiguous, instead of passing vacuously (a typo in a locator never makes a negative check pass). Visibility assertions accept a missing element as hidden (`ToBeHiddenAsync`, `Not.ToBeVisibleAsync`) but fail on more than one match, in both polarities.

## Actions

Actions auto-wait until the locator matches exactly one visible element. Clicks, fills, typing, checks and selections also wait for it to be enabled. Content under an `inert` ancestor can't be acted on.

```csharp
await locator.ClickAsync();                      // modifiers, button, timeout, force
await locator.DoubleClickAsync();
await locator.HoverAsync();
await locator.FocusAsync(); await locator.BlurAsync();
await locator.FillAsync("text"); await locator.ClearAsync();
await locator.TypeAsync("text");                 // one key event per character
await locator.PressAsync("Enter");               // focuses the element, then presses
await locator.CheckAsync(); await locator.UncheckAsync();
await locator.SelectOptionAsync("value-or-label");
await locator.PointerDownAsync(); await locator.PointerUpAsync();
await locator.DragToAsync(target);
await locator.SetInputFilesAsync(StoryFile.FromText("notes.txt", "Hello"), StoryFile.FromPath("avatar.png"));
await locator.SetInputFilesAsync();              // clears the selection
```

**File uploads.** `SetInputFilesAsync` chooses files in an `<input type="file">` as the browser's file picker would: the
input's `change` event fires and Blazor's `InputFile` reads them (name, content type, size, content). The input may be
visually hidden behind a styled drop zone, but must be enabled, and more than one file needs `multiple`. Files are
held in memory (`StoryFile.FromText`, `FromBytes`, `FromPath`), so the same files reach every driver. bUnit hands them
to the `InputFile` component that rendered the input (a plain file input fails with an explanation); Playwright uses
`setInputFiles`, and the in-app driver a `DataTransfer`.

Keyboard actions that act on the focused element:

```csharp
await ctx.PressAsync(Keys.Enter);
await ctx.PressAsync(Keys.Chord(Keys.Shift, Keys.Tab));
await ctx.TypeAsync("hello");
await ctx.TabAsync(3); await ctx.ShiftTabAsync();
await ctx.ExpectTabOrderAsync(first, second, third);
```

## Assertions

Every element assertion retries until it passes or the timeout expires (default 5 s, set with `BTP_TIMEOUT_MS` for bUnit and Playwright runs; in-frame runs use 5 s). Retrying removes the most common cause of flaky UI tests: asserting before an asynchronous render has finished.

```csharp
await ctx.Expect(locator).ToBeVisibleAsync();      // .Not.ToBeVisibleAsync(), ToBeHiddenAsync()
await ctx.Expect(locator).ToHaveTextAsync("Saved");// exact after whitespace normalisation; Regex overload
await ctx.Expect(locator).ToContainTextAsync("save");
await ctx.Expect(locator).ToHaveValueAsync("ada@example.com");
await ctx.Expect(locator).ToHaveAttributeAsync("aria-expanded", "true");
await ctx.Expect(locator).ToHaveClassAsync("is-active");
await ctx.Expect(locator).ToBeFocusedAsync();
await ctx.Expect(locator).ToBeEnabledAsync();      // ToBeDisabledAsync, ToBeCheckedAsync, ToBeEditableAsync
await ctx.Expect(locator).ToHaveCountAsync(3);
await ctx.Expect(locator).ToHaveAccessibleNameAsync("Close dialog");
await ctx.Expect(locator).ToHaveRoleAsync("dialog");
await ctx.Expect(locator).ToHaveAccessibleDescriptionAsync("We only use it for receipts.");
await ctx.Expect(locator).ToHaveAccessibleErrorMessageAsync("Enter a complete address."); // aria-errormessage, while aria-invalid
await ctx.Expect(locator).ToBeRequiredAsync();     // required or aria-required="true"
await ctx.Expect(locator).ToBeInvalidAsync();      // aria-invalid (Blazor's inputs set it on validation errors)
await ctx.Expect(locator).ToBePartiallyCheckedAsync(); // aria-checked="mixed"
await ctx.Expect(locator).ToBeEmptyAsync();        // no value (controls) or no text
await ctx.Expect(locator).ToHaveIdAsync("email");
await ctx.Expect(locator).ToContainClassesAsync("field", "field--error");
await ctx.Expect(listItems).ToHaveTextAsync(["Alpha", "Beta", "Gamma"]); // every match, in order
// Regex overloads: ToHaveValueAsync, ToHaveAttributeAsync(name, regex), ToHaveClassAsync, ToHaveAccessibleNameAsync,
// ToHaveAccessibleDescriptionAsync.
```

The accessible description and error message are computed by both engines (C# for bUnit, its JavaScript port for the
browser drivers) and compared in `DriverParityTests`.

**Soft assertions.** `ctx.ExpectSoft(locator)` records a failure and lets the play script continue, as Playwright's
`expect.soft`; any assertion can be made soft with `await ctx.SoftAsync(() => ...)`. The story still fails when play
ends, with every soft failure listed (`ctx.SoftFailures` holds them). If a hard failure stops the script first, it is
reported as it is, and the soft failures stay visible as failed steps in the timeline.

**Retrying blocks, polling and conditions that must hold.**

```csharp
// Playwright's toPass: retry the whole block until it passes (inner assertions check once per attempt).
await ctx.ExpectEventuallyAsync(async () =>
{
    await ctx.GetByRole("button", "Refresh").ClickAsync();
    await ctx.Expect(ctx.GetByText("Ready")).ToBeVisibleAsync();
}, TimeSpan.FromSeconds(10));

// Playwright's expect.poll: wait for state the DOM does not show.
await ctx.ExpectPollAsync(() => Task.FromResult(cart.Count), n => n == 3, "the cart to hold 3 items");

// Kotest's continually: the tooltip stays closed for 500 ms (advanced on the fake clock when the story uses one).
await ctx.ExpectToRemainAsync(() => ctx.Expect(tooltip).ToBeHiddenAsync(), TimeSpan.FromMilliseconds(500));
```

Failure messages show the expected value, the last observed value, the driver and render mode, the matched elements and, for focus assertions, the element that actually had focus. A failed story's message ends with the last steps of its timeline.

Side effects have auto-waiting assertions too. They work under every driver. The negative forms (`NotToHaveBeenCalledAsync`, `NotToHaveBeenMadeAsync`), and `times: 0`, wait for the story to be idle and then check once:

```csharp
await ctx.ExpectAction("OnSave").ToHaveBeenCalledAsync(times: 1);                // callbacks
await ctx.ExpectRequest("POST", "api/people").ToHaveBeenMadeAsync();             // MockHttp requests
await ctx.ExpectJSCall("localStorage.setItem").ToHaveBeenLastCalledWithAsync("consent", "accepted"); // JS interop (stories with MockJS)
```

Navigations have them too. A component's `NavigationManager.NavigateTo` (and, in browsers, intercepted link clicks) is
recorded in every driver; in browsers the workbench stops navigations that would leave the story frame, so the story
stays on screen. Patterns follow the request rules (relative paths with `*`, the query compared only when the pattern
has one, absolute URLs) or are regular expressions over `/path?query#fragment`:

```csharp
await ctx.GetByRole("button", "Open order 42").ClickAsync();
await ctx.ExpectNavigation("/orders/42").ToHaveNavigatedAsync(times: 1);
await ctx.ExpectNavigation("/orders/*").ToBeTheLastNavigationAsync();
await ctx.ExpectNavigation(new Regex("^/checkout")).NotToHaveNavigatedAsync();
var all = await ctx.GetNavigationsAsync();     // NavigationRecord: Uri, RelativeUri, ReplaceHistoryEntry, ForceLoad (bUnit)
```

**JavaScript calling .NET.** Components that wrap a JavaScript widget or observer pass a `DotNetObjectReference` to
JavaScript and expect `[JSInvokable]` callbacks. `ctx.InvokeDotNetAsync` plays the JavaScript side: it finds the latest
recorded call to the identifier that passed a reference and calls the method (its `[JSInvokable]` identifier or name)
on the story's renderer, then waits for the story to settle. Mock the call with `MockJS(...)` so it is recorded (and
`.ApplyInBrowsers()` when there is no real JavaScript behind it):

```csharp
stories.Add("Resized", s => s
    .MockJS(js => js.SetupVoid("resizeObserver.observe").ApplyInBrowsers())
    .Play(async ctx =>
    {
        await ctx.InvokeDotNetAsync("resizeObserver.observe", "OnResize", 320, 200);
        await ctx.Expect(ctx.GetByRole("status")).ToHaveTextAsync("320 × 200");
    }));
```

It works in bUnit, Playwright (through the story frame bridge) and the in-app driver. Arguments and results travel as
JSON: primitives, enums, GUIDs and dates always, other types where reflection-based JSON is enabled.

JS-call arguments are compared after `JSCallLog.Format`, the same under every driver: strings as they are, numbers and dates in the invariant culture, booleans as `true`/`false`, collections as `[1, 2, 3]`, dictionaries as `{"key": value}` (strings inside collections are quoted), and other objects as their camelCase JSON, which is what JS interop sends. In trimmed WebAssembly apps, where reflection-based JSON is unavailable, other objects fall back to `ToString()`.
### Styles and layout

`ToHaveCssAsync(property, value)` compares an element's computed style as the browser reports it (`rgb(...)` for colors, pixels for lengths; kebab-case or camelCase names; `pseudoElement: "::before"` reads a pseudo-element), and `ToBeInViewportAsync(ratio)` checks that the element intersects the viewport, or that at least `ratio` of it does (as Playwright's `toHaveCSS` and `toBeInViewport`). Both are strict, auto-waiting, negatable and soft like the other assertions. They need a driver that applies CSS and lays pages out (`DriverCapabilities.Css`, `Layout`): Playwright and the in-app driver evaluate them, and bUnit records them in the timeline as skipped while the play script goes on, so one script keeps working everywhere.

```csharp
await ctx.Expect(ctx.GetByRole("tabpanel")).ToHaveCssAsync("paddingTop", "16px");
await ctx.Expect(ctx.GetByRole("dialog")).ToBeInViewportAsync(ratio: 1);
```

### Screenshots and accessibility in the middle of play

`ctx.ExpectScreenshotAsync("menu-open")` compares a screenshot of the current state with a committed baseline, as Playwright's `toHaveScreenshot('name')`: the story root, an element (`target: ctx.GetByRole("menu")`) or the whole page (`fullPage: true`, for overlays rendered outside the story). Baselines sit next to the story's own snapshot (`{story-id}/{viewport}.{theme}.{browser}-{platform}.menu-open.png`), use the story's tolerances and masks, are written when missing locally and approved with `btp approve` or in the hub. Names are unique within a story; `initial` is reserved. bUnit and the in-app driver record the check as skipped.

`ctx.ExpectAccessibleAsync("menu open")` scans the story now and fails when the accessibility policy fails, with the story's disabled rules and waivers: the states play passes through (an open menu, a validation error) that the scan after play never sees. bUnit applies its markup rules, browsers run axe-core, and the in-app driver records the check as skipped.

`CheckAccessibilityAfterActions()` on a story runs that check after every element action (click, fill, hover...) and key
press of its play function, as Compose's `enableAccessibilityChecks()`, and names the action that broke the policy:
`expect accessible (after click role=button[name="Show picture"i])`. In browsers each check is an axe run, so turn it on
for the stories whose intermediate states matter (dialogs, menus, wizards); `ctx.CheckAccessibilityAfterActions = false`
stops it for the rest of play. `TabAsync(n)`, `ShiftTabAsync(n)` and `ExpectTabOrderAsync` check once, after their last
key press. Combined with `ExploreAsync`, accessibility becomes an invariant of every explored step.

### Markup snapshots

`await ctx.ExpectMarkupSnapshotAsync("after-add", ctx.GetByText("Count:"))` compares normalized, scrubbed markup with a
committed `.markup.html` file in bUnit ([quality gates](quality-gates.md#markup-snapshots)); other drivers record the
check as skipped.

### Attachments and step timeouts

`ctx.Attach("cart.json", json)` (text or bytes) attaches a file to the run, as Playwright's `testInfo.attach`: runners write it with the run's artifacts (`TestResults/btp/artifacts/<key>/attachments/`), reports link it, and the xUnit, NUnit, MSTest and TUnit packages attach it to the test result whatever the outcome. `ctx.StepAsync("load the picture", body, TimeSpan.FromSeconds(2))` fails at that step, with its name, when it takes longer than its own timeout.

## Grouping and timelines

`await ctx.StepAsync("open the menu", async () => { ... })` nests actions on the timeline. Every action, assertion, wait and check is recorded with its start offset and duration. The timeline appears in results, in the HTML report and in failure messages.

## How the bUnit driver emulates the browser

bUnit dispatches raw events. On top of that, the driver adds the default behaviour a browser provides:

* Clicks send pointer and mouse events and move focus to the nearest focusable ancestor. Checkboxes and radios toggle, label clicks reach the labelled control, and submit buttons submit their form. `aria-disabled` elements still receive clicks; natively disabled ones (including everything in a disabled `<fieldset>` outside its first `<legend>`) don't.
* Tab and Shift+Tab follow the real tab order: positive `tabindex` first, `tabindex="-1"`, disabled, `inert` and hidden elements skipped, one stop per radio group, and only the first `<summary>` of a `<details>`. `@onkeydown:preventDefault` is honoured, and a keydown handler that moves focus redirects the rest of the key press, as in a browser.
* Enter activates buttons and links. In other fields it never toggles anything: it performs HTML implicit submission, firing `change` for an edited field and then clicking the form's first submit button (a form without one submits only when it has a single text field). Space, on key up, activates buttons and toggles checkboxes and radios, but never follows links.
* `FillAsync` fires `input`, and `change` fires on blur, exactly as in a browser. Code that only reacts to `change` therefore behaves the same in every driver.
* What the user typed or checked stays in the control across renders, as in a browser, until the component renders a different value for it: an input that only handles `@oninput` (no binding) keeps its text, and a two-way binding that resets the value replaces it. Checking a radio unchecks the other radios of its group. Enter fires `keypress` as well as `keydown`/`keyup`.
* A component that throws after an `await` (outside an error boundary) fails the story, as the Blazor error UI would in a browser.
* An action waits for the event handlers it triggers, but not for ever: a handler still running after 1 s (for example one awaiting a delayed `MockHttp` response on the story's fake clock) keeps running while the play script moves on, as in a browser. Assertions wait for its effects, and `ctx.AdvanceTimeAsync` releases it.
* `ElementReference.FocusAsync()` calls made by components are captured and fire blur, focusout, focus and focusin (with change-on-blur for an edited field), so focus management in roving-tabindex widgets and dialogs can be tested in memory.
* Focus, hover and `@ref` targets follow the element, not its position: inserting a sibling before the focused element keeps focus on it. Tab continues from the last clicked element.
* Arrow keys move within a radio group (checking the next radio, wrapping around); activating the first `<summary>` toggles its `<details>` and fires `toggle`; Enter in a textarea or contenteditable inserts a line break; `@onclick:preventDefault` on a checkbox or radio reverts the toggle.
* Mouse events carry the `buttons` bitmask of the pressed button (left 1, right 2, middle 4). A drop only happens when the target's `dragover` handler prevents the default, as in HTML drag and drop; otherwise the target gets `dragleave`. `FillAsync` waits while a field is `readonly`.

**Stylesheets.** By default, visibility in bUnit comes from attributes and inline styles. Point the driver at your CSS, including Blazor CSS-isolation bundles, and the `display`/`visibility` rules apply too. This uses a real cascade with specificity (per selector of a list), `!important`, cascade layers (`@layer`, including the Tailwind v4 layer order: unlayered rules win over layered ones, and the order reverses for `!important`), inline styles, inherited `visibility` and the `inherit`/`initial`/`unset`/`revert` keywords. An author `display` rule overrides the `hidden` attribute, as in browsers, and `@container` queries are evaluated against the viewport. Elements hidden by a class are then hidden from role queries, visibility assertions, Tab order and accessibility rules:

```json
// btp.json
{ "bunit": { "stylesheets": ["../MyDesignSystem/wwwroot/app.css", "../MyDesignSystem/obj/Debug/net10.0/scopedcss/bundle/MyDesignSystem.styles.css"] } }
```

You can also add CSS in code: `new BunitStoryOptions().AddStylesheetFile(path)` or `options.Stylesheets.Add(css)`.

`@media` blocks are evaluated against the viewport the in-memory driver pretends to have. The default is `Viewport.Desktop` (1280×800), as in browser runs. Set it with `BunitStoryOptions.Viewport`, `bunit.viewport` in `btp.json`, or `BTP_BUNIT_VIEWPORT` (a preset name or `WIDTHxHEIGHT`). `BunitStoryOptions.Theme` drives `prefers-color-scheme` and applies the theme's decorator. The evaluator supports width and height (`min-`/`max-` and the range syntax, e.g. `(600px <= width < 900px)`), orientation, `prefers-color-scheme`, `prefers-reduced-motion` (`reduce`, as browser runs emulate, also as a bare `(prefers-reduced-motion)`), `hover`, `pointer`, media types, `not`/`only`, lists, nested `@media` and `@supports`:

```csharp
var phone = new BunitStoryOptions { Viewport = Viewport.Mobile };
await using var session = await BunitStoryRunner.RenderAsync(story, phone);
await session.StoryContext.Expect(session.StoryContext.GetByText("Analytics cookies...")).ToBeHiddenAsync();
```

Limits remain: there is no layout and no real JavaScript (mock it with `MockJS`; see [test data](test-data.md#javascript-interop-mocks)). Without layout, bUnit can't tell that an element has no size, which browsers treat as not visible. When a story depends on layout or a real browser, declare it, and the story is reported as *skipped*, with the reason, by every driver that cannot do it. See [driver capabilities](#driver-capabilities).

## Driver capabilities

Drivers differ in what they can really do. Each one states its capabilities, and nothing is silently substituted: a story that needs a capability a driver lacks is skipped there, with an explanation, never run against an approximation and reported as passed.

| Capability | bUnit | Playwright (Static SSR) | Playwright (Server, WebAssembly, Auto) | In-app (workbench) |
|---|---|---|---|---|
| `RealBrowser` | no | yes | yes | yes |
| `Layout` (sizes, scrolling, hit testing) | no | yes | yes | yes |
| `Screenshots` | no | yes | yes | no |
| `NativeFocus` | emulated (not claimed) | yes | yes | yes |
| `JavaScript` | mocked (not claimed) | yes | yes | yes |
| `Css` | `display`/`visibility` cascade and `@media` only (not claimed) | yes | yes | yes |
| `Network` | no | yes | yes | no |
| `FakeTime` | with `UseFakeTime` | no (not interactive) | with `UseFakeTime` | with `UseFakeTime` |
| `BrowserEmulation` (geolocation, permissions, latency, offline) | no | yes | yes | no |
| `CircuitControl` (`PauseAndResumeCircuitAsync`) | no | no | Server only | no |

**Declare requirements on the story** when the whole story needs them. Runners check them before rendering, and test data sources can leave the story out entirely:

```csharp
stories.Add("Popover placement", s => s
    .Requires(DriverCapabilities.Layout, "measuring the popover position")
    .Play(async ctx => { /* ... */ }));

// Only the stories the in-memory driver can run:
public static TheoryData<string> Stories => new(Catalog.Supporting(DriverCapabilities.None).Ids);
```

`Story.CanRunOn(capabilities)` and `Story.UnsupportedReason(capabilities, driverName)` answer the same question for your own runners; `BunitStoryDriver.CapabilitiesFor(story)` and `PlaywrightStoryDriver.CapabilitiesFor(mode, fakeTime)` give a driver's capabilities without starting it. A skipped result carries a reason such as `Driver 'bunit' lacks capabilities the story requires: Layout (measuring the popover position).`

**Branch inside a play script** when only one step needs a capability:

```csharp
ctx.Require(DriverCapabilities.Layout, "measuring the popover position"); // skips the rest of the story here
if (ctx.Supports(DriverCapabilities.RealBrowser)) { ... }                // optional extra checks
```

A story that calls `ctx.Require` mid-play is still reported as skipped, but it has already rendered and run the earlier steps; prefer the declaration when the requirement covers the whole story.

## Browser emulation

Real browsers can emulate browser APIs and network conditions. A story declares them, and drivers that cannot emulate
(bUnit, the in-app driver) report it as skipped:

```csharp
stories.Add("Nearby stores", s => s
    .Geolocation(52.37, 4.89)                      // navigator.geolocation, with the permission granted
    .GrantPermissions("notifications")             // Permissions API names; browsers support different sets
    .NetworkLatency(TimeSpan.FromMilliseconds(400)) // added to every HTTP request (not to an open WebSocket)
    .Play(async ctx =>
    {
        await ctx.GetByRole("button", "Find stores").ClickAsync();
        await ctx.SetGeolocationAsync(new GeoPosition(-33.87, 151.21)); // move while the story runs
        await ctx.SetOfflineAsync();                                    // navigator.onLine false, requests fail
        await ctx.SetOfflineAsync(false);
    }));
```

`Emulate(e => e with { ... })` sets the same options as a `BrowserEmulation` record. Offline mode is switched from play
because the page must load first; under Interactive Server it also cuts the circuit, so test offline behaviour under
WebAssembly. A story that only calls `ctx.SetOfflineAsync` should still declare
`.Requires(DriverCapabilities.BrowserEmulation, "goes offline")` so other drivers skip it up front.

## Accessibility tree snapshots

`ctx.ExpectAriaSnapshotAsync(expected)` waits until the story's accessibility tree matches: one line per element with a
role (generic containers flattened), its accessible name and states, children indented under a line ending in `:`.

```csharp
await ctx.ExpectAriaSnapshotAsync("""
    - heading "Sign in" [level=2]
    - textbox "Email"
    - checkbox "Remember me" [checked=false]
    - button "Continue" [disabled]
    """);
```

States are `checked`/`checked=false`/`checked=mixed` (native and ARIA), `pressed`/`pressed=false`/`pressed=mixed` for
toggle buttons, `selected` (`aria-selected="true"`), `disabled`, `expanded`/`expanded=false` and `level=N` for
headings. Hidden and `aria-hidden` content is left out. bUnit computes the tree with the C# engine and the browser
drivers with its JavaScript port, so the same snapshot passes or fails under every driver (`DriverParityTests` and
the demo's "Accessibility tree" story hold them to it). The failure message shows the actual tree, ready to paste;
`ctx.GetAriaSnapshotAsync()` returns it, and `AriaSnapshot.Of(markup)` computes it for any markup. The workbench
inspector shows the live tree of the selected story.

**Patterns and partial trees.** A name can be a pattern, `- link /Order \d+/` (with `i`, `m` or `s` flags), matched
against the whole name. With `AriaSnapshotMatch.Contains`, as Playwright's `toMatchAriaSnapshot`, the expected tree only
has to be part of the actual one: it may start at any depth, leave out nodes (the rest keep their order), names and
states; add `- /children: equal` under a node whose children must all be listed:

```csharp
await ctx.ExpectAriaSnapshotAsync("""
    - list "Orders":
      - listitem:
        - button /^Del/
    """, match: AriaSnapshotMatch.Contains);
```

`AriaSnapshotTemplate.Matches(actual, expected, match)` applies the same rules outside a play script.

**Updating snapshots in the source.** A failed `ExpectAriaSnapshotAsync` records its call site (the compiler fills in the
file and line) and the tree the driver saw. `btp snapshots update-source` rewrites the expected raw string literal in place
with it, as Playwright's `updateSourceMethod` and insta's inline snapshots do: review the diff, rerun, commit. It leaves a
call site alone, saying why, when runs disagree on the tree (a driver or render-mode difference is a parity bug, not an
outdated snapshot), when the expected tree has name patterns or `/children` directives (a rewrite would lose them), or when
the expected text is not a literal at the call. `--dry-run` lists what would change; `--source-root` resolves the
deterministic paths (`/_/...`) of results built on CI.

## Forcing hover and focus

`locator.ForceStateAsync(PseudoState.Hover)` makes an element match `:hover` (and `:active`, `:focus`, `:focus-visible`,
`:focus-within`, `:visited`) without a pointer or keyboard, as storybook-addon-pseudo-states does: every style rule that
uses the pseudo-class gets a twin keyed on an attribute the element carries (its ancestors too for `:hover`, `:active` and
`:focus-within`, as with a real pointer). Hover and focus looks can then be checked and screenshotted deterministically.
Each call replaces the states forced before; `PseudoState.None` clears them. Inline styles and cross-origin stylesheets
are not affected. It needs CSS: bUnit skips the story, with the reason.

```csharp
var button = ctx.GetByRole("button", "Save changes");
await button.ForceStateAsync(PseudoState.Hover);
await ctx.ExpectScreenshotAsync("hover", button);
await button.ForceStateAsync(PseudoState.FocusVisible);
await ctx.Expect(button).ToHaveCssAsync("outlineStyle", "solid");
```

## Exploring sequences of actions

`ctx.ExploreAsync(name, e => ...)` explores a component as a state machine, as Hypothesis' rule-based tests and fast-check's
model-based commands do: it takes `Steps(n)` random actions (default 20) among those possible at each point (`when:`),
picked by a seed, and checks every invariant before the first step and after each one.

```csharp
await ctx.ExploreAsync("stepper", e => e
    .Action("add", c => c.GetByRole("button", "Add").ClickAsync(), code: "await ctx.GetByRole(\"button\", \"Add\").ClickAsync();")
    .Action("remove", c => c.GetByRole("button", "Remove").ClickAsync(), when: c => c.GetByRole("button", "Remove").IsEnabledAsync())
    .Invariant("count is never negative", c => c.Expect(c.GetByTestId("count")).Not.ToHaveTextAsync(new Regex("^-"), TimeSpan.Zero)));
```

A failure lists the steps that led to it, the seed that replays them (`.Seed(n)` or `BTP_EXPLORE_SEED=n`) and a play script
built from the actions' `code`, ready to paste as a regression story. The seed derives from the story's, so a run repeats
exactly; set `BTP_EXPLORE_SEED` (for example to the build number in a nightly job) to try other sequences.

The bUnit runner **shrinks** a failing sequence before reporting it: it replays shorter candidates on fresh renders (delta
debugging, `ExplorationReplay`) and reports the shortest that still fails the same way, for example
`Steps: add -> remove (shrunk from 23)`. `BunitStoryOptions.ShrinkExplorations = false` turns it off and `MaxShrinkProbes`
(default 200) bounds it. The browser runner shrinks the same way on fresh pages, on the last attempt only (retries would
find the same sequence): `BrowserRunOptions.ShrinkExplorations` and `MaxShrinkProbes` (default 40, as each probe opens a
page); a shrink also stops once the attempt's timeout has passed again, keeping the shortest sequence found. Named
screenshots are not compared while shrinking.

## Pausing and resuming the circuit

`await ctx.PauseAndResumeCircuitAsync()` releases the story's Blazor Server circuit and resumes it, as when a user returns
to a tab the browser put to sleep (.NET 10's `Blazor.pauseCircuit()` and `Blazor.resumeCircuit()`). The server persists
`[PersistentState]` properties when it pauses; on resume a new circuit renders the components again from that state, so
everything else starts over. Assert what the user sees afterwards:

```csharp
await add.ClickAsync();
await ctx.PauseAndResumeCircuitAsync();
await ctx.Expect(ctx.GetByTestId("saved")).ToHaveTextAsync("Saved: 1");     // [PersistentState]
await ctx.Expect(ctx.GetByTestId("unsaved")).ToHaveTextAsync("Unsaved: 0"); // in memory only
```

It needs `DriverCapabilities.CircuitControl`: browsers under Interactive Server. bUnit, the in-app driver and the other
render modes report the story as skipped. Recorded actions, JavaScript calls and requests start over with the new circuit.
Declare it on the story (`Requires(DriverCapabilities.CircuitControl, "...")`) so other cells skip before rendering: under
static SSR, ASP.NET Core rejects a `[PersistentState]` component that has no render mode ("The registered callback
PersistProperty must be associated with a component or define an explicit render mode type").

## Component harnesses

A component harness is the test API a design system ships with a component, as Angular CDK's harnesses: tests in every
app that uses the component drive it through methods (`ConfirmAsync`, `PickAsync(date)`) instead of its markup, so the
markup can change without breaking them. A harness names how to find the component's root and builds on locators, so
one harness works in bUnit, Playwright and the in-app driver:

```csharp
public sealed class DialogHarness : ComponentHarness
{
    protected override By HostQuery => By.Role("dialog");
    public Task ConfirmAsync() => Locate(By.Css(".c-dialog__actions button")).Last.ClickAsync();
    public Task ExpectClosedAsync() => Context.Expect(Host).ToHaveCountAsync(0);
}

var dialog = ctx.Harness<DialogHarness>();          // inside the story; within: and nth: narrow it
await dialog.ConfirmAsync();
var counters = await ctx.HarnessesAsync<CounterHarness>(); // one per instance on screen, in document order
```

Lookups are lazy and strict, like locators: an action waits for the component and fails when several match (pass
`nth`). Harnesses compose: inside a harness, `Harness<FieldHarness>()` finds a component inside its host. Put a design
system's harnesses in a package next to its stories, so consuming apps get both. The demo's `Overlays/Dialog/Confirm`
uses `DialogHarness`.

## Three drivers, one script

| Driver | Where the play function runs | Used by |
|---|---|---|
| bUnit (`BunitStoryDriver`) | Test process, in memory | `BunitStoryRunner` |
| Playwright (`PlaywrightStoryDriver`) | Test process, driving a real browser: elements are found with the shared locator script, then acted on with real Playwright input | `BrowserStoryRunner` |
| In-app (`InAppStoryDriver`) | Inside the story frame, in the Server circuit or the WebAssembly runtime, driving the real DOM | The workbench **Run interactions** button, and `StoryPageOptions.AutoPlay` |

The in-app driver uses the browser's own activation behaviour (`click()`, focus, form submission) and honours `@onkeydown:preventDefault`. It emulates what scripts cannot trigger with trusted input: Tab movement, typing and Backspace, change-on-blur, Enter and Space activation, and the event sequences of hover, pointer and drag actions, following the same rules as bUnit. The demo's tests run every play script through all three drivers, and `DriverParityTests` compares roles, names, text and state across them element by element.

## Fake time

```csharp
stories.Add("Expires", s => s
    .UseFakeTime(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero))
    .Play(async ctx =>
    {
        await ctx.AdvanceTimeAsync(TimeSpan.FromMinutes(2));
        await ctx.Expect(ctx.GetByRole("timer")).ToHaveTextAsync("Session expired");
    }));
```

Components must inject `TimeProvider`. In browsers, the clock is advanced through the story frame bridge, under both Server and WebAssembly.

Advancing stops at every timer occurrence on the way and lets the story settle in between, so a one-minute
`Task.Delay` loop advanced by three minutes runs three times, as with a real clock and Playwright's `clock.runFor`. The
story's `TimeProvider` is a `StoryClock`, which also moves the wall clock alone, as Playwright's `clock` API does:

| Call | Effect |
|---|---|
| `ctx.AdvanceTimeAsync(TimeSpan)` | Runs time forward, firing every timer due (`clock.runFor`) |
| `ctx.AdvanceTimeToAsync(DateTimeOffset)` | Runs time to an instant (`clock.pauseAt`; the fake clock is paused between steps anyway) |
| `ctx.SetSystemTimeAsync(DateTimeOffset)` | Moves the wall clock either way without firing timers (`clock.setSystemTime`): `GetUtcNow()` and local time change, delays, timers and stopwatches do not |
| `ctx.GetTimeAsync()` | The clock's current wall-clock time |

```csharp
await ctx.SetSystemTimeAsync(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero)); // the next morning, no timer fired
await ctx.AdvanceTimeToAsync(new DateTimeOffset(2026, 3, 2, 8, 3, 0, TimeSpan.Zero)); // three minutes of a one-minute timer
```

They work in bUnit, Playwright (every interactive mode) and the in-app driver. Only timers created through the story's
`TimeProvider` are stepped through; a timer created on a `FakeTimeProvider` resolved directly fires once per advance.
