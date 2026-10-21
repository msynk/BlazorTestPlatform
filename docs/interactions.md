# Interactions and assertions

Play scripts receive a `StoryContext`. The same calls run in bUnit (in memory) and in Playwright (real browsers). Locator semantics, auto-waiting, strictness and the timeline are implemented once, in the core, so both drivers behave alike.

## Locators

Prefer queries that match how people and assistive technology find elements:

| Call | Matches |
|---|---|
| `ctx.GetByRole("button", "Save")` | Explicit or implicit ARIA role, plus accessible name (substring and case-insensitive unless `exact: true`). Hidden elements are excluded. |
| `ctx.GetByLabel("Email")` | Controls labelled by `<label>`, `aria-label` or `aria-labelledby` |
| `ctx.GetByText("Saved")` | The innermost element whose text matches |
| `ctx.GetByPlaceholder(...)`, `GetByAltText`, `GetByTitle`, `GetByTestId` | The named attribute (`By.TestIdAttribute` defaults to `data-testid`) |
| `ctx.Locate(".css")` | A CSS selector (last resort) |

Locators are lazy and can be chained: `.Locate(...)`, `.GetByRole(...)`, `.Nth(i)`, `.First`, `.Last`, `.Filter("has text")` and `.FilterNot(...)`.

**Strictness.** An action or a single-element assertion fails if its locator matches more than one element. The error lists the matches.

## Actions

Actions auto-wait for exactly one visible, enabled element:

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
```

Keyboard actions that act on the focused element:

```csharp
await ctx.PressAsync(Keys.Enter);
await ctx.PressAsync(Keys.Chord(Keys.Shift, Keys.Tab));
await ctx.TypeAsync("hello");
await ctx.TabAsync(3); await ctx.ShiftTabAsync();
await ctx.ExpectTabOrderAsync(first, second, third);
```

## Assertions

Every assertion retries until it passes or the timeout expires (default 5 s, set with `BTP_TIMEOUT_MS`). Retrying removes the most common cause of flaky UI tests: asserting before an asynchronous render has finished.

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
```

Failure messages show the expected value, the last observed value, the driver and render mode, the matched elements and, for focus assertions, the element that actually had focus.

Side effects have auto-waiting assertions too. They work under every driver:

```csharp
await ctx.ExpectAction("OnSave").ToHaveBeenCalledAsync(times: 1);                // callbacks
await ctx.ExpectRequest("POST", "api/people").ToHaveBeenMadeAsync();             // MockHttp requests
await ctx.ExpectJSCall("localStorage.setItem").ToHaveBeenLastCalledWithAsync("consent", "accepted"); // JS interop (stories with MockJS)
```

## Grouping and timelines

`await ctx.StepAsync("open the menu", async () => { ... })` nests actions on the timeline. Every action, assertion, wait and check is recorded with its start offset and duration. The timeline appears in results, in the HTML report and in failure messages.

## How the bUnit driver emulates the browser

bUnit dispatches raw events. On top of that, the driver adds the default behaviour a browser provides:

* Clicks send pointer and mouse events and move focus to the nearest focusable ancestor. Checkboxes and radios toggle, label clicks reach the labelled control, and submit buttons submit their form.
* Tab and Shift+Tab follow the real tab order: positive `tabindex` first, `tabindex="-1"` and disabled or hidden elements skipped, one stop per radio group. `@onkeydown:preventDefault` is honoured.
* Enter and Space activate buttons and links, and Enter in a text field submits its form.
* `FillAsync` fires `input`, and `change` fires on blur, exactly as in a browser. Code that only reacts to `change` therefore behaves the same in both drivers.
* `ElementReference.FocusAsync()` calls made by components are captured, so focus management in roving-tabindex widgets and dialogs can be tested in memory.

**Stylesheets.** By default, visibility in bUnit comes from attributes and inline styles. Point the driver at your CSS, including Blazor CSS-isolation bundles, and the `display`/`visibility` rules apply too. This uses a real cascade with specificity, `!important`, inline styles and inherited `visibility`. Elements hidden by a class are then hidden from queries, assertions, Tab order and accessibility rules:

```json
// btp.json
{ "bunit": { "stylesheets": ["../MyDesignSystem/wwwroot/app.css", "../MyDesignSystem/obj/Debug/net10.0/scopedcss/bundle/MyDesignSystem.styles.css"] } }
```

You can also add CSS in code: `new BunitStoryOptions().AddStylesheetFile(path)` or `options.Stylesheets.Add(css)`.

`@media` blocks are evaluated against the viewport the in-memory driver pretends to have. The default is `Viewport.Desktop` (1280×800), as in browser runs. Set it with `BunitStoryOptions.Viewport`, `bunit.viewport` in `btp.json`, or `BTP_BUNIT_VIEWPORT` (a preset name or `WIDTHxHEIGHT`). `BunitStoryOptions.Theme` drives `prefers-color-scheme` and applies the theme's decorator. The evaluator supports width and height (`min-`/`max-` and the range syntax, e.g. `(600px <= width < 900px)`), orientation, `prefers-color-scheme`, `prefers-reduced-motion` (`reduce`, as browser runs emulate), `hover`, `pointer`, media types, `not`/`only`, lists, nested `@media` and `@supports`:

```csharp
var phone = new BunitStoryOptions { Viewport = Viewport.Mobile };
await using var session = await BunitStoryRunner.RenderAsync(story, phone);
await session.StoryContext.Expect(session.StoryContext.GetByText("Analytics cookies...")).ToBeHiddenAsync();
```

Limits remain: there is no layout and no real JavaScript (mock it with `MockJS`; see [test data](test-data.md#javascript-interop-mocks)). When a play script depends on them, declare it, and the story is reported as *skipped* for drivers that cannot do it:

```csharp
ctx.Require(DriverCapabilities.Layout, "measuring the popover position");
if (ctx.Supports(DriverCapabilities.RealBrowser)) { ... }
```

## Three drivers, one script

| Driver | Where the play function runs | Used by |
|---|---|---|
| bUnit (`BunitStoryDriver`) | Test process, in memory | `BunitStoryRunner` |
| Playwright (`PlaywrightStoryDriver`) | Test process, driving a real browser | `BrowserStoryRunner` |
| In-app (`InAppStoryDriver`) | Inside the story frame, in the Server circuit or the WebAssembly runtime, driving the real DOM | The workbench **Run interactions** button, and `StoryPageOptions.AutoPlay` |

The in-app driver uses the browser's own activation behaviour (`click()`, focus, form submission) and honours `@onkeydown:preventDefault`. It emulates only what scripts cannot trigger: Tab movement, typing and change-on-blur. The sample's tests run every play script through all three drivers.

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
